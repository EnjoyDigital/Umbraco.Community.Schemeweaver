using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Unicode;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Umbraco.Cms.Core.Models;
using Umbraco.Cms.Core.Services;
using Umbraco.Community.SchemeWeaver.Models.Api;
using Umbraco.Community.SchemeWeaver.Services;
using Umbraco.Community.SchemeWeaver.Services.Transforms;
using Umbraco.Community.SchemeWeaver.Services.ValueSchemas;
using Umbraco.Community.SchemeWeaver.TypeSafe.Configuration;
using Umbraco.Extensions;
using Editors = Umbraco.Community.SchemeWeaver.SchemeWeaverConstants.PropertyEditors;

namespace Umbraco.Community.SchemeWeaver.TypeSafe.Services.Judgments;

/// <summary>
/// What the model is allowed to know about one Umbraco content type: its properties (with
/// editors), the four built-ins, and every Block List/Grid property described BY ITS
/// CONTENTS. The snapshot is the shared <c>state</c> of every request in a mapper call and
/// the only source of the aliases a mapping may reference, so nothing here is invented.
/// </summary>
/// <remarks>
/// <para>
/// A block property described only as <c>sections (Umbraco.BlockList)</c> is an opaque
/// name; the eval harness showed a body-sections container never binds to
/// <c>mainEntity</c>/<c>hasPart</c> until its element types and their fields are in the
/// state. Element types come from <see cref="ISchemeWeaverService.GetBlockElementTypesAsync"/>,
/// resolved lazily because <c>SchemeWeaverService</c> depends on <c>ISchemaAutoMapper</c>,
/// which this satellite decorates — eager injection is a DI cycle (the AI satellite's
/// <c>AISchemaMapper</c> documents the same trap).
/// </para>
/// <para>
/// v2 widens what the model sees along four axes, each behind <see cref="TypeSafeOptions"/>:
/// the value schema per property (the stored value's shape, which carries no content), an
/// opt-in sample value per scalar property, the recursive structure of blocks inside blocks
/// (v1 flattened a nested Block List field to its editor alias, which is how it came to emit a
/// dead <c>wrapInType "Thing"</c> inner mapping the core warns on and drops), and the
/// neighbourhood of types above and beside this one for the cross-node round. The v1 entry
/// point is kept and produces the v1 state byte for byte, so nothing measured moves without
/// a caller opting in.
/// </para>
/// <para>
/// The whole v2 state is then measured once against <see cref="TypeSafeOptions.MaxStateCharacters"/>
/// and, while over budget, thinned in a fixed order (block-field value schemas, nested block
/// levels beyond the first, property value schemas, sample values) until it fits; see
/// <see cref="FitToBudget"/>. The per-field cap bounds a field, the budget bounds the type.
/// </para>
/// </remarks>
internal sealed class ContentTypeSnapshot
{
    /// <summary>
    /// Per-property value-schema cap, the same figure the AI satellite uses
    /// (<c>AISchemaMapper.MaxValueSchemaChars</c>): a recursive Block List schema can run to
    /// kilobytes and the request budget is 32k tokens for the state. It bounds one field; the
    /// state as a whole is bounded by <see cref="TypeSafeOptions.MaxStateCharacters"/>.
    /// </summary>
    internal const int MaxValueSchemaChars = 600;

    /// <summary>Sample values are a hint about kind (an ISBN, a coupon code), not content to reason over.</summary>
    internal const int MaxSampleValueChars = 120;

    /// <summary>
    /// Nodes fetched when looking for one to sample. The first page of a type is often a
    /// draft or a template node; a few more give a published one a chance without paging.
    /// </summary>
    private const int SamplePageSize = 10;

    private static HashSet<string> BlockEditorAliases => SchemeWeaverConstants.PropertyEditors.BlockEditorAliases;

    /// <summary>
    /// The client's serializer, mirrored (the same encoder, no naming policy), so a state
    /// measured here is the state the wire carries, character for character. The client keeps
    /// its options private; the two must move together.
    /// </summary>
    private static readonly JsonSerializerOptions StateSerializerOptions = new()
    {
        Encoder = JavaScriptEncoder.Create(UnicodeRanges.All),
    };

