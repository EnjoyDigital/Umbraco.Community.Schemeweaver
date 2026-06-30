using Umbraco.Community.SchemeWeaver.Models.Entities;

namespace Umbraco.Community.SchemeWeaver.Persistence;

/// <summary>
/// Repository for Schema.org mapping persistence.
/// </summary>
public interface ISchemaMappingRepository
{
    IEnumerable<SchemaMapping> GetAll();
    SchemaMapping? GetByContentTypeAlias(string contentTypeAlias);
    SchemaMapping Save(SchemaMapping mapping);
    void Delete(int id);
    IEnumerable<PropertyMapping> GetPropertyMappings(int schemaMappingId);

    /// <summary>
    /// Fetches every property mapping in a single query, keyed by the owning
    /// <see cref="PropertyMapping.SchemaMappingId"/>. Use this instead of calling
    /// <see cref="GetPropertyMappings"/> in a loop to avoid N+1 queries when
    /// building DTOs for many mappings at once.
    /// </summary>
    IReadOnlyDictionary<int, List<PropertyMapping>> GetAllPropertyMappingsByMappingId();

    void SavePropertyMappings(int schemaMappingId, IEnumerable<PropertyMapping> mappings);
    IEnumerable<SchemaMapping> GetInheritedMappings();

    /// <summary>
    /// Evicts the in-memory mapping cache. Save/Delete/SavePropertyMappings call this
    /// automatically; call it explicitly after writing the mapping tables out of band
    /// (e.g. a bulk DB import, or a test resetting the tables directly).
    /// </summary>
    void ClearCache();
}
