using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Umbraco.Cms.Infrastructure.Scoping;
using Umbraco.Community.SchemeWeaver.Models.Entities;

namespace Umbraco.Community.SchemeWeaver.Persistence;

/// <summary>
/// NPoco-based repository for schema mapping persistence, with an in-memory cache.
///
/// JSON-LD generation reads mappings on the hot path — once per node, per culture, and again
/// per block — so without caching, a single content publish (which re-indexes the published
/// subtree across every culture) fired thousands of identical full-table queries, holding the
/// SQLite write lock long enough to stall publishing for tens of seconds. Mappings only change
/// via the admin Save/Delete/SavePropertyMappings methods below, each of which evicts the cache,
/// so caching is safe and the read storm collapses to two table fetches per cache window.
///
/// Reads return defensive <see cref="SchemaMapping.Clone"/>/<see cref="PropertyMapping.Clone"/>
/// copies so callers that fetch-then-mutate (e.g. SchemeWeaverService.SaveMapping) can never
/// corrupt the shared cached snapshot.
///
/// Caveat: the cache is a local <see cref="IMemoryCache"/>; in a load-balanced setup eviction is
/// per-server. That matches the existing local-cache model of <c>IJsonLdBlocksProvider</c>; a
/// distributed cache refresher would be needed for multi-server invalidation. The short backstop
/// expiry below also self-heals any out-of-band write (e.g. a uSync import writing the tables
/// directly) that bypasses the evicting methods.
/// </summary>
public class SchemaMappingRepository : ISchemaMappingRepository
{
    private const string AllMappingsKey = "schemeweaver:mappings:all";
    private const string AllPropertyMappingsKey = "schemeweaver:mappings:properties:all";

    /// <summary>Backstop expiry; evictions on write keep the cache fresh, this just bounds staleness
    /// from any write path that bypasses this repository (e.g. uSync importing into the tables).</summary>
    private static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(5);

    private readonly IScopeProvider _scopeProvider;
    private readonly IMemoryCache _cache;
    private readonly ILogger<SchemaMappingRepository> _logger;

    public SchemaMappingRepository(
        IScopeProvider scopeProvider,
        IMemoryCache cache,
        ILogger<SchemaMappingRepository> logger)
    {
        _scopeProvider = scopeProvider;
        _cache = cache;
        _logger = logger;
    }

    // -------------------------------------------------------------------------------------------
    // Cached snapshots — every read derives from these two table fetches, so the view is coherent.
    // -------------------------------------------------------------------------------------------

    private List<SchemaMapping> AllMappingsSnapshot() =>
        _cache.GetOrCreate(AllMappingsKey, entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = CacheDuration;
            using var scope = _scopeProvider.CreateScope(autoComplete: true);
            return scope.Database.Fetch<SchemaMapping>();
        })!;

    private IReadOnlyDictionary<int, List<PropertyMapping>> AllPropertyMappingsSnapshot() =>
        _cache.GetOrCreate(AllPropertyMappingsKey, entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = CacheDuration;
            using var scope = _scopeProvider.CreateScope(autoComplete: true);
            return (IReadOnlyDictionary<int, List<PropertyMapping>>)scope.Database
                .Fetch<PropertyMapping>()
                .GroupBy(x => x.SchemaMappingId)
                .ToDictionary(g => g.Key, g => g.ToList());
        })!;

    private void EvictCache()
    {
        _cache.Remove(AllMappingsKey);
        _cache.Remove(AllPropertyMappingsKey);
    }

    /// <inheritdoc />
    public void ClearCache() => EvictCache();

    // -------------------------------------------------------------------------------------------
    // Reads (cached; return defensive copies)
    // -------------------------------------------------------------------------------------------

    public IEnumerable<SchemaMapping> GetAll() =>
        AllMappingsSnapshot().Select(m => m.Clone()).ToList();

    public SchemaMapping? GetByContentTypeAlias(string contentTypeAlias) =>
        AllMappingsSnapshot()
            .FirstOrDefault(x => string.Equals(x.ContentTypeAlias, contentTypeAlias, StringComparison.Ordinal))
            ?.Clone();

    public IEnumerable<PropertyMapping> GetPropertyMappings(int schemaMappingId) =>
        AllPropertyMappingsSnapshot().TryGetValue(schemaMappingId, out var list)
            ? list.Select(p => p.Clone()).ToList()
            : new List<PropertyMapping>();

    public IReadOnlyDictionary<int, List<PropertyMapping>> GetAllPropertyMappingsByMappingId() =>
        AllPropertyMappingsSnapshot()
            .ToDictionary(kvp => kvp.Key, kvp => kvp.Value.Select(p => p.Clone()).ToList());

    public IEnumerable<SchemaMapping> GetInheritedMappings() =>
        AllMappingsSnapshot()
            .Where(x => x.IsInherited && x.IsEnabled)
            .Select(m => m.Clone())
            .ToList();

    // -------------------------------------------------------------------------------------------
    // Writes (evict the cache after the scope commits)
    // -------------------------------------------------------------------------------------------

    public SchemaMapping Save(SchemaMapping mapping)
    {
        using (var scope = _scopeProvider.CreateScope())
        {
            var now = DateTime.UtcNow;
            mapping.UpdatedDate = now;

            if (mapping.Id is 0)
            {
                mapping.CreatedDate = now;
                scope.Database.Insert(mapping);
                _logger.LogInformation("Created schema mapping for {Alias}", mapping.ContentTypeAlias);
            }
            else
            {
                scope.Database.Update(mapping);
                _logger.LogInformation("Updated schema mapping for {Alias}", mapping.ContentTypeAlias);
            }

            scope.Complete();
        }

        EvictCache();
        return mapping;
    }

    public void Delete(int id)
    {
        using (var scope = _scopeProvider.CreateScope())
        {
            // Delete property mappings first (foreign key constraint)
            scope.Database.Delete<PropertyMapping>("WHERE SchemaMappingId = @0", id);
            scope.Database.Delete<SchemaMapping>(id);

            scope.Complete();
        }

        EvictCache();
        _logger.LogInformation("Deleted schema mapping {Id}", id);
    }

    public void SavePropertyMappings(int schemaMappingId, IEnumerable<PropertyMapping> mappings)
    {
        var materialised = mappings.ToList();

        using (var scope = _scopeProvider.CreateScope())
        {
            // Remove existing mappings
            scope.Database.Delete<PropertyMapping>("WHERE SchemaMappingId = @0", schemaMappingId);

            // Insert new mappings
            foreach (var mapping in materialised)
            {
                mapping.SchemaMappingId = schemaMappingId;
                scope.Database.Insert(mapping);
            }

            scope.Complete();
        }

        EvictCache();
        _logger.LogInformation("Saved {Count} property mappings for schema mapping {Id}",
            materialised.Count, schemaMappingId);
    }
}
