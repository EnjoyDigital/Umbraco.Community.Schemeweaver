using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Schema.NET;
using Umbraco.Cms.Core.Models.PublishedContent;
using Umbraco.Cms.Core.Routing;
using Umbraco.Cms.Core.Services.Navigation;
using Umbraco.Community.SchemeWeaver.Graph;
using Umbraco.Community.SchemeWeaver.Models.Api;
using Umbraco.Community.SchemeWeaver.Models.Entities;
using Umbraco.Community.SchemeWeaver.Persistence;
using Umbraco.Cms.Core.PublishedCache;
using Umbraco.Community.SchemeWeaver.Services.Resolvers;
using Umbraco.Community.SchemeWeaver.Services.Transforms;
using Umbraco.Extensions;

namespace Umbraco.Community.SchemeWeaver.Services;

/// <summary>
/// Generates JSON-LD from published content using stored schema mappings.
/// Uses the extensible <see cref="IPropertyValueResolver"/> pattern for property value extraction.
/// </summary>
public partial class JsonLdGenerator : IJsonLdGenerator, IComplexTypeBuilder
{
    private readonly ISchemaMappingRepository _repository;
    private readonly ISchemaTypeRegistry _registry;
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly IDocumentNavigationQueryService _navigationQueryService;
    private readonly IPublishedContentStatusFilteringService _publishedStatusFilteringService;
    private readonly IPropertyValueResolverFactory _resolverFactory;
    private readonly IPublishedUrlProvider _urlProvider;
    private readonly IVariationContextAccessor _variationContextAccessor;
    private readonly ILogger<JsonLdGenerator> _logger;
    private readonly SchemeWeaverOptions _options;

    /// <summary>
    /// Shared empty visited-node chain, for the top-level entry into complex-type resolution.
    /// </summary>
    private static readonly IReadOnlySet<Guid> NoVisitedContentKeys = new HashSet<Guid>();

