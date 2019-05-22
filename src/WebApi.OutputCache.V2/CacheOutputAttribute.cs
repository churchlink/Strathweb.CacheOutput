using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Formatting;
using System.Net.Http.Headers;
using System.Runtime.ExceptionServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Http;
using System.Web.Http.Controllers;
using System.Web.Http.Filters;
using WebApi.OutputCache.Core;
using WebApi.OutputCache.Core.Cache;
using WebApi.OutputCache.Core.Time;

namespace WebApi.OutputCache.V2
{
    [AttributeUsage(AttributeTargets.Method | AttributeTargets.Class, AllowMultiple = false, Inherited = true)]
    public class CacheOutputAttribute : FilterAttribute, IActionFilter
    {
        private const string CurrentRequestMediaType = "CacheOutput:CurrentRequestMediaType";
        protected static MediaTypeHeaderValue DefaultMediaType = new MediaTypeHeaderValue("application/json");

        /// <summary>
        /// Cache enabled only for requests when Thread.CurrentPrincipal is not set
        /// </summary>
        public bool AnonymousOnly { get; set; }

        /// <summary>
        /// Corresponds to MustRevalidate HTTP header - indicates whether the origin server requires revalidation of a cache entry on any subsequent use when the cache entry becomes stale
        /// </summary>
        public bool MustRevalidate { get; set; }

        /// <summary>
        /// Do not vary cache by querystring values
        /// </summary>
        public bool ExcludeQueryStringFromCacheKey { get; set; }

        /// <summary>
        /// How long response should be cached on the server side (in seconds)
        /// </summary>
        public int ServerTimeSpan { get; set; }

        /// <summary>
        /// Corresponds to CacheControl MaxAge HTTP header (in seconds)
        /// </summary>
        public int ClientTimeSpan { get; set; }

        
        private int? _sharedTimeSpan = null;

        /// <summary>
        /// Corresponds to CacheControl Shared MaxAge HTTP header (in seconds)
        /// </summary>
        public int SharedTimeSpan
        {
            get // required for property visibility
            {
                if (!_sharedTimeSpan.HasValue)
                    throw new Exception("should not be called without value set"); 
                return _sharedTimeSpan.Value;
            }
            set { _sharedTimeSpan = value; }
        }

        /// <summary>
        /// Corresponds to CacheControl NoCache HTTP header
        /// </summary>
        public bool NoCache { get; set; }

        /// <summary>
        /// If true, the ETag will be an MD5 hash of the content.
        /// Else Guid.NewGuid();
        /// </summary>
        public bool HashContentForETag { get; set; }

