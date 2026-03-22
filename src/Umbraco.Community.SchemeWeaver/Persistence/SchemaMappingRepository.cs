using Microsoft.Extensions.Logging;
using Umbraco.Cms.Infrastructure.Scoping;
using Umbraco.Community.SchemeWeaver.Models.Entities;

namespace Umbraco.Community.SchemeWeaver.Persistence;

/// <summary>
/// NPoco-based repository for schema mapping persistence.
/// </summary>
public class SchemaMappingRepository : ISchemaMappingRepository
{
    private readonly IScopeProvider _scopeProvider;
    private readonly ILogger<SchemaMappingRepository> _logger;

    public SchemaMappingRepository(IScopeProvider scopeProvider, ILogger<SchemaMappingRepository> logger)
    {
        _scopeProvider = scopeProvider;
        _logger = logger;
    }

    public IEnumerable<SchemaMapping> GetAll()
    {
        using var scope = _scopeProvider.CreateScope(autoComplete: true);
        return scope.Database.Fetch<SchemaMapping>();
    }

    public SchemaMapping? GetByContentTypeAlias(string contentTypeAlias)
    {
        using var scope = _scopeProvider.CreateScope(autoComplete: true);
        return scope.Database
            .Query<SchemaMapping>()
            .Where(x => x.ContentTypeAlias == contentTypeAlias)
            .FirstOrDefault();
    }

    public SchemaMapping Save(SchemaMapping mapping)
    {
        using var scope = _scopeProvider.CreateScope();

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
        return mapping;
    }

    public void Delete(int id)
    {
        using var scope = _scopeProvider.CreateScope();

        // Delete property mappings first (foreign key constraint)
        scope.Database.Delete<PropertyMapping>("WHERE SchemaMappingId = @0", id);
        scope.Database.Delete<SchemaMapping>(id);

        scope.Complete();
        _logger.LogInformation("Deleted schema mapping {Id}", id);
    }

    public IEnumerable<PropertyMapping> GetPropertyMappings(int schemaMappingId)
    {
        using var scope = _scopeProvider.CreateScope(autoComplete: true);
        return scope.Database
            .Query<PropertyMapping>()
            .Where(x => x.SchemaMappingId == schemaMappingId)
            .ToList();
    }

    public IEnumerable<SchemaMapping> GetInheritedMappings()
    {
        using var scope = _scopeProvider.CreateScope(autoComplete: true);
        return scope.Database
            .Query<SchemaMapping>()
            .Where(x => x.IsInherited && x.IsEnabled)
            .ToList();
    }

    public void SavePropertyMappings(int schemaMappingId, IEnumerable<PropertyMapping> mappings)
    {
        using var scope = _scopeProvider.CreateScope();

        // Remove existing mappings
        scope.Database.Delete<PropertyMapping>("WHERE SchemaMappingId = @0", schemaMappingId);

        // Insert new mappings
        foreach (var mapping in mappings)
        {
            mapping.SchemaMappingId = schemaMappingId;
            scope.Database.Insert(mapping);
        }

        scope.Complete();
        _logger.LogInformation("Saved {Count} property mappings for schema mapping {Id}",
            mappings.Count(), schemaMappingId);
    }
}