    /// <summary>
    /// Fallback serialiser options for Schema.NET types that have property name collisions
    /// (e.g. Drug, MedicalCondition, Physician — ~83 types in Schema.NET 13.0.0).
    /// Only used when <see cref="Thing.ToString()"/> throws.
    /// </summary>
    private static readonly JsonSerializerOptions _deduplicatingOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingDefault,
        TypeInfoResolver = new DeduplicatingTypeInfoResolver()
    };

    /// <summary>
    /// Writer options for <see cref="ReEncodeWithHtmlSafeEncoder"/>. Deliberately identical to
    /// <c>GraphGenerator</c>'s: Create(UnicodeRanges.All) writes non-ASCII literally but — unlike
    /// UnsafeRelaxedJsonEscaping — still escapes &lt;, &gt;, &amp; and ', which is what keeps a
    /// mapped value from breaking out of the &lt;script type="application/ld+json"&gt; block.
    /// </summary>
    private static readonly JsonWriterOptions _htmlSafeWriterOptions = new()
    {
        Indented = false,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.Create(System.Text.Unicode.UnicodeRanges.All),
    };

    public JsonLdGenerator(
        ISchemaMappingRepository repository,
        ISchemaTypeRegistry registry,
        IHttpContextAccessor httpContextAccessor,
        IDocumentNavigationQueryService navigationQueryService,
        IPublishedContentStatusFilteringService publishedStatusFilteringService,
        IPropertyValueResolverFactory resolverFactory,
        IPublishedUrlProvider urlProvider,
        IVariationContextAccessor variationContextAccessor,
        ILogger<JsonLdGenerator> logger,
        IOptions<SchemeWeaverOptions> options)
    {
        _repository = repository;
        _registry = registry;
        _httpContextAccessor = httpContextAccessor;
        _navigationQueryService = navigationQueryService;
        _publishedStatusFilteringService = publishedStatusFilteringService;
        _resolverFactory = resolverFactory;
        _urlProvider = urlProvider;
        _variationContextAccessor = variationContextAccessor;
        _logger = logger;
        _options = options.Value;
    }

    public Thing? GenerateJsonLd(
        IPublishedContent content,
        string? culture = null,
        GraphPieceContext? graphContext = null)
    {
        var previousContext = _variationContextAccessor.VariationContext;
        if (culture is not null)
            _variationContextAccessor.VariationContext = new VariationContext(culture);

        try
        {
            return GenerateJsonLdCore(content, culture, graphContext);
        }
        finally
        {
            _variationContextAccessor.VariationContext = previousContext;
        }
    }

    private Thing? GenerateJsonLdCore(IPublishedContent content, string? culture, GraphPieceContext? graphContext = null)
    {
        var mapping = _repository.GetByContentTypeAlias(content.ContentType.Alias);
        if (mapping is not { IsEnabled: true })
            return null;

        var clrType = _registry.GetClrType(mapping.SchemaTypeName);
        if (clrType is null)
        {
            _logger.LogWarning("Schema type {TypeName} not found in registry", mapping.SchemaTypeName);
            return null;
        }

        if (Activator.CreateInstance(clrType) is not Thing instance)
            return null;

        // Materialise once so the InLanguage precompute and the mapping loop share one snapshot.
        var propertyMappings = _repository.GetPropertyMappings(mapping.Id).ToList();

        // An explicit InLanguage mapping (of any source type) suppresses the post-loop auto-fill.
        // The original signalled this from inside the loop before resolving the mapping, so its
        // presence — not its resolved value — is what matters; precompute it up front.
        var hasExplicitInLanguage = propertyMappings.Any(pm =>
            string.Equals(pm.SchemaPropertyName, "InLanguage", StringComparison.OrdinalIgnoreCase));
        var hasExplicitId = false;

        foreach (var propMapping in propertyMappings)
        {
            // Per-property try/catch is the degrade boundary: one bad property logs a
            // warning and the rest of the Thing is still built. Keep it INSIDE the loop.
            try
            {
                if (ApplyPropertyMapping(instance, propMapping, content, culture, graphContext)
                    == PropertyMappingOutcome.SetExplicitId)
                    hasExplicitId = true;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to map property {Property} for content {ContentId}",
                    propMapping.SchemaPropertyName, content.Id);
            }
        }

        // Auto-fill inLanguage when culture is set and not explicitly mapped
        if (culture is not null && !hasExplicitInLanguage)
            SchemaPropertySetter.SetPropertyValue(instance, "InLanguage", culture);

        // @id precedence:
        //   1. Explicit "Id" property mapping (handled in the loop above, sets hasExplicitId).
        //   2. Mapping-level IdOverride template with token expansion.
        //   3. Default {absoluteUrl}#{schemaTypeLowercase} — disambiguates the page
        //      (WebPage) from any other entity it describes (Organization, Article, …)
        //      that would otherwise collide on the bare URL.
        if (!hasExplicitId)
        {
            var contentUrl = ResolveAbsoluteUrl(content);
            var expandedId = ResolveIdFromOverrideOrDefault(mapping, content, contentUrl, culture);
            if (!string.IsNullOrEmpty(expandedId)
                && Uri.TryCreate(expandedId, UriKind.Absolute, out var idUri))
            {
                instance.Id = idUri;
            }
        }

        return instance;
    }

    /// <summary>
    /// Outcome of applying a single <see cref="PropertyMapping"/> to the top-level Thing. Lets the
    /// caller learn whether an explicit <c>@id</c> was set WITHOUT a <c>ref</c> flag, while keeping the
    /// per-property try/catch (the degrade boundary) in the parent loop.
    /// </summary>
    private enum PropertyMappingOutcome
    {
        /// <summary>Nothing was applied (null/empty resolution, unresolvable reference, or an Id that would not coerce).</summary>
        Skipped,

        /// <summary>A normal schema property (or reference shell) was set on the instance.</summary>
        Set,

        /// <summary>An explicit <c>Id</c> mapping coerced to a Uri and was set — suppresses the default <c>@id</c> convention.</summary>
        SetExplicitId
    }

    /// <summary>
    /// Applies a single property mapping to <paramref name="instance"/>. Mirrors the original loop
    /// body exactly (reference short-circuit before ResolveValue; transform only on strings; the
    /// explicit-Id branch); the caller wraps this in the per-property try/catch.
    /// </summary>
    private PropertyMappingOutcome ApplyPropertyMapping(
        Thing instance,
        PropertyMapping propMapping,
        IPublishedContent content,
        string? culture,
        GraphPieceContext? graphContext)
    {
        // `reference` source type: cross-piece @id ref. Only resolvable when called inside the
        // graph pipeline (graphContext != null). Outside that pipeline (legacy single-Thing
        // callers) we can't know what to point at, so the mapping is skipped. This must
        // short-circuit BEFORE ResolveValue — references are resolved from the graph context.
        if (string.Equals(propMapping.SourceType, SchemeWeaverConstants.SourceTypes.Reference, StringComparison.OrdinalIgnoreCase))
            return TryApplyReference(instance, propMapping, graphContext);

        var value = ResolveValue(propMapping, content, culture);
        if (value is null)
            return PropertyMappingOutcome.Skipped;

        // Apply transforms only to string values; skip empty/whitespace
        if (value is string stringValue)
        {
            if (string.IsNullOrWhiteSpace(stringValue))
                return PropertyMappingOutcome.Skipped;
            value = ApplyTransform(stringValue, propMapping.TransformType);
        }

        // Guard against null after transform (ApplyTransform can return null)
        if (value is null or string { Length: 0 })
            return PropertyMappingOutcome.Skipped;

        // @id is Uri-typed on Schema.NET Thing; SchemaPropertySetter can't convert a
        // string to Uri generically, so we handle it here. Setting via mapping
        // suppresses the default {url}#{type} convention.
        if (string.Equals(propMapping.SchemaPropertyName, "Id", StringComparison.OrdinalIgnoreCase))
            return TryApplyExplicitId(instance, value);

        SchemaPropertySetter.SetPropertyValue(instance, propMapping.SchemaPropertyName, value);
        return PropertyMappingOutcome.Set;
    }

    /// <summary>
    /// Handles a <c>reference</c> source-type mapping: resolves the target piece's absolute @id from
    /// the graph context and binds a range-typed @id-only shell. Returns <see cref="PropertyMappingOutcome.Skipped"/>
    /// when there is no graph context, no target key, or an unresolvable/relative id.
    /// </summary>
    private static PropertyMappingOutcome TryApplyReference(
        Thing instance, PropertyMapping propMapping, GraphPieceContext? graphContext)
    {
        if (graphContext is null || string.IsNullOrWhiteSpace(propMapping.TargetPieceKey))
            return PropertyMappingOutcome.Skipped;

        var refId = graphContext.IdFor(propMapping.TargetPieceKey);
        if (string.IsNullOrWhiteSpace(refId)
            || !Uri.TryCreate(refId, UriKind.Absolute, out var refUri))
            return PropertyMappingOutcome.Skipped;

        // @id-only shell typed to the target property's range so it binds even to narrowly-typed
        // properties (e.g. publisher needs an Organization, not a bare Thing). GraphGenerator's
        // ref-collapse then reduces the serialised form to {"@id": …}.
        SchemaPropertySetter.SetPropertyValue(
            instance,
            propMapping.SchemaPropertyName,
            SchemaPropertySetter.CreateReferenceShell(
                instance, propMapping.SchemaPropertyName, refUri));
        return PropertyMappingOutcome.Set;
    }

    /// <summary>
    /// Handles an explicit <c>Id</c> mapping: coerces the resolved value to a Uri (relative-or-absolute,
    /// preserving fragment ids) and sets <see cref="Thing.Id"/>. Returns <see cref="PropertyMappingOutcome.SetExplicitId"/>
    /// only when the value coerces — otherwise the default @id convention still applies.
    /// </summary>
    private static PropertyMappingOutcome TryApplyExplicitId(Thing instance, object value)
    {
        if (TryCoerceToUri(value, out var idUri))
        {
            instance.Id = idUri;
            return PropertyMappingOutcome.SetExplicitId;
        }

        return PropertyMappingOutcome.Skipped;
    }

    public string? GenerateJsonLdString(IPublishedContent content, string? culture = null)
    {
        var thing = GenerateJsonLd(content, culture);
        return SafeSerialize(thing, content.Id);
    }

    public string? GenerateBreadcrumbJsonLd(IPublishedContent content, string? culture = null)
    {
        // Walk the parent chain to build the ancestor list (root-first order)
        var ancestors = new List<IPublishedContent> { content };
        try
        {
            var current = content.Parent<IPublishedContent>(_navigationQueryService, _publishedStatusFilteringService);
            while (current is not null)
            {
                ancestors.Add(current);
                current = current.Parent<IPublishedContent>(_navigationQueryService, _publishedStatusFilteringService);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to walk parent chain for breadcrumb generation on content {ContentId}", content.Id);
            return null;
        }
        ancestors.Reverse();

        return BuildBreadcrumbJsonLd(ancestors);
    }

    /// <summary>
    /// Builds a BreadcrumbList JSON-LD string from a root-first ordered list of content nodes.
    /// Returns null if the list has fewer than 2 items (no meaningful breadcrumb trail).
    /// </summary>
    internal string? BuildBreadcrumbJsonLd(List<IPublishedContent> ancestors)
    {
        if (ancestors.Count <= 1)
            return null; // No breadcrumbs for root nodes

        var breadcrumb = new BreadcrumbList();
        var items = new List<IListItem>();

        for (var i = 0; i < ancestors.Count; i++)
        {
            var ancestor = ancestors[i];
            var url = ResolveAbsoluteUrl(ancestor);

            var listItem = new ListItem
            {
                Position = i + 1,
                Name = ancestor.Name
            };

            if (!string.IsNullOrEmpty(url)
                && Uri.TryCreate(url, UriKind.Absolute, out var uri))
            {
                // Google's BreadcrumbList spec requires `item` on each ListItem
                // (not `url`/`@id`). A minimal WebPage with @id is accepted.
                listItem.Item = new WebPage { Id = uri };
            }

            items.Add(listItem);
        }

        breadcrumb.ItemListElement = items;
        return SafeSerialize(breadcrumb);
    }

    /// <summary>
    /// Resolves an absolute URL for content using the URL provider with a request-context fallback.
    /// </summary>
    /// <summary>
    /// Coerces a resolved mapping value to a Uri suitable for Schema.NET Thing.Id.
    /// Accepts Uri directly or any string parseable as relative-or-absolute URI.
    /// Relative Uris (e.g. "#organization") are preserved as-is so users can express
    /// fragment-only identifiers.
    /// </summary>
    private static bool TryCoerceToUri(object value, out Uri uri)
    {
        switch (value)
        {
            case Uri u:
                uri = u;
                return true;
            case string s when !string.IsNullOrWhiteSpace(s)
                               && Uri.TryCreate(s, UriKind.RelativeOrAbsolute, out var parsed):
                uri = parsed;
                return true;
            default:
                uri = null!;
                return false;
        }
    }

    private string? ResolveAbsoluteUrl(IPublishedContent content)
    {
        var url = _urlProvider.GetUrl(content, UrlMode.Absolute);
        if (!string.IsNullOrEmpty(url) && url != "#")
            return url;

        // Fallback: build absolute URL from relative + request context
        var relativeUrl = _urlProvider.GetUrl(content, UrlMode.Relative);
        if (string.IsNullOrEmpty(relativeUrl) || relativeUrl == "#")
            return null;

        var request = _httpContextAccessor.HttpContext?.Request;
        if (request is null)
            return null;

        return $"{request.Scheme}://{request.Host}{relativeUrl}";
    }

    private string? ResolveSiteUrl()
    {
        var request = _httpContextAccessor.HttpContext?.Request;
        if (request is not null)
            return $"{request.Scheme}://{request.Host}";
        return null;
    }

    /// <inheritdoc />
    public string? GetResolvedBaseUrl() => ResolveSiteUrl();

    /// <inheritdoc />
    public IPublishedElement? FindBlockInstance(IPublishedContent page, Guid blockInstanceKey, string? culture = null)
    {
        var previousContext = _variationContextAccessor.VariationContext;
        if (culture is not null)
            _variationContextAccessor.VariationContext = new VariationContext(culture);
        try
        {
            return FindBlockInstanceCore(page, blockInstanceKey, culture);
        }
        finally
        {
            _variationContextAccessor.VariationContext = previousContext;
        }
    }

    private IPublishedElement? FindBlockInstanceCore(IPublishedContent page, Guid blockInstanceKey, string? culture)
    {
        foreach (var property in page.Properties
            .Where(p => p.PropertyType?.EditorAlias is "Umbraco.BlockList" or "Umbraco.BlockGrid"))
        {
            var value = property.GetValue(culture: culture);
            if (value is null)
                continue;

            var match = EnumerateNestedBlockElements(value, culture, 0, new HashSet<Guid>())
                .FirstOrDefault(e => e.Key == blockInstanceKey);
            if (match is not null)
                return match;
        }

        return null;
    }

    /// <inheritdoc />
    public BlockInstancePreviewResult GenerateBlockInstanceJsonLd(IPublishedContent page, Guid blockInstanceKey, string? culture = null)
    {
        var previousContext = _variationContextAccessor.VariationContext;
        if (culture is not null)
            _variationContextAccessor.VariationContext = new VariationContext(culture);
        try
        {
            var element = FindBlockInstanceCore(page, blockInstanceKey, culture);
            if (element is null)
                return new BlockInstancePreviewResult(BlockInstanceResolutionStatus.BlockNotFound, null, page.Name, page.Key, null, null);

            var blockAlias = element.ContentType.Alias;
            var pageMapping = _repository.GetByContentTypeAlias(page.ContentType.Alias);
            var route = pageMapping is null
                ? null
                : FindRouteForBlock(_repository.GetPropertyMappings(pageMapping.Id).ToList(), blockAlias, depth: 0);

            if (route is null)
                return new BlockInstancePreviewResult(BlockInstanceResolutionStatus.NoRouteForBlock, null, page.Name, page.Key, blockAlias, null);

            // The block-list resolver is the sole claimant of the Umbraco.BlockList alias.
            if (_resolverFactory.GetResolver("Umbraco.BlockList") is not BlockContentResolver blockResolver)
                return new BlockInstancePreviewResult(BlockInstanceResolutionStatus.NoRouteForBlock, null, page.Name, page.Key, blockAlias, route.NestedSchemaType);

            var context = new PropertyResolverContext
            {
                Content = page,
                Mapping = new PropertyMapping { SourceType = "blockContent", SchemaPropertyName = route.NestedSchemaType ?? string.Empty },
                PropertyAlias = blockAlias,
                SchemaTypeRegistry = _registry,
                MappingRepository = _repository,
                HttpContextAccessor = _httpContextAccessor,
                ResolverFactory = _resolverFactory,
            ComplexTypeBuilder = this,
                RecursionDepth = 0,
                MaxRecursionDepth = _options.MaxRecursionDepth,
                Culture = culture,
                VisitedContentKeys = new HashSet<Guid> { element.Key }
            };

            var thing = blockResolver.MapElementViaRoute(element, route, context);
            if (thing is null)
                return new BlockInstancePreviewResult(BlockInstanceResolutionStatus.EmptyAfterRender, null, page.Name, page.Key, blockAlias, route.NestedSchemaType);

            var json = SafeSerialize(thing);
            if (string.IsNullOrEmpty(json))
                return new BlockInstancePreviewResult(BlockInstanceResolutionStatus.EmptyAfterRender, null, page.Name, page.Key, blockAlias, route.NestedSchemaType);

            return new BlockInstancePreviewResult(BlockInstanceResolutionStatus.Rendered, json, page.Name, page.Key, blockAlias, route.NestedSchemaType);
        }
        finally
        {
            _variationContextAccessor.VariationContext = previousContext;
        }
    }

    /// <summary>
    /// Finds the <see cref="BlockRoute"/> on the page mapping that maps a block of type
    /// <paramref name="blockAlias"/> to a schema type. Searches every <c>blockContent</c> property
    /// mapping's routes, recursing into nested routes (a block nested inside another block) up to the
    /// configured depth. Exact alias match wins; otherwise an empty-alias wildcard; legacy
    /// <c>NestedMappings</c> config is treated as an implicit wildcard route.
    /// </summary>
    private BlockRoute? FindRouteForBlock(IReadOnlyList<PropertyMapping> pagePropertyMappings, string blockAlias, int depth)
    {
        BlockRoute? wildcard = null;

        foreach (var pm in pagePropertyMappings.Where(p => p.SourceType == "blockContent"))
        {
            var config = ParseResolverConfigModel(pm.ResolverConfig);
            if (config is null)
                continue;

            if (config.Routes is { Count: > 0 } routes)
            {
                var (exact, wc) = SearchRoutesForBlock(routes, blockAlias, depth);
                if (exact is not null)
                    return exact;
                wildcard ??= wc;
            }
            else if (config.NestedMappings is { Count: > 0 } && !string.IsNullOrEmpty(pm.NestedSchemaTypeName))
            {
                wildcard ??= new BlockRoute
                {
                    BlockAlias = string.Empty,
                    NestedSchemaType = pm.NestedSchemaTypeName,
                    PropertyMappings = config.NestedMappings,
                    RequiredProperties = config.RequiredProperties
                };
            }
        }

        return wildcard;
    }

    private (BlockRoute? Exact, BlockRoute? Wildcard) SearchRoutesForBlock(List<BlockRoute> routes, string blockAlias, int depth)
    {
        if (depth > _options.MaxRecursionDepth)
            return (null, null);

        BlockRoute? wildcard = null;
        foreach (var route in routes)
        {
            if (string.IsNullOrEmpty(route.NestedSchemaType))
                continue;

            if (string.Equals(route.BlockAlias, blockAlias, StringComparison.OrdinalIgnoreCase))
                return (route, null);

            if (string.IsNullOrEmpty(route.BlockAlias))
                wildcard ??= route;

            if (route.PropertyMappings is { Count: > 0 } nested)
            {
                foreach (var npm in nested.Where(m => m.Routes is { Count: > 0 }))
                {
                    var (nestedExact, nestedWc) = SearchRoutesForBlock(npm.Routes!, blockAlias, depth + 1);
                    if (nestedExact is not null)
                        return (nestedExact, null);
                    wildcard ??= nestedWc;
                }
            }
        }

        return (null, wildcard);
    }

    private static ResolverConfigModel? ParseResolverConfigModel(string? json)
    {
        if (string.IsNullOrEmpty(json))
            return null;
        try
        {
            return JsonSerializer.Deserialize<ResolverConfigModel>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private string? ResolveIdFromOverrideOrDefault(
        SchemaMapping mapping,
        IPublishedContent content,
        string? contentUrl,
        string? culture)
    {
        if (!string.IsNullOrWhiteSpace(mapping.IdOverride))
        {
            var expanded = ExpandIdTokens(
                mapping.IdOverride,
                contentUrl,
                ResolveSiteUrl(),
                mapping.SchemaTypeName,
                content.Key,
                culture);
            if (!string.IsNullOrWhiteSpace(expanded))
                return expanded;
        }

        if (!string.IsNullOrEmpty(contentUrl))
            return $"{contentUrl}#{mapping.SchemaTypeName.ToLowerInvariant()}";

        return null;
    }

    /// <summary>
    /// Expands @id template tokens. Supported: {url}, {type}, {key}, {culture},
    /// {siteUrl}. Missing context values expand to empty strings so a template
    /// like "{siteUrl}#{type}" still works when the site URL isn't resolvable
    /// at test time.
    /// </summary>
    internal static string ExpandIdTokens(
        string template,
        string? contentUrl,
        string? siteUrl,
        string schemaTypeName,
        Guid contentKey,
        string? culture)
    {
        return template
            .Replace("{url}", contentUrl ?? string.Empty, StringComparison.OrdinalIgnoreCase)
            .Replace("{type}", schemaTypeName.ToLowerInvariant(), StringComparison.OrdinalIgnoreCase)
            .Replace("{key}", contentKey.ToString("D"), StringComparison.OrdinalIgnoreCase)
            .Replace("{culture}", culture ?? string.Empty, StringComparison.OrdinalIgnoreCase)
            .Replace("{siteUrl}", siteUrl ?? string.Empty, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Two-axis resolution: first determines WHERE (which node) via SourceType,
    /// then HOW (value extraction) via the resolver factory based on property editor alias.
    /// </summary>
    private object? ResolveValue(PropertyMapping propMapping, IPublishedContent content, string? culture)
    {
        // Complex type creates a nested Thing with sub-property mappings
        if (propMapping.SourceType == "complexType")
            return ResolveComplexType(propMapping, content, culture);

        // Static values bypass resolver entirely
        if (propMapping.SourceType == "static")
            return propMapping.StaticValue;

        // Determine the target node based on SourceType (WHERE axis)
        var targetNode = ResolveTargetNode(propMapping, content);
        if (targetNode is null)
            return null;

        if (string.IsNullOrEmpty(propMapping.ContentTypePropertyAlias))
            return null;

        // Built-in properties (URL, Name, dates) bypass GetProperty() — resolve directly
        if (SchemeWeaverConstants.BuiltInProperties.IsBuiltIn(propMapping.ContentTypePropertyAlias))
        {
            var builtInResolver = _resolverFactory.GetResolver(SchemeWeaverConstants.BuiltInProperties.EditorAlias);
            var builtInContext = new PropertyResolverContext
            {
                Content = targetNode,
                Mapping = propMapping,
                PropertyAlias = propMapping.ContentTypePropertyAlias,
                SchemaTypeRegistry = _registry,
                MappingRepository = _repository,
                HttpContextAccessor = _httpContextAccessor,
                ResolverFactory = _resolverFactory,
            ComplexTypeBuilder = this,
                Property = null,
                RecursionDepth = 0,
                MaxRecursionDepth = _options.MaxRecursionDepth,
                Culture = culture
            };
            return builtInResolver.Resolve(builtInContext);
        }

        // Get the property and its editor alias
        var publishedProperty = targetNode.GetProperty(propMapping.ContentTypePropertyAlias);
        if (publishedProperty is null)
            return null;

        var editorAlias = publishedProperty.PropertyType?.EditorAlias;

        // Select resolver based on editor alias (HOW axis)
        var resolver = _resolverFactory.GetResolver(editorAlias);

        var context = new PropertyResolverContext
        {
            Content = targetNode,
            Mapping = propMapping,
            PropertyAlias = propMapping.ContentTypePropertyAlias,
            SchemaTypeRegistry = _registry,
            MappingRepository = _repository,
            HttpContextAccessor = _httpContextAccessor,
            ResolverFactory = _resolverFactory,
            ComplexTypeBuilder = this,
            Property = publishedProperty,
            RecursionDepth = 0,
            MaxRecursionDepth = _options.MaxRecursionDepth,
            Culture = culture
        };

        return resolver.Resolve(context);
    }

    /// <summary>
    /// Resolves a complex Schema.org type by creating a nested Thing with sub-property mappings.
    /// </summary>
    private object? ResolveComplexType(PropertyMapping propMapping, IPublishedContent content, string? culture)
    {
        var nestedTypeName = propMapping.NestedSchemaTypeName;
        if (string.IsNullOrEmpty(nestedTypeName))
            return null;

        var config = ParseComplexTypeConfig(propMapping.ResolverConfig);
        return ResolveComplexTypeFromConfig(nestedTypeName, config, content, culture);
    }

    /// <summary>
    /// Recursively resolves a complex Schema.org type from its config.
    ///
    /// The config's own recursion is bounded by the finite JSON structure of resolverConfig, but the
    /// BASE NODE can change as we descend (a picked-item object builds from the picked node), so the
    /// content graph — which is not finite — becomes reachable. <paramref name="recursionDepth"/> and
    /// <paramref name="visitedContentKeys"/> bound that traversal; without them an A-picks-B,
    /// B-picks-A cycle recurses to a StackOverflowException, which cannot be caught and so defeats
    /// the never-break-the-page policy entirely.
    ///
    /// Both parameters default to "top level" so every pre-existing caller behaves exactly as before.
    /// The visited set is checked against <paramref name="content"/> on entry and only THEN extended
    /// with it — a caller passes the chain it has already walked, never the node it is asking for.
    /// </summary>
    /// <inheritdoc />
    public Thing? BuildFromConfig(
        string typeName, ComplexTypeConfigModel config, IPublishedContent content, string? culture,
        int recursionDepth, IReadOnlySet<Guid> visitedContentKeys)
        => ResolveComplexTypeFromConfig(typeName, config, content, culture, recursionDepth, visitedContentKeys);

    private Thing? ResolveComplexTypeFromConfig(
        string typeName, ComplexTypeConfigModel? config, IPublishedContent content, string? culture,
        int recursionDepth = 0, IReadOnlySet<Guid>? visitedContentKeys = null)
    {
        if (recursionDepth >= _options.MaxRecursionDepth)
            return null;

        var visited = visitedContentKeys ?? NoVisitedContentKeys;
        if (visited.Contains(content.Key))
            return null; // this node is already being rendered further up the chain

        var clrType = _registry.GetClrType(typeName);
        if (clrType is null || Activator.CreateInstance(clrType) is not Thing nestedInstance)
            return null;

        if (config?.ComplexTypeMappings is null or { Count: 0 })
            return null; // No sub-mappings configured — skip rather than emit empty object

        // Two-phase (mandatory): resolve EVERY sub-mapping up front so a resolved media ImageObject
        // can be ADOPTED as the nested instance (see TryAdoptImageObject) before any sub-value is
        // applied to it. Adoption must inspect the full resolved list, so nothing is set until phase 2.
        var resolved = new List<(ComplexTypeMappingEntry SubMapping, object Value)>();
        foreach (var subMapping in config.ComplexTypeMappings.Where(m => !string.IsNullOrEmpty(m.SchemaProperty)))
        {
            var value = ResolveSubValue(subMapping, content, culture, recursionDepth, visited);
            if (value is not null)
                resolved.Add((subMapping, value));
        }

        // Render-time repair for persisted MediaPicker→ImageObject shapes (see TryAdoptImageObject).
        nestedInstance = TryAdoptImageObject(nestedInstance, resolved);

        foreach (var (subMapping, value) in resolved)
            SchemaPropertySetter.SetPropertyValue(nestedInstance, subMapping.SchemaProperty, value);

        // Empty-shell guard: when no sub-mapping actually landed a value on the nested
        // instance (all resolved null, or every set was dropped by type conversion), omit
        // the nested Thing entirely — {"@type":"Person"} shells are invalid structured data.
        return SchemaPropertySetter.HasResolvedProperty(nestedInstance) ? nestedInstance : null;
    }

    /// <summary>
    /// Resolves a single complex-type sub-mapping to its value: the 5-arm SourceType switch
    /// (<c>static</c> / <c>property</c> / <c>parent</c>-<c>ancestor</c>-<c>sibling</c> /
    /// <c>complexType</c> / default) plus the transform post-processing. The switch keeps this
    /// file's case-sensitive render-path comparison policy.
    /// A transform applies ONLY to a node-sourced string — on this node or a related one (static
    /// stays untransformed, mirroring the top-level static behaviour; complexType yields a Thing,
    /// not a string) — and, when it collapses to whitespace, drops the sub-value.
    /// </summary>
    private object? ResolveSubValue(
        ComplexTypeMappingEntry subMapping, IPublishedContent content, string? culture,
        int recursionDepth, IReadOnlySet<Guid> visitedContentKeys)
    {
        object? value = subMapping.SourceType switch
        {
            SchemeWeaverConstants.SourceTypes.Static => subMapping.StaticValue,
            SchemeWeaverConstants.SourceTypes.Property when !string.IsNullOrEmpty(subMapping.ContentTypePropertyAlias) =>
                ResolveComplexTypePropertyValue(content, subMapping.ContentTypePropertyAlias, culture, recursionDepth, visitedContentKeys, subMapping),
            SchemeWeaverConstants.SourceTypes.Parent
                or SchemeWeaverConstants.SourceTypes.Ancestor
                or SchemeWeaverConstants.SourceTypes.Sibling
                when !string.IsNullOrEmpty(subMapping.ContentTypePropertyAlias) =>
                ResolveRelatedNodeSubValue(subMapping, content, culture, recursionDepth, visitedContentKeys),
            SchemeWeaverConstants.SourceTypes.ComplexType when !string.IsNullOrEmpty(subMapping.ResolverConfig) =>
                ResolveNestedComplexType(subMapping, content, culture, recursionDepth, visitedContentKeys),
            _ => null
        };

        if (value is string sv
            && IsNodeSourced(subMapping.SourceType)
            && !string.IsNullOrEmpty(subMapping.TransformType))
        {
            var transformed = ApplyTransform(sv, subMapping.TransformType);
            value = string.IsNullOrWhiteSpace(transformed) ? null : transformed;
        }

        return value;
    }

    /// <summary>
    /// True for the source types whose sub-value is read from a content node — this node
    /// (<c>property</c>) or a related one (<c>parent</c>/<c>ancestor</c>/<c>sibling</c>) — and which
    /// are therefore eligible for transform post-processing in <see cref="ResolveSubValue"/>.
    /// </summary>
    private static bool IsNodeSourced(string? sourceType) =>
        string.Equals(sourceType, SchemeWeaverConstants.SourceTypes.Property, StringComparison.OrdinalIgnoreCase)
        || string.Equals(sourceType, SchemeWeaverConstants.SourceTypes.Parent, StringComparison.OrdinalIgnoreCase)
        || string.Equals(sourceType, SchemeWeaverConstants.SourceTypes.Ancestor, StringComparison.OrdinalIgnoreCase)
        || string.Equals(sourceType, SchemeWeaverConstants.SourceTypes.Sibling, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Render-time repair for persisted MediaPicker→ImageObject shapes: the auto-mapper + enricher
    /// historically persisted complexType/ImageObject configs binding e.g. ImageObject.Name &lt;- the
    /// media alias. At render time the media resolves (via the resolver factory) to a FULL ImageObject
    /// that can never be assigned into a string-only sub-property — it would be silently dropped,
    /// leaving an empty {"@type":"ImageObject"} shell. Instead, adopt the first such resolved
    /// ImageObject AS the nested instance (removing it from <paramref name="resolved"/> so it is not
    /// re-applied), then let the caller apply the remaining sub-mappings (e.g. static captions) on top.
    /// The adoption is strictly limited to that broken-shape case — the 3-clause guard requires a
    /// <c>property</c>-sourced sub-mapping that resolves to an ImageObject AND whose target sub-property
    /// does NOT accept an image. A sub-property whose range DOES accept an ImageObject or URL (e.g.
    /// ImageObject.Thumbnail, contentUrl) is a valid config the setter handles — adopting it would
    /// hijack the intended structure (the thumbnail would masquerade as the whole image), so it is
    /// left to bind normally. Returns the (possibly adopted) instance.
    /// </summary>
    private static Thing TryAdoptImageObject(
        Thing nestedInstance,
        List<(ComplexTypeMappingEntry SubMapping, object Value)> resolved)
    {
        if (nestedInstance is not ImageObject)
            return nestedInstance;

        var adoptIndex = resolved.FindIndex(r =>
            string.Equals(r.SubMapping.SourceType, SchemeWeaverConstants.SourceTypes.Property, StringComparison.OrdinalIgnoreCase)
            && FirstImageObject(r.Value) is not null
            && !SchemaPropertySetter.PropertyAcceptsImageValue(nestedInstance, r.SubMapping.SchemaProperty));
        if (adoptIndex < 0)
            return nestedInstance;

        var adopted = FirstImageObject(resolved[adoptIndex].Value)!;
        resolved.RemoveAt(adoptIndex);
        return adopted;
    }

    /// <summary>
    /// Extracts the first Schema.NET <see cref="ImageObject"/> from a resolved sub-mapping
    /// value — a single instance or the first of a resolved list (the two shapes
    /// <see cref="Resolvers.MediaPickerResolver"/> produces). Null when the value is not image-shaped.
    /// </summary>
    private static ImageObject? FirstImageObject(object value) => value switch
    {
        ImageObject image => image,
        IEnumerable<IImageObject> many => many.OfType<ImageObject>().FirstOrDefault(),
        _ => null
    };

    /// <summary>
    /// Resolves a nested complex type sub-mapping by parsing its ResolverConfig and recursing.
    /// </summary>
    private object? ResolveNestedComplexType(
        ComplexTypeMappingEntry entry, IPublishedContent content, string? culture,
        int recursionDepth, IReadOnlySet<Guid> visitedContentKeys)
    {
        var nestedConfig = ParseComplexTypeConfig(entry.ResolverConfig);
        var nestedTypeName = nestedConfig?.SelectedSubType;
        if (string.IsNullOrEmpty(nestedTypeName))
            return null;

        // Same node, one level deeper in the CONFIG — so neither guard advances. The depth counter
        // bounds node hops, not config nesting: config nesting is still bounded by the finite JSON
        // structure of resolverConfig, and incrementing here would silently truncate legitimately
        // deep existing mappings. The visited set likewise passes through: the base node has not moved.
        return ResolveComplexTypeFromConfig(
            nestedTypeName, nestedConfig, content, culture, recursionDepth, visitedContentKeys);
    }

    /// <summary>
    /// Resolves a related-node (parent/ancestor/sibling) complex type sub-mapping:
    /// locates the target node exactly as a top-level related mapping would, then
    /// resolves the sub-property off that node through the resolver pipeline. This
    /// is what lets e.g. an inline Organization's name/logo read the site root.
    /// Sub-mappings resolve relative to the page being generated, at every nesting depth —
    /// EXCEPT inside a picked-item object (<see cref="IComplexTypeBuilder"/>), where the base node
    /// is the PICKED node, so parent/ancestor/sibling walk the picked node's branch of the tree.
    /// </summary>
    private object? ResolveRelatedNodeSubValue(
        ComplexTypeMappingEntry subMapping, IPublishedContent content, string? culture,
        int recursionDepth, IReadOnlySet<Guid> visitedContentKeys)
    {
        var syntheticMapping = new PropertyMapping
        {
            SchemaPropertyName = subMapping.SchemaProperty,
            SourceType = subMapping.SourceType,
            SourceContentTypeAlias = subMapping.SourceContentTypeAlias,
            ContentTypePropertyAlias = subMapping.ContentTypePropertyAlias
        };

        var targetNode = ResolveTargetNode(syntheticMapping, content);
        if (targetNode is null)
            return null;

        return ResolveComplexTypePropertyValue(
            targetNode, subMapping.ContentTypePropertyAlias!, culture, recursionDepth, visitedContentKeys);
    }

    /// <summary>
    /// Resolves a property value for complex type sub-mappings using the resolver factory.
    /// This ensures media pickers, content pickers, built-ins etc. are handled correctly.
    ///
    /// This is the boundary at which a sub-value can leave the current node (a picker property
    /// resolves to whatever it points at), so it is here — and only here — that
    /// <paramref name="content"/> joins the visited chain the resolver pipeline inherits.
    /// </summary>
    private object? ResolveComplexTypePropertyValue(
        IPublishedContent content, string propertyAlias, string? culture,
        int recursionDepth, IReadOnlySet<Guid> visitedContentKeys,
        ComplexTypeMappingEntry? subMapping = null)
    {
        IPublishedProperty? publishedProperty = null;
        string? editorAlias;

        // Built-in properties (__name, __url, dates) bypass GetProperty() — route to the
        // built-in resolver, which reads them straight off the node.
        if (SchemeWeaverConstants.BuiltInProperties.IsBuiltIn(propertyAlias))
        {
            editorAlias = SchemeWeaverConstants.BuiltInProperties.EditorAlias;
        }
        else
        {
            publishedProperty = content.GetProperty(propertyAlias);
            if (publishedProperty is null)
                return null;

            editorAlias = publishedProperty.PropertyType?.EditorAlias;
        }

        var resolver = _resolverFactory.GetResolver(editorAlias);

        // A picker sub-row historically got a BLANK synthetic mapping, so PickedContentResolver saw
        // no config, skipped both drill-down and whole-item nesting, and emitted the picked node's
        // NAME for every sub-property — Person.jobTitle rendering the author's name, Organization.logo
        // rendering "Jane Doe". Forward the sub-row's own ResolverConfig so a picker sub-row can be
        // configured exactly like a top-level picker row.
        //
        // Gated on the editor alias so this is provably inert for every non-picker sub-row: a stale
        // complexType/blockContent blob left on a property sub-row must never reach BlockContentResolver.
        var isPicker = editorAlias is not null
            && SchemeWeaverConstants.PropertyEditors.ContentPickerAliases.Contains(editorAlias);
        var pickedConfig = isPicker ? PickedContentResolver.ParseConfig(subMapping?.ResolverConfig) : null;

        var context = new PropertyResolverContext
        {
            Content = content,
            Mapping = new Models.Entities.PropertyMapping
            {
                SchemaPropertyName = subMapping?.SchemaProperty ?? string.Empty,
                ContentTypePropertyAlias = propertyAlias,
                SourceType = "property",
                ResolverConfig = isPicker ? subMapping?.ResolverConfig : null,
                NestedSchemaTypeName = pickedConfig?.NestedSchemaTypeName
            },
            PropertyAlias = propertyAlias,
            SchemaTypeRegistry = _registry,
            MappingRepository = _repository,
            HttpContextAccessor = _httpContextAccessor,
            ResolverFactory = _resolverFactory,
            ComplexTypeBuilder = this,
            Property = publishedProperty,
            RecursionDepth = recursionDepth,
            MaxRecursionDepth = _options.MaxRecursionDepth,
            VisitedContentKeys = new HashSet<Guid>(visitedContentKeys) { content.Key },
            Culture = culture
        };

        return resolver.Resolve(context);
    }

    private ComplexTypeConfigModel? ParseComplexTypeConfig(string? json)
    {
        if (string.IsNullOrEmpty(json))
            return null;

        try
        {
            return JsonSerializer.Deserialize<ComplexTypeConfigModel>(json,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Failed to parse complex type ResolverConfig JSON: {Json}", json);
            return null;
        }
    }

    /// <summary>
    /// Resolves the target IPublishedContent node based on the SourceType (WHERE axis).
    /// </summary>
    private IPublishedContent? ResolveTargetNode(PropertyMapping propMapping, IPublishedContent content)
    {
        return propMapping.SourceType switch
        {
            "property" or "blockContent" => content,
            "parent" => content.Parent<IPublishedContent>(_navigationQueryService, _publishedStatusFilteringService),
            "ancestor" => ResolveAncestorNode(content, propMapping),
            "sibling" => ResolveSiblingNode(content, propMapping),
            _ => null
        };
    }

    private IPublishedContent? ResolveAncestorNode(IPublishedContent content, PropertyMapping propMapping)
    {
        var ancestors = content.Ancestors(_navigationQueryService, _publishedStatusFilteringService)
            .Where(node => string.IsNullOrEmpty(propMapping.SourceContentTypeAlias)
                || string.Equals(node.ContentType.Alias, propMapping.SourceContentTypeAlias, StringComparison.OrdinalIgnoreCase));

        foreach (var node in ancestors)
        {
            if (string.IsNullOrEmpty(propMapping.ContentTypePropertyAlias))
                continue;

            // Built-in properties always exist on content nodes
            if (SchemeWeaverConstants.BuiltInProperties.IsBuiltIn(propMapping.ContentTypePropertyAlias))
                return node;

            // Invariant probe: check existence without culture so we find the node
            // regardless of which language variant has a value
            if (node.GetProperty(propMapping.ContentTypePropertyAlias)?.GetValue() is not null)
                return node;
        }

        return null;
    }

    private IPublishedContent? ResolveSiblingNode(IPublishedContent content, PropertyMapping propMapping)
    {
        var parent = content.Parent<IPublishedContent>(_navigationQueryService, _publishedStatusFilteringService);
        var siblings = parent?.Children(_navigationQueryService, _publishedStatusFilteringService);
        if (siblings is null)
            return null;

        var candidates = siblings
            .Where(sibling => sibling.Id != content.Id)
            .Where(sibling => string.IsNullOrEmpty(propMapping.SourceContentTypeAlias)
                || string.Equals(sibling.ContentType.Alias, propMapping.SourceContentTypeAlias, StringComparison.OrdinalIgnoreCase));

        foreach (var sibling in candidates)
        {
            if (string.IsNullOrEmpty(propMapping.ContentTypePropertyAlias))
                continue;

            // Built-in properties always exist on content nodes
            if (SchemeWeaverConstants.BuiltInProperties.IsBuiltIn(propMapping.ContentTypePropertyAlias))
                return sibling;

            // Invariant probe: check existence without culture so we find the node
            // regardless of which language variant has a value
            if (sibling.GetProperty(propMapping.ContentTypePropertyAlias)?.GetValue() is not null)
                return sibling;
        }

        return null;
    }

    // Transform logic lives in SchemaValueTransformer so the nested-block resolver applies
    // the same stripHtml/toAbsoluteUrl/formatDate behaviour. This thin wrapper keeps the
    // existing call sites unchanged.
    private string? ApplyTransform(string? value, string? transformType)
        => SchemaValueTransformer.Apply(value, transformType, _httpContextAccessor, _logger);

    /// <summary>
    /// Serialises a Schema.NET Thing to JSON-LD, working around property name collisions
    /// in Schema.NET's interface hierarchy (e.g. IDrug.Funding, IArchiveOrganization.Address).
    /// Uses <see cref="Thing.ToString()"/> for the 697 types that serialise cleanly, and
    /// falls back to a <see cref="DeduplicatingTypeInfoResolver"/> for the ~83 that don't.
    ///
    /// The result is re-encoded through <see cref="ReEncodeWithHtmlSafeEncoder"/> first, because
    /// this string is written raw into a <c>&lt;script type="application/ld+json"&gt;</c> block on
    /// the non-graph path and Schema.NET's own serialiser leaves <c>&lt;</c>/<c>&gt;</c> unescaped.
    /// </summary>
    private string? SafeSerialize(Thing? thing, int? contentId = null)
    {
        if (thing is null)
            return null;

        try
        {
            return ReEncodeWithHtmlSafeEncoder(thing.ToString());
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("collides with another property"))
        {
            _logger.LogDebug(ex,
                "Schema.NET type {SchemaType} has a property collision — using fallback serialiser",
                thing.GetType().Name);

            try
            {
                // _deduplicatingOptions carries no explicit Encoder, so it uses
                // JavaScriptEncoder.Default, which already escapes < > & — no re-encode needed.
                return JsonSerializer.Serialize<object>(thing, _deduplicatingOptions);
            }
            catch (JsonException inner)
            {
                _logger.LogWarning(inner,
                    "Fallback serialisation also failed for {SchemaType} (content {ContentId})",
                    thing.GetType().Name, contentId);
                return null;
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Failed to serialise Schema.NET type {SchemaType} for content {ContentId}",
                thing.GetType().Name, contentId);
            return null;
        }
    }

    /// <summary>
    /// Re-emits already-valid JSON through an encoder that escapes the HTML-sensitive characters
    /// <c>&lt; &gt; &amp; '</c> while still writing non-ASCII literally, so "Textkörper" stays
    /// readable rather than becoming "Textkörper".
    ///
    /// Schema.NET's <see cref="Thing.ToString()"/> emits <c>&lt;</c> and <c>&gt;</c> raw. On the
    /// non-graph path that string is written straight into a
    /// <c>&lt;script type="application/ld+json"&gt;</c> block, and the HTML parser ends a script
    /// element on a literal <c>&lt;/script&gt;</c> regardless of its type attribute — so an
    /// unescaped mapped value can break out of the block (stored XSS). This matters more since
    /// <c>StripHtmlTags</c> began HTML-DECODING entities: text an editor typed as
    /// <c>&lt;/script&gt;</c>, which Umbraco stores encoded, now reaches serialisation as a live
    /// closing tag. The graph path already gets this protection from <c>GraphGenerator</c>'s
    /// writer options; this gives the legacy path the identical guarantee.
    ///
    /// Input that is not parseable JSON is returned unchanged — this is a hardening step, not a
    /// validator, and must never turn serialisable output into no output.
    /// </summary>
    private static string ReEncodeWithHtmlSafeEncoder(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            using var buffer = new MemoryStream();
            using (var writer = new Utf8JsonWriter(buffer, _htmlSafeWriterOptions))
            {
                document.WriteTo(writer);
            }

            return System.Text.Encoding.UTF8.GetString(buffer.ToArray());
        }
        catch (JsonException)
        {
            return json;
        }
    }

    /// <inheritdoc />
    public IEnumerable<string> GenerateInheritedJsonLdStrings(IPublishedContent content, string? culture = null)
    {
        var inheritedAliases = _repository.GetInheritedMappings()
            .Select(m => m.ContentTypeAlias)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        if (inheritedAliases.Count == 0)
            return [];

        // Walk up from the parent (not the current page) to avoid duplicating the current page's own schema
        var results = new List<string>();
        var current = content.Parent<IPublishedContent>(_navigationQueryService, _publishedStatusFilteringService);
        while (current is not null)
        {
            if (inheritedAliases.Contains(current.ContentType.Alias))
            {
                var jsonLd = GenerateJsonLdString(current, culture);
                if (!string.IsNullOrEmpty(jsonLd))
                    results.Add(jsonLd);
            }

            current = current.Parent<IPublishedContent>(_navigationQueryService, _publishedStatusFilteringService);
        }

        results.Reverse(); // Root-first order: Website before intermediate schemas
        return results;
    }

    /// <inheritdoc />
    public IEnumerable<string> GenerateBlockElementJsonLdStrings(IPublishedContent content, string? culture = null)
    {
        // Batch-load all mappings AND all property mappings in two queries and
        // index by alias / mapping id for O(1) lookups. This avoids N+1 queries
        // when a page has multiple block element types (or many blocks of the
        // same type).
        var allMappings = _repository.GetAll()
            .Where(m => m.IsEnabled)
            .ToDictionary(m => m.ContentTypeAlias, StringComparer.OrdinalIgnoreCase);
        var propertyMappingsByMappingId = _repository.GetAllPropertyMappingsByMappingId();

        // Identify properties already explicitly mapped via blockContent source type to avoid duplicates
        allMappings.TryGetValue(content.ContentType.Alias, out var currentMapping);
        var explicitBlockProperties = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (currentMapping is not null)
        {
            var currentPropertyMappings = propertyMappingsByMappingId.GetValueOrDefault(currentMapping.Id) ?? [];
            foreach (var pm in currentPropertyMappings
                .Where(pm => pm.SourceType == "blockContent" && !string.IsNullOrEmpty(pm.ContentTypePropertyAlias)))
            {
                explicitBlockProperties.Add(pm.ContentTypePropertyAlias!);
            }
        }

        foreach (var property in content.Properties
            .Where(p => p.PropertyType?.EditorAlias is "Umbraco.BlockList" or "Umbraco.BlockGrid")
            .Where(p => !explicitBlockProperties.Contains(p.Alias)))
        {
            var value = property.GetValue(culture: culture);
            if (value is null)
                continue;

            // Walk the whole block subtree: top-level blocks, Block Grid area blocks, AND
            // blocks nested inside a block's own Block List/Grid properties — so a nested
            // block that carries its own mapping still emits a standalone JSON-LD script.
            var visited = new HashSet<Guid>();
            foreach (var element in EnumerateNestedBlockElements(value, culture, 0, visited))
            {
                if (!allMappings.TryGetValue(element.ContentType.Alias, out var mapping))
                    continue;

                var elementPropertyMappings = propertyMappingsByMappingId.GetValueOrDefault(mapping.Id) ?? [];
                var thing = GenerateThingFromElement(element, mapping, elementPropertyMappings);
                if (thing is not null)
                {
                    var jsonLd = SafeSerialize(thing);
                    if (!string.IsNullOrEmpty(jsonLd))
                        yield return jsonLd;
                }
            }
        }
    }

    /// <summary>
    /// Maximum block-nesting depth walked when discovering standalone block-element JSON-LD,
    /// matching <see cref="Resolvers.PropertyResolverContext.MaxRecursionDepth"/>.
    /// </summary>
    private const int MaxBlockDiscoveryDepth = 3;

    /// <summary>
    /// Depth-first enumeration of every block element reachable from a Block List/Grid value:
    /// top-level blocks, Block Grid area blocks, and blocks held in a block element's own
    /// Block List/Grid properties. Depth-capped and de-duplicated by element key to guard
    /// against deep or cyclic structures.
    /// </summary>
    private static IEnumerable<IPublishedElement> EnumerateNestedBlockElements(
        object? value, string? culture, int depth, HashSet<Guid> visited)
    {
        if (depth > MaxBlockDiscoveryDepth)
            yield break;

        IEnumerable<IPublishedElement>? items = value switch
        {
            Umbraco.Cms.Core.Models.Blocks.BlockListModel blockList => blockList.Select(b => b.Content),
            Umbraco.Cms.Core.Models.Blocks.BlockGridModel blockGrid => FlattenGrid(blockGrid),
            _ => null
        };

        if (items is null)
            yield break;

        foreach (var element in items)
        {
            if (!visited.Add(element.Key))
                continue;

            yield return element;

            foreach (var nestedProperty in element.Properties
                .Where(p => p.PropertyType?.EditorAlias is "Umbraco.BlockList" or "Umbraco.BlockGrid"))
            {
                var nestedValue = nestedProperty.GetValue(culture: culture);
                foreach (var nested in EnumerateNestedBlockElements(nestedValue, culture, depth + 1, visited))
                    yield return nested;
            }
        }
    }

    /// <summary>Flattens a Block Grid (top-level items plus every nested area) into one sequence.</summary>
    private static IEnumerable<IPublishedElement> FlattenGrid(
        IEnumerable<Umbraco.Cms.Core.Models.Blocks.BlockGridItem> items)
    {
        foreach (var item in items)
        {
            yield return item.Content;

            foreach (var area in item.Areas)
                foreach (var areaItem in FlattenGrid(area))
                    yield return areaItem;
        }
    }

    /// <summary>
    /// Generates a Thing from a block element using its schema mapping.
    /// Only supports "property" and "static" source types (block elements have no parents/ancestors).
    /// </summary>
    private Thing? GenerateThingFromElement(
        IPublishedElement element,
        SchemaMapping mapping,
        IEnumerable<PropertyMapping> propertyMappings)
    {
        var clrType = _registry.GetClrType(mapping.SchemaTypeName);
        if (clrType is null)
            return null;

        if (Activator.CreateInstance(clrType) is not Thing instance)
            return null;

        foreach (var propMapping in propertyMappings)
        {
            try
            {
                object? value = propMapping.SourceType switch
                {
                    "static" => propMapping.StaticValue,
                    "property" when !string.IsNullOrEmpty(propMapping.ContentTypePropertyAlias) =>
                        ResolveElementPropertyValue(element, propMapping.ContentTypePropertyAlias),
                    _ => null
                };

                if (value is null)
                    continue;

                if (value is string stringValue)
                {
                    if (string.IsNullOrWhiteSpace(stringValue))
                        continue;
                    value = ApplyTransform(stringValue, propMapping.TransformType);
                }

                // Guard against null after transform (ApplyTransform can return null)
                if (value is null or string { Length: 0 })
                    continue;

                SchemaPropertySetter.SetPropertyValue(instance, propMapping.SchemaPropertyName, value);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to map property {Property} for block element {ElementType}",
                    propMapping.SchemaPropertyName, element.ContentType.Alias);
            }
        }

        return instance;
    }

    /// <summary>
    /// Resolves a property value from an IPublishedElement (block content).
    /// Uses the resolver factory for proper handling of media pickers, rich text, etc.
    /// </summary>
    private object? ResolveElementPropertyValue(IPublishedElement element, string propertyAlias)
    {
        var publishedProperty = element.GetProperty(propertyAlias);
        if (publishedProperty is null)
            return null;

        // Use the utility for media extraction, fall back to GetValue().ToString()
        return SchemaPropertySetter.ResolveElementPropertyValue(element, propertyAlias, _httpContextAccessor);
    }

}