    private ContentTypeSnapshot(
        string alias,
        string name,
        string? description,
        IReadOnlyList<SnapshotProperty> properties,
        int nestedBlockDepth,
        bool includeBlockValueSchemas)
    {
        Alias = alias;
        Name = name;
        Description = description;
        Properties = properties;
        NestedBlockDepth = nestedBlockDepth;
        IncludeBlockValueSchemas = includeBlockValueSchemas;
    }

    public string Alias { get; }

    public string Name { get; }

    public string? Description { get; }

    /// <summary>
    /// Composition properties (inherited ones included) followed by the four built-ins. Rebuilt
    /// without value schemas or sample values when <see cref="FitToBudget"/> has to thin the state.
    /// </summary>
    public IReadOnlyList<SnapshotProperty> Properties { get; private set; }

    /// <summary>
    /// The types above and beside this one (v2, cross-node sources). Empty until the snapshot
    /// build discovers it; only ever serialised into the cross-node round's state.
    /// </summary>
    public ContentTypeNeighbourhood Neighbourhood { get; set; } = ContentTypeNeighbourhood.Empty;

    /// <summary>
    /// How many levels of blocks-inside-blocks the state and the block summaries describe
    /// below a block property's own element types. 0 is the v1 state (a nested Block List field
    /// is just an editor alias); v2 uses <see cref="TypeSafeOptions.MaxBlockRouteDepth"/>, cut
    /// back to 1 when <see cref="FitToBudget"/> has to thin the state.
    /// </summary>
    internal int NestedBlockDepth { get; private set; }

    /// <summary>
    /// Whether block fields carry their value schema in the state (v2) or not (v1, or a v2
    /// state <see cref="FitToBudget"/> had to thin).
    /// </summary>
    internal bool IncludeBlockValueSchemas { get; private set; }

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

    /// <summary>
    /// The raw element types of a block property, with their <see cref="BlockElementTypeInfo.PropertyInfos"/>
    /// and, recursively, <see cref="BlockElementPropertyInfo.NestedBlockElementTypes"/>, exactly
    /// as the core discovered them. This is what a routes planner walks: unlike
    /// <see cref="BlockInnerProperties"/> it is NOT de-duplicated across element types, because a
    /// route is per block alias and two element types that share a field alias are still two
    /// routes. Empty for a non-block or unknown property.
    /// </summary>
    public IReadOnlyList<BlockElementTypeInfo> BlockElementTypes(string propertyAlias)
    {
        var property = Find(propertyAlias);
        return property is null || !property.IsBlock ? [] : property.BlockTypes;
    }

    /// <summary>
    /// One-line summary of a block property's contents for question text. With a nested block
    /// depth above 0 a field that is itself a Block List reads as "questions: a nested list of
    /// faqItem(question, answer)" so the shape question can see that a block carries a list.
    /// </summary>
    public string SummariseBlocks(string propertyAlias)
    {
        var property = Find(propertyAlias);
        if (property is null || property.BlockTypes.Count == 0)
            return string.Empty;

        var parts = property.BlockTypes.Select(el =>
            $"\"{(string.IsNullOrEmpty(el.Name) ? el.Alias : el.Name)}\" (fields: {string.Join(", ", el.PropertyInfos.Select(ip => DescribeField(ip, NestedBlockDepth)))})");
        return $" It is a repeating list; each entry is one of these block types: {string.Join("; ", parts)}.";
    }

    private static string DescribeField(BlockElementPropertyInfo field, int depth)
    {
        if (depth <= 0 || !IsNestedList(field))
            return field.Alias;

        var inner = field.NestedBlockElementTypes.Select(el =>
            $"{el.Alias}({string.Join(", ", el.PropertyInfos.Select(f => DescribeField(f, depth - 1)))})");
        return $"{field.Alias}: a nested list of {string.Join(" or ", inner)}";
    }

    /// <summary>
    /// The shared request state. <paramref name="targetSchemaType"/> is set for a property
    /// mapping and left out for schema-type suggestion, which is choosing it.
    /// </summary>
    public object ToState(string? targetSchemaType)
        => ToState(targetSchemaType, neighbourhood: null);

