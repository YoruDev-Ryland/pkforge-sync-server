using Microsoft.Extensions.Caching.Memory;

namespace PKForge.Sync.Server.Web.Details;

/// <summary>Parsed inspector details by blob hash. Its own size-limited cache, so the limit (which makes every
/// entry state a size) doesn't apply to the app-wide <see cref="IMemoryCache"/>.</summary>
public sealed class DetailsCache : IDisposable
{
    private static readonly TimeSpan Sliding = TimeSpan.FromHours(1);
    private readonly MemoryCache cache = new(new MemoryCacheOptions { SizeLimit = 20_000 });

    public MonDetails? Get(string blob) => cache.TryGetValue(blob, out MonDetails? details) ? details : null;

    public void Set(string blob, MonDetails details) =>
        cache.Set(blob, details, new MemoryCacheEntryOptions { SlidingExpiration = Sliding, Size = 1 });

    public void Dispose() => cache.Dispose();
}
