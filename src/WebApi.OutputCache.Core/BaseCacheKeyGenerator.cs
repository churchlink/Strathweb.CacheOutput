using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;

namespace WebApi.OutputCache.Core
{
	public class BaseCacheKeyGenerator
	{
		public static string GetKey(string controller, string action, string baseKeyExtension = null)
		{
			string key = baseKeyExtension.IsNulle()
				? string.Format("{0}-{1}", controller, action)
				: string.Format("{0}-{1}[{2}]", controller, action, baseKeyExtension);

			return key.ToLower();
		}

		public static string GetKey(string controller, string action, Dictionary<string, object> actionArgs, params string[] cacheKeyArgs)
		{
			string ext = null;
			// If there are no actionArgs, then continue
			if (actionArgs.NotNulle())
			{ 
				// --STEP #1: 
				// For complex types, ICacheKey is the bomb, doesn't even require
				// CacheArgs atrribute to be set, making things really easy. It does however
				// mean we have to iterate through actionArgs looking for any that are of type ICacheKey

				ICacheKey ck = actionArgs.Select(d => d.Value as ICacheKey).FirstOrDefault(v => v != null);
				if (ck != null)
					ext = ck.CacheKey();
				else if (cacheKeyArgs.NotNulle())
				{
					// --STEP #2: 
					// None of the args are actionArs; we only get here if there ARE cacheKey
					// values that were inputted (in the attr).

					for (int i = 0; i < cacheKeyArgs.Length; i++)
					{
						string arg = cacheKeyArgs[i];
						string v = GetValueStatic(actionArgs.Value(arg));
						if (v.NotNulle())
							ext += (ext == null ? null : "&") + arg + '=' + v;
					}
				}
			}
			return GetKey(controller, action, ext);
		}

		/// <summary>
		/// Converts the value type to string. 
		/// Null returns null, 
		/// Type ICacheKey returns the string result,
		/// Type IEnumerable (minus string) aggregates the values to a string,
		/// Else returns val.ToString.
		/// </summary>
		public static string GetValueStatic(object val)
		{
			// --- What about complex types with properties? e.g. where argname=="model.WidgetId"? ---

			// #1) ?Reflection?: see commented code below, but 'we hates reflection'
			// #2) ?Have type simply override ToString? But 1) that also requires messing with 
			//			the POCO just for this purpose, 2) is not clearly defined, 3) we can't know when 
			//			a type intended this, ... lots of problems, so might as well do the interface route
			// #3) SOLUTION: New interface ICacheKey! Performant, simple, clearly defined, and complex 
			//			types for api are typically 'View' specific / non-domain types anyways 
			//			(making it a small-issue having a dependency on this library's interface for the 
			//			model type.)

			if (val == null)
				return null;

			if (val is ICacheKey)
				return (val as ICacheKey).CacheKey();

			if (val is IEnumerable && !(val is string)) {
				var concatValue = string.Empty;
				var paramArray = val as IEnumerable;
				return paramArray.Cast<object>().Aggregate(concatValue, (current, paramValue) => current + (paramValue + ";"));
			}

			// SCRATCHED: REFLECTION
			// val.GetType().GetProperty(argname.GetPropertyNameAfterPeriod()).GetValue(val, null).ToString();

			return val.ToString();
		}

	}
}