    /// <summary>
    /// The shared request state, optionally carrying the <c>neighbourhood</c> node. The
    /// neighbourhood is attached ONLY to the cross-node round's state so its tokens are not paid
    /// on every bind, shape, descent and inner request.
    /// </summary>
    public object ToState(string? targetSchemaType, ContentTypeNeighbourhood? neighbourhood)
        => new StateShape(
            new ContentTypeShape(
                Alias,
                Name,
                string.IsNullOrWhiteSpace(Description) ? null : Description,
                Properties.Select(p => p.ToState(NestedBlockDepth, IncludeBlockValueSchemas)).ToList()),
            targetSchemaType,
            "Built-in properties are prefixed with __: __name is the node name, __url its URL, "
            + "__createDate and __updateDate its timestamps.",
            neighbourhood is { IsEmpty: false } ? neighbourhood.ToState() : null);

    /// <summary>
    /// v2 entry point: as the five-argument overload plus the value schema per property, the
    /// opt-in sample values, the recursive nested block structure and the neighbourhood (see
    /// <see cref="TypeSafeOptions"/>). A <c>null</c> <paramref name="valueSchemaService"/> (a
    /// host below Umbraco 17.4, or a test) simply leaves the value schemas out.
    /// </summary>
    public static Task<ContentTypeSnapshot?> BuildAsync(
        IContentTypeService contentTypeService,
        IServiceProvider serviceProvider,
        IPropertyValueSchemaService? valueSchemaService,
        TypeSafeOptions options,
        string contentTypeAlias,
        ILogger logger,
        CancellationToken cancellationToken)
        => BuildCoreAsync(contentTypeService, serviceProvider, valueSchemaService, options, contentTypeAlias, logger, cancellationToken);

    /// <summary>
    /// Snapshots a content type, or returns <c>null</c> when the alias is unknown. Block
    /// introspection failures degrade to "no element types" with a Debug log — a thinner
    /// state, never an error. This is the v1 state: no value schemas, no sample values, no
    /// nested block structure and no neighbourhood.
    /// </summary>
    public static Task<ContentTypeSnapshot?> BuildAsync(
        IContentTypeService contentTypeService,
        IServiceProvider serviceProvider,
        string contentTypeAlias,
        ILogger logger,
        CancellationToken cancellationToken)
        => BuildCoreAsync(contentTypeService, serviceProvider, valueSchemaService: null, options: null, contentTypeAlias, logger, cancellationToken);

