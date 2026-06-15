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
}
