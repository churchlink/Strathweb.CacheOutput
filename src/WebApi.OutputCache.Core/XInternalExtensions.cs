using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;

namespace WebApi.OutputCache.Core
{
	internal static class XInternalExtensions
	{

		[DebuggerStepThrough]
		public static bool IsNulle(this string s)
		{
			return s == null || s.Length == 0;
		}
		[DebuggerStepThrough]
		public static bool NotNulle(this string s)
		{
			return s != null && s.Length > 0;
		}

		[DebuggerStepThrough]
		public static bool IsNulle<TValue>(this ICollection<TValue> collection)
		{
			return collection == null || collection.Count < 1;
		}
		[DebuggerStepThrough]
		public static bool NotNulle<TValue>(this ICollection<TValue> collection)
		{
			return collection != null && collection.Count > 0;
		}

		[DebuggerStepThrough]
		public static TValue Value<TKey, TValue>(this IDictionary<TKey, TValue> dict, TKey key, TValue defaultVal = default(TValue))
		{
			if (!dict.IsNulle())
			{
				TValue val;
				if (dict.TryGetValue(key, out val))
					return val;
			}
			return defaultVal;
		}

	}
}