    /// <summary>
    /// The one builder behind both entry points. <paramref name="options"/> <c>null</c> is the
    /// v1 profile; with options every v2 axis is applied as configured.
    /// </summary>
    private static async Task<ContentTypeSnapshot?> BuildCoreAsync(
        IContentTypeService contentTypeService,
        IServiceProvider serviceProvider,
        IPropertyValueSchemaService? valueSchemaService,
        TypeSafeOptions? options,
        string contentTypeAlias,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        var contentType = contentTypeService.Get(contentTypeAlias);
        if (contentType is null)
            return null;

        var v2 = options is not null;
        var nestedBlockDepth = options is null ? 0 : Math.Max(0, options.MaxBlockRouteDepth);

        ISchemeWeaverService? schemeWeaverService = null;
        IServiceScope? scope = null;
        try
        {
            var samples = options is { IncludeSampleValues: true }
                ? ReadSampleValues(contentType, serviceProvider, ref scope, logger, cancellationToken)
                : null;

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
                        schemeWeaverService ??= ResolveRequired<ISchemeWeaverService>(serviceProvider, ref scope);
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

                string? valueSchema = null;
                if (v2 && valueSchemaService is not null)
                {
                    try
                    {
                        valueSchema = TruncateValueSchema(await valueSchemaService
                            .GetDataTypeValueSchemaAsync(property.DataTypeKey)
                            .ConfigureAwait(false));
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException)
                    {
                        // Boundary catch by policy: the value schema is a hint, and a data type
                        // whose editor cannot describe its value is described by editor alone.
                        logger.LogDebug(ex,
                            "Value schema for {ContentType}.{Property} unavailable; describing the property by editor only",
                            contentType.Alias, property.Alias);
                    }
                }

                string? sampleValue = null;
                if (samples is not null && samples.TryGetValue(property.Alias, out var sample))
                    sampleValue = sample;

                properties.Add(new SnapshotProperty(
                    property.Alias,
                    string.IsNullOrEmpty(property.Name) ? property.Alias : property.Name,
                    property.PropertyEditorAlias,
                    string.IsNullOrWhiteSpace(property.Description) ? null : property.Description,
                    blockTypes,
                    valueSchema,
                    sampleValue));
            }

            foreach (var (alias, displayName, editorAlias) in SchemeWeaverConstants.BuiltInProperties.All)
                properties.Add(new SnapshotProperty(alias, displayName, editorAlias, null, []));

            var snapshot = new ContentTypeSnapshot(
                contentType.Alias,
                string.IsNullOrEmpty(contentType.Name) ? contentType.Alias : contentType.Name,
                contentType.Description,
                properties,
                nestedBlockDepth,
                includeBlockValueSchemas: v2);

            if (options is not null)
                snapshot.FitToBudget(options.MaxStateCharacters, logger);

            if (options is { EnableCrossNodeSources: true })
            {
                try
                {
                    snapshot.Neighbourhood = await ContentTypeNeighbourhood
                        .DiscoverAsync(contentType, contentTypeService, serviceProvider, options, logger, cancellationToken)
                        .ConfigureAwait(false);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    // Boundary catch by policy: discovery already degrades internally; this
                    // guards the contract, so the cross-node round simply has nothing to offer.
                    logger.LogDebug(ex, "Neighbourhood of {ContentType} unavailable; no cross-node sources", contentType.Alias);
                    snapshot.Neighbourhood = ContentTypeNeighbourhood.Empty;
                }
            }

            return snapshot;
        }
        finally
        {
            scope?.Dispose();
        }
    }

    /// <summary>
    /// One published node's scalar values, HTML-stripped, whitespace-collapsed and cut to
    /// <see cref="MaxSampleValueChars"/>, keyed by property alias. Only a published node is ever
    /// read: a draft may be embargoed content, and <see cref="TypeSafeOptions.IncludeSampleValues"/>
    /// promises one published node, so a type with no published node in the first page has no
    /// sample values at all. Block editors, content and media pickers and labels are skipped: a
    /// picker holds a reference and a block a structure, which the value schema already
    /// describes better. Never logged: these are customer content. Any failure is "no sample
    /// values" with a Debug log.
    /// </summary>
    private static Dictionary<string, string>? ReadSampleValues(
        IContentType contentType,
        IServiceProvider serviceProvider,
        ref IServiceScope? scope,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        try
        {
            var contentService = ResolveOptional<IContentService>(serviceProvider, ref scope);
            if (contentService is null)
                return null;

            // The first page in tree order is often a draft or an editor's template node; a
            // few more give a published one a chance without paging through the type. A draft
            // is never a fallback: its values have not been released and must not leave the site.
            var page = contentService.GetPagedOfTypes([contentType.Id], 0, SamplePageSize, out _, null).ToList();
            var node = page.FirstOrDefault(c => c.Published);
            if (node is null)
                return null;

            var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var property in contentType.CompositionPropertyTypes)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var editor = property.PropertyEditorAlias ?? string.Empty;
                if (Editors.BlockEditorAliases.Contains(editor)
                    || Editors.MediaPickerAliases.Contains(editor)
                    || Editors.ContentPickerAliases.Contains(editor)
                    || string.Equals(editor, "Umbraco.Label", StringComparison.OrdinalIgnoreCase))
                    continue;

                // A culture-variant property has no invariant value: read the first published
                // culture instead, and skip the property when the node has none.
                string? culture = null;
                if (property.VariesByCulture())
                {
                    culture = node.PublishedCultures.FirstOrDefault();
                    if (culture is null)
                        continue;
                }

                var raw = node.GetValue(property.Alias, culture, published: true)?.ToString();
                if (string.IsNullOrWhiteSpace(raw))
                    continue;

                var text = SchemaValueTransformer.StripHtmlTags(raw);
                if (text.Length == 0)
                    continue;

                result[property.Alias] = text.Length <= MaxSampleValueChars ? text : text[..MaxSampleValueChars] + "…";
            }

            return result.Count > 0 ? result : null;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Boundary catch by policy: sample values are an opt-in hint, never a reason to fail.
            logger.LogDebug(ex, "Sample values for {ContentType} unavailable; describing properties by structure only", contentType.Alias);
            return null;
        }
    }

    /// <summary>
    /// Cuts a value schema to <see cref="MaxValueSchemaChars"/> with the same marker the AI
    /// satellite appends, so both satellites present the same shape to their models. Empty in,
    /// <c>null</c> out, so an absent schema is omitted from the state rather than serialised empty.
    /// </summary>
    internal static string? TruncateValueSchema(string? valueSchema)
    {
        if (string.IsNullOrWhiteSpace(valueSchema))
            return null;

        return valueSchema.Length <= MaxValueSchemaChars
            ? valueSchema
            : valueSchema[..MaxValueSchemaChars] + " …(schema truncated)";
    }

    /// <summary>Characters of a state as the client sends it. Internal for the budget's tests.</summary>
    internal static int CountStateCharacters(object state)
        => JsonSerializer.Serialize(state, StateSerializerOptions).Length;

    /// <summary>
    /// Keeps the shared state inside <paramref name="maxStateCharacters"/>
    /// (<see cref="TypeSafeOptions.MaxStateCharacters"/>). <see cref="MaxValueSchemaChars"/>
    /// bounds one field, not the type: a wide type with several block editors expanded three
    /// levels deep, every field carrying a schema, can pass the 32k-token state cap and fail
    /// every round with a 4xx the client never retries, after which the decorator falls back
    /// to the heuristic for the whole mapping. So the state is measured once, as the rounds
    /// send it, and while it is over budget the least valuable detail goes first, one step at
    /// a time with a fresh measurement after each: block-field value schemas, then nested
    /// block levels beyond the first, then the properties' own value schemas, then the sample
    /// values. Each step logs sizes only, never content.
    /// </summary>
    /// <remarks>
    /// Two nodes of the real state are not measured. The target schema type is not known here;
    /// it is one type name, tens of characters against a budget of tens of thousands. The
    /// neighbourhood is attached only to the cross-node round's request (see
    /// <see cref="ToState(string?, ContentTypeNeighbourhood?)"/>); a neighbourhood that pushes
    /// that one request over the cap is left to the mapper's boundary catch around the
    /// cross-node round, which keeps the local mapping and logs a Warning.
    /// </remarks>
    private void FitToBudget(int maxStateCharacters, ILogger logger)
    {
        var size = CountStateCharacters(ToState(targetSchemaType: null));
        if (size <= maxStateCharacters)
            return;

        foreach (var (detail, drop) in Reductions())
        {
            if (!drop())
                continue;

            var before = size;
            size = CountStateCharacters(ToState(targetSchemaType: null));
            logger.LogDebug(
                "TypeSafe state for {ContentType} is over budget ({Before} of {Budget} characters); dropped {Detail}, now {After} characters",
                Alias, before, maxStateCharacters, detail, size);

            if (size <= maxStateCharacters)
                return;
        }

        logger.LogDebug(
            "TypeSafe state for {ContentType} is still {Characters} characters against a budget of {Budget} with every reduction applied; the request may be refused and the mapping fall back to the prior mapper",
            Alias, size, maxStateCharacters);
    }

    /// <summary>The reductions in the order they are applied; each reports whether it removed anything.</summary>
    private IEnumerable<(string Detail, Func<bool> Drop)> Reductions()
    {
        yield return ("block-field value schemas", DropBlockFieldValueSchemas);
        yield return ("nested block levels beyond the first", DropNestedBlockLevelsBeyondTheFirst);
        yield return ("property value schemas", DropPropertyValueSchemas);
        yield return ("sample values", DropSampleValues);
    }

    private bool DropBlockFieldValueSchemas()
    {
        if (!IncludeBlockValueSchemas || !Properties.Any(p => p.IsBlock && HasFieldValueSchema(p.BlockTypes, NestedBlockDepth)))
            return false;

        IncludeBlockValueSchemas = false;
        return true;
    }

    private bool DropNestedBlockLevelsBeyondTheFirst()
    {
        if (NestedBlockDepth <= 1 || !Properties.Any(p => p.IsBlock && HasNestedListAtLevel(p.BlockTypes, level: 2)))
            return false;

        NestedBlockDepth = 1;
        return true;
    }

    private bool DropPropertyValueSchemas()
    {
        if (Properties.All(p => p.ValueSchema is null))
            return false;

        Properties = Properties.Select(p => p with { ValueSchema = null }).ToList();
        return true;
    }

    private bool DropSampleValues()
    {
        if (Properties.All(p => p.SampleValue is null))
            return false;

        Properties = Properties.Select(p => p with { SampleValue = null }).ToList();
        return true;
    }

    /// <summary>Whether any field of these element types, or of the nested ones the state shows to <paramref name="depth"/>, carries a value schema.</summary>
    private static bool HasFieldValueSchema(IReadOnlyList<BlockElementTypeInfo> elementTypes, int depth)
        => elementTypes.Any(el => el.PropertyInfos.Any(ip =>
            !string.IsNullOrWhiteSpace(ip.ValueSchema)
            || (depth > 0 && IsNestedList(ip) && HasFieldValueSchema(ip.NestedBlockElementTypes, depth - 1))));

    /// <summary>Whether a nested Block List field with element types sits <paramref name="level"/> levels down (1 is a field of these element types themselves).</summary>
    private static bool HasNestedListAtLevel(IReadOnlyList<BlockElementTypeInfo> elementTypes, int level)
        => elementTypes.Any(el => el.PropertyInfos.Any(ip =>
            IsNestedList(ip) && (level <= 1 || HasNestedListAtLevel(ip.NestedBlockElementTypes, level - 1))));

    /// <summary>A block field that is itself a Block List/Grid whose element types the core discovered.</summary>
    private static bool IsNestedList(BlockElementPropertyInfo field)
        => BlockEditorAliases.Contains(field.EditorAlias) && field.NestedBlockElementTypes.Count > 0;

    /// <summary>
    /// Resolves a core service at call time. <see cref="ISchemeWeaverService"/> is scoped in
    /// the core; when this mapper is itself scoped the injected provider hands back the scope's
    /// instance (the AI satellite's pattern). When the mapper is a singleton the injected
    /// provider is the root and scope validation refuses, so a child scope is opened instead
    /// and disposed with the snapshot.
    /// </summary>
    private static T ResolveRequired<T>(IServiceProvider serviceProvider, ref IServiceScope? scope)
        where T : notnull
    {
        try
        {
            return serviceProvider.GetRequiredService<T>();
        }
        catch (InvalidOperationException)
        {
            scope ??= serviceProvider.CreateScope();
            return scope.ServiceProvider.GetRequiredService<T>();
        }
    }

    /// <summary>As <see cref="ResolveRequired{T}"/>, but <c>null</c> when the host has not registered the service.</summary>
    private static T? ResolveOptional<T>(IServiceProvider serviceProvider, ref IServiceScope? scope)
        where T : class
    {
        try
        {
            return serviceProvider.GetService<T>();
        }
        catch (InvalidOperationException)
        {
            scope ??= serviceProvider.CreateScope();
            return scope.ServiceProvider.GetService<T>();
        }
    }

    // ---- wire shapes for the shared state (camelCase is fixed here, not by the client's options) ----

    private sealed record StateShape(
        [property: JsonPropertyName("umbracoContentType")] ContentTypeShape UmbracoContentType,
        [property: JsonPropertyName("targetSchemaType")]
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? TargetSchemaType,
        [property: JsonPropertyName("note")] string Note,
        [property: JsonPropertyName("neighbourhood")]
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] object? Neighbourhood = null);

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
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] IReadOnlyList<BlockTypeShape>? BlockTypes,
        [property: JsonPropertyName("valueSchema")]
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? ValueSchema = null,
        [property: JsonPropertyName("sampleValue")]
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? SampleValue = null);

    internal sealed record BlockTypeShape(
        [property: JsonPropertyName("blockType")] string BlockType,
        [property: JsonPropertyName("blockName")] string BlockName,
        [property: JsonPropertyName("fields")] IReadOnlyList<FieldShape> Fields);

    internal sealed record FieldShape(
        [property: JsonPropertyName("alias")] string Alias,
        [property: JsonPropertyName("name")] string Name,
        [property: JsonPropertyName("editor")] string Editor,
        [property: JsonPropertyName("valueSchema")]
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? ValueSchema = null,
        [property: JsonPropertyName("blockTypes")]
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] IReadOnlyList<BlockTypeShape>? BlockTypes = null);

    /// <summary>
    /// The element types of a block property (or of a nested Block List field) as state, with
    /// nested Block List fields expanded to <paramref name="nestedBlockDepth"/> further levels.
    /// The core discovers up to three levels; whatever it did not discover reads as a plain field.
    /// </summary>
    internal static IReadOnlyList<BlockTypeShape> BlockTypeShapes(
        IReadOnlyList<BlockElementTypeInfo> elementTypes,
        int nestedBlockDepth,
        bool includeValueSchemas)
        => elementTypes.Select(el => new BlockTypeShape(
                el.Alias,
                string.IsNullOrEmpty(el.Name) ? el.Alias : el.Name,
                el.PropertyInfos.Select(ip => new FieldShape(
                    ip.Alias,
                    string.IsNullOrEmpty(ip.Name) ? ip.Alias : ip.Name,
                    ip.EditorAlias,
                    includeValueSchemas ? TruncateValueSchema(ip.ValueSchema) : null,
                    nestedBlockDepth > 0 && IsNestedList(ip)
                        ? BlockTypeShapes(ip.NestedBlockElementTypes, nestedBlockDepth - 1, includeValueSchemas)
                        : null)).ToList()))
            .ToList();
}

