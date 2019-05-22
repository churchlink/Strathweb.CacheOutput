using System;
using System.Net.Http;
using System.Web.Http.Filters;
using WebApi.OutputCache.Core;

namespace WebApi.OutputCache.V2
{
    [AttributeUsage(AttributeTargets.Method, AllowMultiple = true, Inherited = true)]
    public sealed class InvalidateCacheOutputAttribute : BaseCacheAttribute
    {
        private string _controller;
        private readonly string _methodName;

        string _cacheArgs;

        /// <summary>
        /// List of cache args (for simple types) that need invalidated, that were formerly 
        /// included in the basecachekey. See fuller notes on CacheOutputAttribute.CacheArgs.
        /// For complex types instead implement ICacheKey on them in which case this 
        /// attribute is not needed.
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

        public InvalidateCacheOutputAttribute(string methodName)
            : this(methodName, null)
        {
        }

        public InvalidateCacheOutputAttribute(string methodName, Type type = null)
        {
            _controller = type != null ? type.Name.Replace("Controller", string.Empty) : null;
            // compare orig: _controller = type != null ? type.FullName : null;
            _methodName = methodName;
        }

        public override void OnActionExecuted(HttpActionExecutedContext actionExecutedContext)
        {
            if (actionExecutedContext.Response != null && !actionExecutedContext.Response.IsSuccessStatusCode) return;
            _controller = _controller ?? actionExecutedContext.ActionContext.ControllerContext.ControllerDescriptor.ControllerName; // compare: ...ControllerDescriptor.ControllerType.FullName;

            EnsureCache(actionExecutedContext.Request.GetConfiguration(), actionExecutedContext.Request);

            //axctxt.Request.GetConfiguration().CacheOutputConfiguration()
            var basekey = BaseCacheKeyGenerator.GetKey(_controller, _methodName, actionExecutedContext.ActionContext.ActionArguments, BaseKeyCacheArgs);

            if (WebApiCache.Contains(basekey)) // is this a waste? pry not needed, so long as remove gracefully handles a non-existent key
                WebApiCache.RemoveStartsWith(basekey);
        }
    }
}