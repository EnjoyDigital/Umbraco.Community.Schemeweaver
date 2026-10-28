using Microsoft.Extensions.Caching.Memory;
using Umbraco.Cms.Infrastructure.Scoping;

namespace Umbraco.Community.SchemeWeaver.Seo.Overrides;

/// <summary>
/// NPoco repository with the same snapshot-cache model as the core mapping repository: the
/// override seam runs for every node, culture and block render, so it must never query per call.
/// Writes evict the snapshot; a short backstop expiry self-heals any out-of-band write.
/// </summary>
public sealed class SeoPageOverrideRepository : ISeoPageOverrideRepository
{
    private const string CacheKey = "schemeweaver:seo:page-overrides:all";
    private static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(5);

    private readonly IScopeProvider _scopeProvider;
    private readonly IMemoryCache _cache;

    public SeoPageOverrideRepository(IScopeProvider scopeProvider, IMemoryCache cache)
    {
        _scopeProvider = scopeProvider;
        _cache = cache;
    }

    private Dictionary<Guid, SeoPageOverride> Snapshot() =>
        _cache.GetOrCreate(CacheKey, entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = CacheDuration;
            using var scope = _scopeProvider.CreateScope(autoComplete: true);
            return scope.Database.Fetch<SeoPageOverride>()
                .GroupBy(o => o.ContentKey)
                .ToDictionary(g => g.Key, g => g.First());
        })!;

    public SeoPageOverride? Get(Guid contentKey) =>
        Snapshot().TryGetValue(contentKey, out var row) ? row.Clone() : null;

    public IReadOnlyCollection<SeoPageOverride> GetAll() =>
        Snapshot().Values.Select(o => o.Clone()).ToList();

    public SeoPageOverride Upsert(SeoPageOverride @override)
    {
        // The scope commits on Dispose, so the snapshot is evicted only after the using block;
        // evicting earlier would let a concurrent read repopulate it from pre-commit rows.
        using (var scope = _scopeProvider.CreateScope())
        {
            var existing = scope.Database.FirstOrDefault<SeoPageOverride>(
                "WHERE ContentKey = @0", @override.ContentKey);

            @override.UpdatedDate = DateTime.UtcNow;
            if (existing is null)
            {
                scope.Database.Insert(@override);
            }
            else
            {
                @override.Id = existing.Id;
                scope.Database.Update(@override);
            }

            scope.Complete();
        }

        ClearCache();
        return @override.Clone();
    }

    public void Delete(Guid contentKey) => DeleteMany([contentKey]);

    public void DeleteMany(IEnumerable<Guid> contentKeys)
    {
        var keys = contentKeys.Distinct().ToList();
        if (keys.Count == 0)
            return;

        using (var scope = _scopeProvider.CreateScope())
        {
            foreach (var key in keys)
            {
                scope.Database.Execute(
                    $"DELETE FROM {SchemeWeaverSeoConstants.Tables.PageOverride} WHERE ContentKey = @0", key);
            }

            scope.Complete();
        }

        ClearCache();
    }

    public void ClearCache() => _cache.Remove(CacheKey);
}