        string _cacheArgs;
        /// <summary>
        /// List the comma-separated, case-sensitive names of any simple type arguments in 
        /// this method that you want to use for generating the basecachekey. For complex types 
        /// with properties, implement ICacheKey on them in which case this attribute is not needed.
        /// This library already uses any non-null action values on the method to generate 
        /// separate cached responses,
        /// so why is this needed? Because at invalidation time (e.g. when a cached response 
        /// expires), it unfortunately invalidates ALL cached responses for this method, 
        /// even though their action arguments are different (so even though separate 
        /// cached responses were made for an action argument 'id=33' and another for 
        /// 'id=34', if 'id=33' cache expires, it also deletes the 'id=34' cache and etc). 
        /// In many cases this is what you want, particularly when the underlying resource 
        /// being served up by this endpoint coorelates with a single collection. 
        /// However, there are many cases where this is not true. In those cases,
        /// this cache is largely worthless without this new feature. Why? Consider this example:
        /// <para />
        /// Example:
        /// <example><code><![CDATA[
        /// [CacheOutput(CacheArgs = "feedid")]
        /// [HttpGet]
        /// public HttpResponseMessage GetRSSFeed(int feedId = 0) { }
        /// ]]></code>
        /// Let's say this action endpoint, <c>GetRSSFeed</c>, serves up hundreds or 
        /// thousands of different RSS feeds, totally different feeds depending on the 
        /// feedid. Currently, a cache *IS* made for *each* of these feed responses, but
        /// the problem is, when any of them get invalidated, it invalidates *all* of those
        /// caches! With this attribute, the arg 'feedid' will be used in generating the 
        /// basecachekey, which is the linchpin value used for grouping cached responses.
        /// This will allow *only* the cached responses with the inputed feedid to be 
        /// invalidated.
        /// <para/>
        /// Where is the basecachekey finally used? See here: IApiOutputCache.Add, 
        /// which expects a <c>string dependsOnKey</c> argument, which is in fact
        /// the basecachekey.
        /// <para/>
        /// Note: AutoInvalidateCacheOutputAttribute will not work for
        /// action methods decorated with this attribute.
        /// </example>
        /// </summary>
        public string CacheArgs
        {
            get { return _cacheArgs; }
            set
            {
                _cacheArgs = string.IsNullOrWhiteSpace(value) ? null : value.Trim();
                BaseKeyCacheArgs = _cacheArgs == null
                    ? null
                    : _cacheArgs.Split(new char[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
            }
        }

        public string[] BaseKeyCacheArgs { get; internal set; }

        public CacheOutputAttribute()
        {
        }

        public Type CacheKeyGenerator { get; set; }

        private MediaTypeHeaderValue _responseMediaType;

        // cache repository
        private IApiOutputCache _webApiCache;

        protected virtual void EnsureCache(HttpConfiguration config, HttpRequestMessage req)
        {
            _webApiCache = config.CacheOutputConfiguration().GetCacheOutputProvider(req);
        }

        internal IModelQuery<DateTime, CacheTime> CacheTimeQuery;

        readonly Func<HttpActionContext, bool, bool> _isCachingAllowed = (ac, anonymous) =>
        {
            if (anonymous)
            {
                if (Thread.CurrentPrincipal.Identity.IsAuthenticated)
                {
                    return false;
                }
            }

            return ac.Request.Method == HttpMethod.Get;
        };

        protected virtual void EnsureCacheTimeQuery()
        {
            if (CacheTimeQuery == null) ResetCacheTimeQuery();
        }

        protected void ResetCacheTimeQuery()
        {
            CacheTimeQuery = new ShortTime( ServerTimeSpan, ClientTimeSpan, _sharedTimeSpan);
        }

        protected virtual MediaTypeHeaderValue GetExpectedMediaType(HttpConfiguration config, HttpActionContext actionContext)
        {
            MediaTypeHeaderValue responseMediaType = null;

            var negotiator = config.Services.GetService(typeof(IContentNegotiator)) as IContentNegotiator;
            var returnType = actionContext.ActionDescriptor.ReturnType;

            if (negotiator != null && returnType != typeof(HttpResponseMessage))
            {
                var negotiatedResult = negotiator.Negotiate(returnType, actionContext.Request, config.Formatters);
                responseMediaType = negotiatedResult.MediaType;
                responseMediaType.CharSet = Encoding.UTF8.HeaderName;
            }
            else
            {
                if (actionContext.Request.Headers.Accept != null)
                {
                    responseMediaType = actionContext.Request.Headers.Accept.FirstOrDefault();
                    if (responseMediaType == null ||
                         !config.Formatters.Any(x => x.SupportedMediaTypes.Contains(responseMediaType)))
                    {
                        DefaultMediaType.CharSet = Encoding.UTF8.HeaderName;
                        return DefaultMediaType;
                    }
                }
            }

            return responseMediaType;
        }

        private void OnActionExecuting(HttpActionContext actxt)
        {
            if (actxt == null) throw new ArgumentNullException("actxt");

            if (!_isCachingAllowed(actxt, AnonymousOnly)) return;

            var config = actxt.Request.GetConfiguration();

            EnsureCacheTimeQuery();
            EnsureCache(config, actxt.Request);

            var cacheKeyGenerator = config.CacheOutputConfiguration().GetCacheKeyGenerator(actxt.Request, CacheKeyGenerator);

            _responseMediaType = GetExpectedMediaType(config, actxt);
            var cachekey = cacheKeyGenerator.MakeCacheKey(actxt, _responseMediaType, ExcludeQueryStringFromCacheKey, BaseKeyCacheArgs);

            if (!_webApiCache.Contains(cachekey)) return;

            if (actxt.Request.Headers.IfNoneMatch != null)
            {
                var etag = _webApiCache.Get(cachekey + Constants.EtagKey) as string;
                if (etag != null)
                {
                    if (actxt.Request.Headers.IfNoneMatch.Any(x => x.Tag == etag))
                    {
                        var time = CacheTimeQuery.Execute(DateTime.Now);
                        var quickResponse = actxt.Request.CreateResponse(HttpStatusCode.NotModified);
                        ApplyCacheHeaders(quickResponse, time);
                        actxt.Response = quickResponse;
                        return;
                    }
                }
            }

            var val = _webApiCache.Get(cachekey) as byte[];
            if (val == null) return;

            var contenttype = _webApiCache.Get(cachekey + Constants.ContentTypeKey) as string ?? cachekey.Split(':')[1];

            actxt.Response = actxt.Request.CreateResponse();
            actxt.Response.Content = new ByteArrayContent(val);

            actxt.Response.Content.Headers.ContentType = new MediaTypeHeaderValue(contenttype);
            var responseEtag = _webApiCache.Get(cachekey + Constants.EtagKey) as string;
            if (responseEtag != null) SetEtag(actxt.Response, responseEtag);

            var cacheTime = CacheTimeQuery.Execute(DateTime.Now);
            ApplyCacheHeaders(actxt.Response, cacheTime);
        }

        private async Task OnActionExecuted(HttpActionExecutedContext axctxt)
        {
            var ctxt = axctxt.ActionContext;
            if (ctxt.Response == null || !ctxt.Response.IsSuccessStatusCode) return;

            if (!_isCachingAllowed(ctxt, AnonymousOnly)) return;

            var cacheTime = CacheTimeQuery.Execute(DateTime.Now);
            if (cacheTime.AbsoluteExpiration > DateTime.Now)
            {
                var config = axctxt.Request.GetConfiguration().CacheOutputConfiguration();
                var cacheKeyGenerator = config.GetCacheKeyGenerator(axctxt.Request, CacheKeyGenerator);

                string cachekey = cacheKeyGenerator.MakeCacheKey(ctxt, _responseMediaType, ExcludeQueryStringFromCacheKey, BaseKeyCacheArgs);

                if (!string.IsNullOrWhiteSpace(cachekey) && !(_webApiCache.Contains(cachekey)))
                {

                    byte[] content = axctxt.Response.Content == null
                        ? null
                        : await axctxt.Response.Content.ReadAsByteArrayAsync();

                    string etag = !HashContentForETag || content.IsNulle()
                        ? Guid.NewGuid().ToString()
                        : GetMD5Hash(content);

                    SetEtag(axctxt.Response, etag);

                    if (axctxt.Response.Content != null)
                    {
                        string baseKey = BaseCacheKeyGeneratorWebApi.GetKey(ctxt, BaseKeyCacheArgs);

                        _webApiCache.Add(baseKey, string.Empty, cacheTime.AbsoluteExpiration);
                        _webApiCache.Add(cachekey, content, cacheTime.AbsoluteExpiration, baseKey);

                        _webApiCache.Add(cachekey + Constants.ContentTypeKey,
                                        axctxt.Response.Content.Headers.ContentType.MediaType,
                                        cacheTime.AbsoluteExpiration, baseKey);

                        _webApiCache.Add(cachekey + Constants.EtagKey,
                                        axctxt.Response.Headers.ETag.Tag,
                                        cacheTime.AbsoluteExpiration, baseKey);
                    }
                }
            }
            ApplyCacheHeaders(ctxt.Response, cacheTime);
        }

        static TimeSpan md5HashTimeSec;

        /// <summary>
        /// Returns an MD5 hash of data converted to a Base64 string.
        /// What to do if content / data was null or empty? 
        /// Currently, we are not calling this if that is the case, are 
        /// generating a NewGuid(), because returning Guid.Empty could
        /// make resources confused with regard to their headers being 
        /// different headers but both empty content. But maybe that 
        /// would be fine? For now, we're bypassing the issue, empty
        /// content returns a new random Guid (Guid.NewGuid());
        /// </summary>
        /// <param name="data">Data. If null, will be converted to an
        /// empty array before converting.</param>
        public static string GetMD5Hash(byte[] data)
        {
            string result = null;
            var sw = System.Diagnostics.Stopwatch.StartNew();
            if (data == null) data = new byte[0];
            using (var md5 = System.Security.Cryptography.MD5.Create())
            {
                result = Convert.ToBase64String(md5.ComputeHash(data));
            }
            md5HashTimeSec = sw.Elapsed;
            return result;
        }

        private void ApplyCacheHeaders(HttpResponseMessage response, CacheTime cacheTime)
        {
            if (cacheTime.ClientTimeSpan > TimeSpan.Zero || MustRevalidate)
            {
                var cachecontrol = new CacheControlHeaderValue
                {
                    MaxAge = cacheTime.ClientTimeSpan,
                    MustRevalidate = MustRevalidate
                };

                response.Headers.CacheControl = cachecontrol;
            }
            else if (NoCache)
            {
                response.Headers.CacheControl = new CacheControlHeaderValue { NoCache = true };
                response.Headers.Add("Pragma", "no-cache");
            }
        }

        private static void SetEtag(HttpResponseMessage message, string etag)
        {
            if (etag != null)
            {
                var eTag = new EntityTagHeaderValue(@"""" + etag.Replace("\"", string.Empty) + @"""");
                message.Headers.ETag = eTag;
            }
        }

        Task<HttpResponseMessage> IActionFilter.ExecuteActionFilterAsync(HttpActionContext actionContext, CancellationToken cancellationToken, Func<Task<HttpResponseMessage>> continuation)
        {
            if (actionContext == null)
            {
                throw new ArgumentNullException("actionContext");
            }

            if (continuation == null)
            {
                throw new ArgumentNullException("continuation");
            }

            OnActionExecuting(actionContext);

            if (actionContext.Response != null)
            {
                return Task.FromResult(actionContext.Response);
            }

            return CallOnActionExecutedAsync(actionContext, cancellationToken, continuation);
        }

        private async Task<HttpResponseMessage> CallOnActionExecutedAsync(HttpActionContext actionContext, CancellationToken cancellationToken, Func<Task<HttpResponseMessage>> continuation)
        {
            cancellationToken.ThrowIfCancellationRequested();

            HttpResponseMessage response = null;
            Exception exception = null;
            try
            {
                response = await continuation();
            }
            catch (Exception e)
            {
                exception = e;
            }

            try
            {
                var executedContext = new HttpActionExecutedContext(actionContext, exception) { Response = response };
                await OnActionExecuted(executedContext);

                if (executedContext.Response != null)
                {
                    return executedContext.Response;
                }

                if (executedContext.Exception != null)
                {
                    ExceptionDispatchInfo.Capture(executedContext.Exception).Throw();
                }
            }
            catch (Exception e)
            {
                actionContext.Response = null;
                ExceptionDispatchInfo.Capture(e).Throw();
            }

            throw new InvalidOperationException(GetType().Name);
        }

    }
}
