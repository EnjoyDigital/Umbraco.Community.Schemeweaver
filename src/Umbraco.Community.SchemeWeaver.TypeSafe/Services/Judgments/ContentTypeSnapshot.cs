using System.Text.Json.Serialization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Umbraco.Cms.Core.Services;
using Umbraco.Community.SchemeWeaver.Models.Api;
using Umbraco.Community.SchemeWeaver.Services;

namespace Umbraco.Community.SchemeWeaver.TypeSafe.Services.Judgments;

/// <summary>
/// What the model is allowed to know about one Umbraco content type: its properties (with
/// editors), the four built-ins, and every Block List/Grid property described BY ITS
/// CONTENTS. The snapshot is the shared <c>state</c> of every request in a mapper call and
/// the only source of the aliases a mapping may reference, so nothing here is invented.
/// </summary>
/// <remarks>
/// A block property described only as <c>sections (Umbraco.BlockList)</c> is an opaque
/// name; the eval harness showed a body-sections container never binds to
/// <c>mainEntity</c>/<c>hasPart</c> until its element types and their fields are in the
/// state. Element types come from <see cref="ISchemeWeaverService.GetBlockElementTypesAsync"/>,
/// resolved lazily because <c>SchemeWeaverService</c> depends on <c>ISchemaAutoMapper</c>,
/// which this satellite decorates — eager injection is a DI cycle (the AI satellite's
/// <c>AISchemaMapper</c> documents the same trap).
/// </remarks>
internal sealed class ContentTypeSnapshot
{
    private static HashSet<string> BlockEditorAliases => SchemeWeaverConstants.PropertyEditors.BlockEditorAliases;

    private ContentTypeSnapshot(string alias, string name, string? description, IReadOnlyList<SnapshotProperty> properties)
    {
        Alias = alias;
        Name = name;
        Description = description;
        Properties = properties;
    }

    public string Alias { get; }

    public string Name { get; }

    public string? Description { get; }

    /// <summary>Composition properties (inherited ones included) followed by the four built-ins.</summary>
    public IReadOnlyList<SnapshotProperty> Properties { get; }

    public SnapshotProperty? Find(string alias)
        => Properties.FirstOrDefault(p => string.Equals(p.Alias, alias, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// The inner fields across a block property's element types, de-duplicated by alias
    /// (the first element type declaring an alias names it). Empty for a non-block property.
    /// </summary>
    public IReadOnlyList<BlockInnerProperty> BlockInnerProperties(string propertyAlias)
    {
        var property = Find(propertyAlias);
        if (property is null || !property.IsBlock)
            return [];

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var result = new List<BlockInnerProperty>();
        foreach (var element in property.BlockTypes)
        {
            foreach (var inner in element.PropertyInfos)
            {
                if (seen.Add(inner.Alias))
                {
                    result.Add(new BlockInnerProperty(
                        inner.Alias,
                        string.IsNullOrEmpty(inner.Name) ? inner.Alias : inner.Name,
                        inner.EditorAlias,
                        element.Alias,
                        string.IsNullOrEmpty(element.Name) ? element.Alias : element.Name));
                }
            }
        }

        return result;
    }

    /// <summary>One-line summary of a block property's contents for question text.</summary>
    public string SummariseBlocks(string propertyAlias)
    {
        var property = Find(propertyAlias);
        if (property is null || property.BlockTypes.Count == 0)
            return string.Empty;

        var parts = property.BlockTypes.Select(el =>
            $"\"{(string.IsNullOrEmpty(el.Name) ? el.Alias : el.Name)}\" (fields: {string.Join(", ", el.PropertyInfos.Select(ip => ip.Alias))})");
        return $" It is a repeating list; each entry is one of these block types: {string.Join("; ", parts)}.";
    }

    /// <summary>
    /// The shared request state. <paramref name="targetSchemaType"/> is set for a property
    /// mapping and left out for schema-type suggestion, which is choosing it.
    /// </summary>
    public object ToState(string? targetSchemaType)
        => new StateShape(
            new ContentTypeShape(
                Alias,
                Name,
                string.IsNullOrWhiteSpace(Description) ? null : Description,
                Properties.Select(p => p.ToState()).ToList()),
            targetSchemaType,
            "Built-in properties are prefixed with __: __name is the node name, __url its URL, "
            + "__createDate and __updateDate its timestamps.");

    /// <summary>
    /// Snapshots a content type, or returns <c>null</c> when the alias is unknown. Block
    /// introspection failures degrade to "no element types" with a Debug log — a thinner
    /// state, never an error.
    /// </summary>
    public static async Task<ContentTypeSnapshot?> BuildAsync(
        IContentTypeService contentTypeService,
        IServiceProvider serviceProvider,
        string contentTypeAlias,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        var contentType = contentTypeService.Get(contentTypeAlias);
        if (contentType is null)
            return null;

        ISchemeWeaverService? schemeWeaverService = null;
        IServiceScope? scope = null;
        try
        {
            var properties = new List<SnapshotProperty>();

            // CompositionPropertyTypes, NOT PropertyTypes: composition-inherited properties
            // (a shared "Hero" tab) were invisible to the heuristic until 17.8.2/18.3.2.
            foreach (var property in contentType.CompositionPropertyTypes)
            {
                cancellationToken.ThrowIfCancellationRequested();

                IReadOnlyList<BlockElementTypeInfo> blockTypes = [];
                if (BlockEditorAliases.Contains(property.PropertyEditorAlias))
                {
                    try
                    {
                        schemeWeaverService ??= ResolveSchemeWeaverService(serviceProvider, ref scope);
                        blockTypes = (await schemeWeaverService
                            .GetBlockElementTypesAsync(contentType.Alias, property.Alias)
                            .ConfigureAwait(false)).ToList();
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException)
                    {
                        // Boundary catch by policy: a failed block introspection must not fail
                        // the mapping; the block is then described by name alone.
                        logger.LogDebug(ex,
                            "Block element types for {ContentType}.{Property} unavailable; describing the block by name only",
                            contentType.Alias, property.Alias);
                    }
                }

                properties.Add(new SnapshotProperty(
                    property.Alias,
                    string.IsNullOrEmpty(property.Name) ? property.Alias : property.Name,
                    property.PropertyEditorAlias,
                    string.IsNullOrWhiteSpace(property.Description) ? null : property.Description,
                    blockTypes));
            }

            foreach (var (alias, displayName, editorAlias) in SchemeWeaverConstants.BuiltInProperties.All)
                properties.Add(new SnapshotProperty(alias, displayName, editorAlias, null, []));

            return new ContentTypeSnapshot(
                contentType.Alias,
                string.IsNullOrEmpty(contentType.Name) ? contentType.Alias : contentType.Name,
                contentType.Description,
                properties);
        }
        finally
        {
            scope?.Dispose();
        }
    }

    /// <summary>
    /// Resolves <see cref="ISchemeWeaverService"/> at call time. It is scoped in the core;
    /// when this mapper is itself scoped the injected provider hands back the scope's
    /// instance (the AI satellite's pattern). When the mapper is a singleton the injected
    /// provider is the root and scope validation refuses, so a child scope is opened
    /// instead and disposed with the snapshot.
    /// </summary>
    private static ISchemeWeaverService ResolveSchemeWeaverService(IServiceProvider serviceProvider, ref IServiceScope? scope)
    {
        try
        {
            return serviceProvider.GetRequiredService<ISchemeWeaverService>();
        }
        catch (InvalidOperationException)
        {
            scope = serviceProvider.CreateScope();
            return scope.ServiceProvider.GetRequiredService<ISchemeWeaverService>();
        }
    }

    // ---- wire shapes for the shared state (camelCase is fixed here, not by the client's options) ----

    private sealed record StateShape(
        [property: JsonPropertyName("umbracoContentType")] ContentTypeShape UmbracoContentType,
        [property: JsonPropertyName("targetSchemaType")]
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? TargetSchemaType,
        [property: JsonPropertyName("note")] string Note);

    private sealed record ContentTypeShape(
        [property: JsonPropertyName("alias")] string Alias,
        [property: JsonPropertyName("name")] string Name,
        [property: JsonPropertyName("description")]
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Description,
        [property: JsonPropertyName("properties")] IReadOnlyList<PropertyShape> Properties);

    internal sealed record PropertyShape(
        [property: JsonPropertyName("alias")] string Alias,
        [property: JsonPropertyName("name")] string Name,
        [property: JsonPropertyName("editor")] string Editor,
        [property: JsonPropertyName("description")]
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Description,
        [property: JsonPropertyName("isRepeatingList")]
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] bool? IsRepeatingList,
        [property: JsonPropertyName("blockTypes")]
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] IReadOnlyList<BlockTypeShape>? BlockTypes);

