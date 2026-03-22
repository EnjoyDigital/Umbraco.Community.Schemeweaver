using System.Xml.Linq;
using Microsoft.Extensions.Logging;
using Umbraco.Community.SchemeWeaver.Models.Entities;
using Umbraco.Community.SchemeWeaver.Persistence;
using uSync.Core;
using uSync.Core.Models;
using uSync.Core.Serialization;

namespace Umbraco.Community.SchemeWeaver.uSync;

/// <summary>
/// uSync serializer for SchemeWeaver schema mappings.
/// Exports and imports SchemaMapping + PropertyMapping entities to/from XML.
/// </summary>
[SyncSerializer("D6F5E8A2-3B4C-4D5E-9F6A-7B8C9D0E1F2A", "SchemeWeaver Mapping Serializer", SchemeWeaverMappingConstants.ItemType)]
public class SchemaMappingSerializer : SyncSerializerRoot<SchemaMapping>, ISyncSerializer<SchemaMapping>
{
    private readonly ISchemaMappingRepository _repository;

    public SchemaMappingSerializer(
        ISchemaMappingRepository repository,
        ILogger<SchemaMappingSerializer> logger)
        : base(logger)
    {
        _repository = repository;
    }

    public override string ItemAlias(SchemaMapping item) => item.ContentTypeAlias;

    public override Guid ItemKey(SchemaMapping item) => item.ContentTypeKey;

    protected override Task<SyncAttempt<XElement>> SerializeCoreAsync(SchemaMapping item, SyncSerializerOptions options)
    {
        var node = InitializeBaseNode(item, item.ContentTypeAlias);

        var info = new XElement("Info",
            new XElement("ContentTypeAlias", item.ContentTypeAlias),
            new XElement("ContentTypeKey", item.ContentTypeKey),
            new XElement("SchemaTypeName", item.SchemaTypeName),
            new XElement("IsEnabled", item.IsEnabled));

        node.Add(info);

        var propertyMappings = _repository.GetPropertyMappings(item.Id);
        var propertyMappingsNode = new XElement("PropertyMappings");

        foreach (var pm in propertyMappings)
        {
            var pmNode = new XElement("PropertyMapping",
                new XElement("SchemaPropertyName", pm.SchemaPropertyName),
                new XElement("SourceType", pm.SourceType),
                new XElement("IsAutoMapped", pm.IsAutoMapped));

            if (!string.IsNullOrEmpty(pm.ContentTypePropertyAlias))
                pmNode.Add(new XElement("ContentTypePropertyAlias", pm.ContentTypePropertyAlias));

            if (!string.IsNullOrEmpty(pm.SourceContentTypeAlias))
                pmNode.Add(new XElement("SourceContentTypeAlias", pm.SourceContentTypeAlias));

            if (!string.IsNullOrEmpty(pm.TransformType))
                pmNode.Add(new XElement("TransformType", pm.TransformType));

            if (!string.IsNullOrEmpty(pm.StaticValue))
                pmNode.Add(new XElement("StaticValue", pm.StaticValue));

            if (!string.IsNullOrEmpty(pm.NestedSchemaTypeName))
                pmNode.Add(new XElement("NestedSchemaTypeName", pm.NestedSchemaTypeName));

            if (!string.IsNullOrEmpty(pm.ResolverConfig))
                pmNode.Add(new XElement("ResolverConfig", new XCData(pm.ResolverConfig)));

            propertyMappingsNode.Add(pmNode);
        }

        node.Add(propertyMappingsNode);

        return Task.FromResult(SyncAttempt<XElement>.Succeed(
            item.ContentTypeAlias, node, ChangeType.Export, new List<uSyncChange>()));
    }

    protected override Task<SyncAttempt<SchemaMapping>> DeserializeCoreAsync(XElement node, SyncSerializerOptions options)
    {
        var info = node.Element("Info");
        if (info is null)
            return Task.FromResult(SyncAttempt<SchemaMapping>.Fail(
                node.Name.LocalName, ChangeType.Fail, "Missing Info element"));

        var alias = info.Element("ContentTypeAlias")?.Value ?? string.Empty;
        var key = Guid.Parse(info.Element("ContentTypeKey")?.Value ?? Guid.Empty.ToString());
        var schemaTypeName = info.Element("SchemaTypeName")?.Value ?? string.Empty;
        var isEnabled = bool.Parse(info.Element("IsEnabled")?.Value ?? "false");

        // Find existing or create new
        var existing = _repository.GetByContentTypeAlias(alias);
        var mapping = existing ?? new SchemaMapping();

        mapping.ContentTypeAlias = alias;
        mapping.ContentTypeKey = key;
        mapping.SchemaTypeName = schemaTypeName;
        mapping.IsEnabled = isEnabled;

        var saved = _repository.Save(mapping);

        // Deserialize property mappings
        var propertyMappingsNode = node.Element("PropertyMappings");
        var propertyMappings = new List<PropertyMapping>();

        if (propertyMappingsNode is not null)
        {
            foreach (var pmNode in propertyMappingsNode.Elements("PropertyMapping"))
            {
                var pm = new PropertyMapping
                {
                    SchemaMappingId = saved.Id,
                    SchemaPropertyName = pmNode.Element("SchemaPropertyName")?.Value ?? string.Empty,
                    SourceType = pmNode.Element("SourceType")?.Value ?? "property",
                    ContentTypePropertyAlias = pmNode.Element("ContentTypePropertyAlias")?.Value,
                    SourceContentTypeAlias = pmNode.Element("SourceContentTypeAlias")?.Value,
                    TransformType = pmNode.Element("TransformType")?.Value,
                    IsAutoMapped = bool.Parse(pmNode.Element("IsAutoMapped")?.Value ?? "false"),
                    StaticValue = pmNode.Element("StaticValue")?.Value,
                    NestedSchemaTypeName = pmNode.Element("NestedSchemaTypeName")?.Value,
                    ResolverConfig = pmNode.Element("ResolverConfig")?.Value,
                };

                propertyMappings.Add(pm);
            }
        }

        _repository.SavePropertyMappings(saved.Id, propertyMappings);

        return Task.FromResult(SyncAttempt<SchemaMapping>.Succeed(
            alias, saved, ChangeType.Import, new List<uSyncChange>()));
    }

    public override Task<SchemaMapping?> FindItemAsync(Guid key)
    {
        var all = _repository.GetAll();
        var item = all.FirstOrDefault(m => m.ContentTypeKey == key);
        return Task.FromResult(item);
    }

    public override Task<SchemaMapping?> FindItemAsync(string alias)
    {
        var item = _repository.GetByContentTypeAlias(alias);
        return Task.FromResult(item);
    }

    public override Task SaveItemAsync(SchemaMapping item)
    {
        _repository.Save(item);
        return Task.CompletedTask;
    }

    public override Task DeleteItemAsync(SchemaMapping item)
    {
        _repository.Delete(item.Id);
        return Task.CompletedTask;
    }
}
