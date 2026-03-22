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
    void SavePropertyMappings(int schemaMappingId, IEnumerable<PropertyMapping> mappings);
    IEnumerable<SchemaMapping> GetInheritedMappings();
}