    internal sealed record BlockTypeShape(
        [property: JsonPropertyName("blockType")] string BlockType,
        [property: JsonPropertyName("blockName")] string BlockName,
        [property: JsonPropertyName("fields")] IReadOnlyList<FieldShape> Fields);

    internal sealed record FieldShape(
        [property: JsonPropertyName("alias")] string Alias,
        [property: JsonPropertyName("name")] string Name,
        [property: JsonPropertyName("editor")] string Editor);
}

/// <summary>One content property as the snapshot sees it.</summary>
internal sealed record SnapshotProperty(
    string Alias,
    string Name,
    string EditorAlias,
    string? Description,
    IReadOnlyList<BlockElementTypeInfo> BlockTypes)
{
    public bool IsBlock => SchemeWeaverConstants.PropertyEditors.BlockEditorAliases.Contains(EditorAlias);

    public bool IsMedia => SchemeWeaverConstants.PropertyEditors.MediaPickerAliases.Contains(EditorAlias);

    public bool IsContentPicker => SchemeWeaverConstants.PropertyEditors.ContentPickerAliases.Contains(EditorAlias);

    public ContentTypeSnapshot.PropertyShape ToState()
        => IsBlock
            ? new ContentTypeSnapshot.PropertyShape(Alias, Name, EditorAlias, Description, true,
                BlockTypes.Select(el => new ContentTypeSnapshot.BlockTypeShape(
                    el.Alias,
                    string.IsNullOrEmpty(el.Name) ? el.Alias : el.Name,
                    el.PropertyInfos.Select(ip => new ContentTypeSnapshot.FieldShape(
                        ip.Alias,
                        string.IsNullOrEmpty(ip.Name) ? ip.Alias : ip.Name,
                        ip.EditorAlias)).ToList())).ToList())
            : new ContentTypeSnapshot.PropertyShape(Alias, Name, EditorAlias, Description, null, null);
}

/// <summary>One field of a block element type, with the element type that declares it.</summary>
internal sealed record BlockInnerProperty(
    string Alias,
    string Name,
    string EditorAlias,
    string BlockAlias,
    string BlockName);
