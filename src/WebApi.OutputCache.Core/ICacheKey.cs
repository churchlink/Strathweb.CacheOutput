
namespace WebApi.OutputCache.Core
{
	/// <summary>
	/// Decorate complex WebApi model types with this interface 
	/// in order to specify how to get a cache key from this model type.
	/// The value will become part of the baseCacheKey in the cache.
	/// This type overrides any other args that were specified on simple types
	/// in the action method.
	/// </summary>
	public interface ICacheKey
	{
		string CacheKey();
	}
}
