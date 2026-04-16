using System.Xml.Linq;
using Microsoft.Extensions.DependencyInjection;
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
/// Uses <see cref="IServiceScopeFactory"/> to resolve the scoped
/// <see cref="ISchemaMappingRepository"/> on demand, because uSync registers
/// serializers as singletons.
/// </summary>
[SyncSerializer("D6F5E8A2-3B4C-4D5E-9F6A-7B8C9D0E1F2A", "SchemeWeaver Mapping Serializer", SchemeWeaverMappingConstants.ItemType)]
public class SchemaMappingSerializer : SyncSerializerRoot<SchemaMapping>, ISyncSerializer<SchemaMapping>
{
    private readonly IServiceScopeFactory _scopeFactory;

    public SchemaMappingSerializer(
        IServiceScopeFactory scopeFactory,
        ILogger<SchemaMappingSerializer> logger)
        : base(logger)
    {
        _scopeFactory = scopeFactory;
    }

    private ISchemaMappingRepository CreateRepository()
    {
        var scope = _scopeFactory.CreateScope();
        return scope.ServiceProvider.GetRequiredService<ISchemaMappingRepository>();
    }

    public override string ItemAlias(SchemaMapping item) => item.ContentTypeAlias;

    public override Guid ItemKey(SchemaMapping item) => item.ContentTypeKey;

    protected override Task<SyncAttempt<XElement>> SerializeCoreAsync(SchemaMapping item, SyncSerializerOptions options)
    {
        var repository = CreateRepository();
        var node = InitializeBaseNode(item, item.ContentTypeAlias);

        var info = new XElement("Info",
            new XElement("ContentTypeAlias", item.ContentTypeAlias),
            new XElement("ContentTypeKey", item.ContentTypeKey),
            new XElement("SchemaTypeName", item.SchemaTypeName),
            new XElement("IsEnabled", item.IsEnabled),
            new XElement("IsInherited", item.IsInherited));

        node.Add(info);

        var propertyMappings = repository.GetPropertyMappings(item.Id);
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

            if (!string.IsNullOrEmpty(pm.DynamicRootConfig))
                pmNode.Add(new XElement("DynamicRootConfig", new XCData(pm.DynamicRootConfig)));

            propertyMappingsNode.Add(pmNode);
        }

        node.Add(propertyMappingsNode);

        return Task.FromResult(SyncAttempt<XElement>.Succeed(
            item.ContentTypeAlias, node, ChangeType.Export, new List<uSyncChange>()));
    }

    protected override Task<SyncAttempt<SchemaMapping>> DeserializeCoreAsync(XElement node, SyncSerializerOptions options)
    {
        var repository = CreateRepository();

        var info = node.Element("Info");
        if (info is null)
            return Task.FromResult(SyncAttempt<SchemaMapping>.Fail(
                node.Name.LocalName, ChangeType.Fail, "Missing Info element"));

        var alias = info.Element("ContentTypeAlias")?.Value ?? string.Empty;
        var key = Guid.Parse(info.Element("ContentTypeKey")?.Value ?? Guid.Empty.ToString());
        var schemaTypeName = info.Element("SchemaTypeName")?.Value ?? string.Empty;
        var isEnabled = bool.Parse(info.Element("IsEnabled")?.Value ?? "false");
        var isInherited = bool.Parse(info.Element("IsInherited")?.Value ?? "false");

        // Find existing or create new
        var existing = repository.GetByContentTypeAlias(alias);
        var mapping = existing ?? new SchemaMapping();

        mapping.ContentTypeAlias = alias;
        mapping.ContentTypeKey = key;
        mapping.SchemaTypeName = schemaTypeName;
        mapping.IsEnabled = isEnabled;
        mapping.IsInherited = isInherited;

        var saved = repository.Save(mapping);

        // Deserialize property mappings
        var propertyMappingsNode = node.Element("PropertyMappings");
        var propertyMappings = new List<PropertyMapping>();

        if (propertyMappingsNode is not null)
        {
            propertyMappings.AddRange(propertyMappingsNode.Elements("PropertyMapping").Select(pmNode => new PropertyMapping
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
                DynamicRootConfig = pmNode.Element("DynamicRootConfig")?.Value,
            }));
        }

        repository.SavePropertyMappings(saved.Id, propertyMappings);

        return Task.FromResult(SyncAttempt<SchemaMapping>.Succeed(
            alias, saved, ChangeType.Import, new List<uSyncChange>()));
    }

    public override Task<SchemaMapping?> FindItemAsync(Guid key)
    {
        var repository = CreateRepository();
        var all = repository.GetAll();
        var item = all.FirstOrDefault(m => m.ContentTypeKey == key);
        return Task.FromResult(item);
    }

    public override Task<SchemaMapping?> FindItemAsync(string alias)
    {
        var repository = CreateRepository();
        var item = repository.GetByContentTypeAlias(alias);
        return Task.FromResult(item);
    }

    public override Task SaveItemAsync(SchemaMapping item)
    {
        var repository = CreateRepository();
        repository.Save(item);
        return Task.CompletedTask;
    }

    public override Task DeleteItemAsync(SchemaMapping item)
    {
        var repository = CreateRepository();
        repository.Delete(item.Id);
        return Task.CompletedTask;
    }
}
