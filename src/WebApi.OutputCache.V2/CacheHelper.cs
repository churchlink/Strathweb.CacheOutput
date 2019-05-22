using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Linq.Expressions;
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

	/// <summary>
	/// Add this as a variable on your ApiController to get easy
	/// access to Cache functions, properties and variables.
	/// <example><code><![CDATA[
	/// #region CacheHelper
	/// CacheHelper _cache;
	/// CacheHelper Cache
	/// {
	/// 	get { if (_cache == null) _cache = new CacheHelper(this); return _cache; }
	/// }
	/// #endregion
	/// ]]></code></example>
	/// </summary>
	public class CacheHelper
	{
		ApiController controller;
		IApiOutputCache _cache;
		ICacheKeyGenerator _cacheKeyGenerator;
		CacheOutputConfiguration _config;
		public Type CacheKeyGeneratorType { get; set; }

		public CacheHelper(ApiController controller)
		{
			this.controller = controller;
		}

		public IApiOutputCache Cache
		{
			get
			{
				if (_cache == null)
					_cache = Config.GetCacheOutputProvider(controller.Request);
				return _cache;
			}
		}

		public CacheOutputConfiguration Config
		{
			get
			{
				if (_config == null)
					_config = controller.Configuration.CacheOutputConfiguration();
				return _config;
			}
		}

		public string Keys
		{
			get
			{
				return string.Join("\r\n", Cache.AllKeys.OrderBy(i => i));
			}
		}

		#region ======= MakeBaseCachekey =======

		public string GetBaseCacheKey(string controller, string action, Dictionary<string, object> actionArgs, params string[] cacheKeyArgs)
		{
			return BaseCacheKeyGenerator.GetKey(controller, action, actionArgs, cacheKeyArgs);
		}

		public string GetBaseCacheKey(string controller, string action, string baseKeyExtension = null)
		{
			return BaseCacheKeyGenerator.GetKey(controller, action, baseKeyExtension);
		}

		public string GetBaseCacheKey<T, U>(Expression<Func<T, U>> expression)
		{
			return BaseCacheKeyGeneratorWebApi.GetKey(expression);
		}


#if WebApi5_1
		// controller.ActionContext only exists on newer, 5.1+ WebApi versions

		/// <summary></summary>
		/// <param name="cacheKeyActionArgs">
		/// These values will be the same as what was set
		/// for CacheOutput(CacheArgs="..."), though here as separate strings
		/// (not comma separated).</param>
		public string GetBaseCacheKey(HttpActionContext context, params string[] cacheKeyActionArgs)
		{
			return BaseCacheKeyGenerator.GetKey(controller.ActionContext, cacheKeyActionArgs);
		}

#else

		/// <summary></summary>
		/// <param name="context">You only need to send this in in pre WebApi 5.1 
		/// versions. On newer ones we can get this from the controller (controller.ActionContext).</param>
		/// <param name="cacheKeyActionArgs">
		/// These values will be the same as what was set
		/// for CacheOutput(CacheArgs="..."), though here as separate strings
		/// (not comma separated).</param>
		public string GetBaseCacheKey(HttpActionContext context, params string[] cacheKeyActionArgs)
		{
			return BaseCacheKeyGeneratorWebApi.GetKey(context, cacheKeyActionArgs);
		}

#endif
		#endregion

		public ICacheKeyGenerator CacheKeyGenerator
		{
			get
			{
				if (_cacheKeyGenerator == null)
					_cacheKeyGenerator = Config.GetCacheKeyGenerator(controller.Request, CacheKeyGeneratorType);
				return _cacheKeyGenerator;
			}
		}

		public void RemoveCacheKeys(string baseKey)
		{
			if (baseKey.NotNulle())
			{
				Cache.RemoveStartsWith(baseKey);
				//(FeedsController t) => t.GetRSSFeed(feedId)));
			}
		}

		public string RemoveCacheKeysWithBeforeAfterKeys(string baseKey)
		{
			string origKeys = Keys;

			RemoveCacheKeys(baseKey);

			return "=== Orig Keys ===\r\n" + origKeys + "\r\n\r\n=== After Keys ===\r\n" + Keys;
		}

	}
}