/// <summary>One content property as the snapshot sees it.</summary>
internal sealed record SnapshotProperty(
    string Alias,
    string Name,
    string EditorAlias,
    string? Description,
    IReadOnlyList<BlockElementTypeInfo> BlockTypes,
    string? ValueSchema = null,
    string? SampleValue = null)
{
    public bool IsBlock => SchemeWeaverConstants.PropertyEditors.BlockEditorAliases.Contains(EditorAlias);

    public bool IsMedia => SchemeWeaverConstants.PropertyEditors.MediaPickerAliases.Contains(EditorAlias);

    public bool IsContentPicker => SchemeWeaverConstants.PropertyEditors.ContentPickerAliases.Contains(EditorAlias);

    /// <summary>The v1 state shape: block fields flat, no block value schemas.</summary>
    public ContentTypeSnapshot.PropertyShape ToState()
        => ToState(nestedBlockDepth: 0, includeBlockValueSchemas: false);

    /// <summary>
    /// The state shape at the snapshot's detail level: nested Block List fields expanded
    /// <paramref name="nestedBlockDepth"/> levels down and block fields carrying their value
    /// schema when asked. The property's own value schema and sample value travel whenever set.
    /// </summary>
    public ContentTypeSnapshot.PropertyShape ToState(int nestedBlockDepth, bool includeBlockValueSchemas)
        => IsBlock
            ? new ContentTypeSnapshot.PropertyShape(Alias, Name, EditorAlias, Description, true,
                ContentTypeSnapshot.BlockTypeShapes(BlockTypes, nestedBlockDepth, includeBlockValueSchemas),
                ValueSchema, SampleValue)
            : new ContentTypeSnapshot.PropertyShape(Alias, Name, EditorAlias, Description, null, null, ValueSchema, SampleValue);
}

/// <summary>One field of a block element type, with the element type that declares it.</summary>
internal sealed record BlockInnerProperty(
    string Alias,
    string Name,
    string EditorAlias,
    string BlockAlias,
    string BlockName);
