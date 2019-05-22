using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Web.Http.Controllers;
using WebApi.OutputCache.Core;

namespace WebApi.OutputCache.V2
{
    public class DefaultCacheKeyGenerator : ICacheKeyGenerator
    {
        public virtual string MakeCacheKey(HttpActionContext context, MediaTypeHeaderValue mediaType, bool excludeQueryString = false, string[] baseKeyCacheArgs = null)
        {
            var basekey = BaseCacheKeyGeneratorWebApi.GetKey(context, baseKeyCacheArgs);
            var actionParameters = context.ActionArguments.Where(x => x.Value != null)
                .Select(x => x.Key + "=" + GetValue(x.Value)).ToArray();

            // key renamed: basekey, parameters renamed actionParameters
            string parameters = null;

            if (!excludeQueryString)
            {
                var queryStringParameters =
                    context.Request.GetQueryNameValuePairs()
                           .Where(x => x.Key.ToLower() != "callback")
                           .Select(x => x.Key + "=" + x.Value);
                var parametersCollections = actionParameters.Union(queryStringParameters);
                parameters = string.Join("&", parametersCollections);

                var callbackValue = GetJsonpCallback(context.Request);
                if (!string.IsNullOrWhiteSpace(callbackValue))
                {
                    var callback = "callback=" + callbackValue;
                    if (parameters.Contains("&" + callback)) parameters = parameters.Replace("&" + callback, string.Empty);
                    if (parameters.Contains(callback + "&")) parameters = parameters.Replace(callback + "&", string.Empty);
                    if (parameters.Contains("-" + callback)) parameters = parameters.Replace("-" + callback, string.Empty);
                    if (parameters.EndsWith("&")) parameters = parameters.TrimEnd('&');
                }
            }
            else if (actionParameters.NotNulle())
            {
                parameters = string.Join("&", actionParameters);
            }

            if (parameters == null) parameters = string.Empty;

            // baseKey is now separated from the rest by a ':' not a dash '-', to make the baseKey more evident
            string cachekey = $"{basekey}:{parameters}:{mediaType.MediaType}".ToLower();
            return cachekey;
        }

        public virtual string GetJsonpCallback(HttpRequestMessage request)
        {
            var callback = string.Empty;
            if (request.Method == HttpMethod.Get)
            {
                var query = request.GetQueryNameValuePairs();

                if (query != null)
                {
                    var queryVal = query.FirstOrDefault(x => x.Key.ToLower() == "callback");
                    if (!queryVal.Equals(default(KeyValuePair<string, string>))) callback = queryVal.Value;
                }
            }
            return callback;
        }

        public virtual string GetValue(object val)
        {
            return BaseCacheKeyGenerator.GetValueStatic(val);
        }
    }
}