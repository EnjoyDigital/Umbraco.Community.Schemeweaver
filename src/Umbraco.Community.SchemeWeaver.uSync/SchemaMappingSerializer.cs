using System.Xml.Linq;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Umbraco.Community.SchemeWeaver.Models.Entities;
using Umbraco.Community.SchemeWeaver.Persistence;
using Umbraco.Community.SchemeWeaver.Services;
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

    /// <summary>
    /// Evicts the rendered JSON-LD cache after an import-side mapping write.
    /// Imports write through the repository directly and deliberately do NOT
    /// publish <c>SchemaMappingSavedNotification</c> (that would re-trigger the
    /// export handler and loop import → save → export), so the notification-based
    /// eviction never fires for them. Failure to evict must never fail an import.
    /// </summary>
    private void InvalidateJsonLdCache()
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            scope.ServiceProvider.GetRequiredService<IJsonLdBlocksProvider>().InvalidateAll();
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to evict the JSON-LD cache after a uSync mapping import; " +
                "rendered output may be stale until content is republished.");
        }
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

        if (!string.IsNullOrEmpty(item.IdOverride))
            info.Add(new XElement("IdOverride", item.IdOverride));

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

            if (!string.IsNullOrEmpty(pm.TargetPieceKey))
                pmNode.Add(new XElement("TargetPieceKey", pm.TargetPieceKey));

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

        var alias = ElemOr(info, "ContentTypeAlias", string.Empty);

        // Idempotent upsert: find the existing mapping by alias so a re-import
        // updates in place (preserving its DB Id) rather than duplicating.
        var existing = repository.GetByContentTypeAlias(alias);
        var mapping = ReadMapping(info, existing ?? new SchemaMapping());

        // Save first: the returned Id is the FK (SchemaMappingId) the child
        // PropertyMappings need, so it MUST run before they are built.
        var saved = repository.Save(mapping);

        // Full-replace: build the (possibly empty) child set and hand it over
        // unconditionally, so an entry with no <PropertyMappings> clears the
        // existing rows.
        var propertyMappings = node.Element("PropertyMappings")
            ?.Elements("PropertyMapping")
            .Select(pmNode => ReadPropertyMapping(pmNode, saved.Id))
            .ToList()
            ?? new List<PropertyMapping>();

        repository.SavePropertyMappings(saved.Id, propertyMappings);
        InvalidateJsonLdCache();

        return Task.FromResult(SyncAttempt<SchemaMapping>.Succeed(
            alias, saved, ChangeType.Import, new List<uSyncChange>()));
    }

    /// <summary>
    /// Reads the header (<c>&lt;Info&gt;</c>) fields onto <paramref name="target"/>.
    /// Load-bearing defaults: empty string for alias/schema type, <see cref="Guid.Empty"/>
    /// for the key, and <c>false</c> for the booleans (see <see cref="ElemBool"/>).
    /// </summary>
    private static SchemaMapping ReadMapping(XElement info, SchemaMapping target)
    {
        target.ContentTypeAlias = ElemOr(info, "ContentTypeAlias", string.Empty);
        target.ContentTypeKey = ElemGuid(info, "ContentTypeKey");
        target.SchemaTypeName = ElemOr(info, "SchemaTypeName", string.Empty);
        target.IsEnabled = ElemBool(info, "IsEnabled");
        target.IsInherited = ElemBool(info, "IsInherited");
        target.IdOverride = Elem(info, "IdOverride");
        return target;
    }

    /// <summary>
    /// Projects a single <c>&lt;PropertyMapping&gt;</c> node onto a
    /// <see cref="PropertyMapping"/>, wiring <paramref name="mappingId"/> as the FK.
    /// A missing <c>&lt;SourceType&gt;</c> defaults to
    /// <see cref="SchemeWeaverConstants.SourceTypes.Property"/> ("property"), not empty.
    /// </summary>
    private static PropertyMapping ReadPropertyMapping(XElement pmNode, int mappingId) => new()
    {
        SchemaMappingId = mappingId,
        SchemaPropertyName = ElemOr(pmNode, "SchemaPropertyName", string.Empty),
        SourceType = ElemOr(pmNode, "SourceType", SchemeWeaverConstants.SourceTypes.Property),
        ContentTypePropertyAlias = Elem(pmNode, "ContentTypePropertyAlias"),
        SourceContentTypeAlias = Elem(pmNode, "SourceContentTypeAlias"),
        TransformType = Elem(pmNode, "TransformType"),
        IsAutoMapped = ElemBool(pmNode, "IsAutoMapped"),
        StaticValue = Elem(pmNode, "StaticValue"),
        NestedSchemaTypeName = Elem(pmNode, "NestedSchemaTypeName"),
        ResolverConfig = Elem(pmNode, "ResolverConfig"),
        DynamicRootConfig = Elem(pmNode, "DynamicRootConfig"),
        TargetPieceKey = Elem(pmNode, "TargetPieceKey"),
    };

    /// <summary>Child element text, or <paramref name="fallback"/> when the element is absent.</summary>
    private static string ElemOr(XElement node, string name, string fallback) =>
        node.Element(name)?.Value ?? fallback;

    /// <summary>Child element text, or <c>null</c> when the element is absent (nullable columns).</summary>
    private static string? Elem(XElement node, string name) =>
        node.Element(name)?.Value;

    /// <summary>Child element parsed as a bool, defaulting to <c>false</c> when absent.</summary>
    private static bool ElemBool(XElement node, string name) =>
        bool.Parse(node.Element(name)?.Value ?? "false");

    /// <summary>Child element parsed as a <see cref="Guid"/>, defaulting to <see cref="Guid.Empty"/> when absent.</summary>
    private static Guid ElemGuid(XElement node, string name) =>
        Guid.Parse(node.Element(name)?.Value ?? Guid.Empty.ToString());

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
        InvalidateJsonLdCache();
        return Task.CompletedTask;
    }

    public override Task DeleteItemAsync(SchemaMapping item)
    {
        var repository = CreateRepository();
        repository.Delete(item.Id);
        InvalidateJsonLdCache();
        return Task.CompletedTask;
    }
}
