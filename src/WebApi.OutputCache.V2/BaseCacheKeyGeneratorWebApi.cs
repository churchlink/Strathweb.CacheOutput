using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Web.Http;
using System.Web.Http.Controllers;
using WebApi.OutputCache.Core;

namespace WebApi.OutputCache.V2
{
	/// <summary>
	/// Class for generating base cache keys, with this class that
	/// inherits from BaseCacheKeyGenerator containing all WebApi
	/// or otherwise System.Web* dependencies. Note that ultimately,
	/// those dependencies are not core to how the key is generated,
	/// they are only needed in these methods to help in implicitly
	/// obtaining the Controller and Method name (string values) for 
	/// instance. 
	/// </summary>
	public class BaseCacheKeyGeneratorWebApi : BaseCacheKeyGenerator
	{

		public static string GetKey(HttpActionContext context, params string[] args)
		{
			return GetKey(
				context.ControllerContext.ControllerDescriptor.ControllerName,
				context.ActionDescriptor.ActionName,
				context.ActionArguments,
				args);
		}

		public static string GetKey<T, U>(Expression<Func<T, U>> expression)
		{
			var method = expression.Body as MethodCallExpression;
			if (method == null) throw new ArgumentException("Expression is wrong");

			var methodName = method.Method.Name;
			var nameAttribs = method.Method.GetCustomAttributes(typeof(ActionNameAttribute), false);
			if (nameAttribs.Any()) {
				var actionNameAttrib = (ActionNameAttribute)nameAttribs.FirstOrDefault();
				if (actionNameAttrib != null) {
					methodName = actionNameAttrib.Name;
				}
			}
			string controller = typeof(T).Name.Replace("Controller", string.Empty);
			return GetKey(controller, methodName);
		}

	}
}