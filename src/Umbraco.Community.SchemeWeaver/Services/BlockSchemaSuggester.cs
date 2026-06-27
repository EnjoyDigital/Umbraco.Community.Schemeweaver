using Umbraco.Community.SchemeWeaver.Models.Api;

namespace Umbraco.Community.SchemeWeaver.Services;

/// <summary>
/// Heuristic suggester that maps each block element type in a BlockList/BlockGrid to a
/// best-fit Schema.org type and target page property, then reuses
/// <see cref="ISchemaAutoMapper.SuggestMappings"/> to fill in each route's per-property
/// mappings against the element type's own properties.
/// </summary>
public class BlockSchemaSuggester : IBlockSchemaSuggester
{
    private readonly ISchemaAutoMapper _autoMapper;
    private readonly ISchemaTypeRegistry _registry;

    /// <summary>Base confidence awarded to a catalogue (keyword) hit.</summary>
    private const int CatalogueConfidence = 80;

    private const string MainEntity = "mainEntity";
    private const string HasPart = "hasPart";
    private const string About = "about";
    private const string CreativeWork = "CreativeWork";

    /// <summary>
    /// Keyword catalogue. Keywords match (after alphanumeric-lowercasing) the block
    /// alias/name AND any property alias/name. First entry whose keyword is contained
    /// in any token wins. <see cref="CanBeMainEntity"/> marks page-defining types that
    /// may claim the single mainEntity slot.
    /// </summary>
    private sealed record CatalogueEntry(
        string[] Keywords, string SchemaType, string TargetProperty, bool CanBeMainEntity = false);

    private static readonly CatalogueEntry[] Catalogue =
    [
        new(["faq", "question", "accordion"], "Question", MainEntity, CanBeMainEntity: true),
        new(["team", "member", "people", "staff"], "Person", HasPart),
        new(["contact", "addressdetails", "address", "locationdetails"], "PostalAddress", "about"),
        new(["map", "geo", "coordinates"], "Place", HasPart),
        new(["testimonial", "review", "quote"], "Review", HasPart),
        new(["hero", "banner", "masthead"], "WPHeader", HasPart),
        new(["service", "feature", "offering"], "Service", HasPart),
        new(["logo", "partner", "client"], "Organization", HasPart),
    ];

    /// <summary>
    /// Block-identity keywords that mark a block as carrying no mappable schema entity
    /// (rich text bodies, CTAs, links/buttons). Matched against the block alias/name
    /// only, and applied before the catalogue so an explicitly content-less block is
    /// never force-fitted to a schema type.
    /// </summary>
    private static readonly string[] SkipKeywords =
        ["richtext", "body", "cta", "calltoaction", "link", "button"];

    public BlockSchemaSuggester(ISchemaAutoMapper autoMapper, ISchemaTypeRegistry registry)
    {
        _autoMapper = autoMapper;
        _registry = registry;
    }

    /// <summary>Maximum block-nesting depth the suggester will descend.</summary>
    private const int MaxNestDepth = 3;

    public IEnumerable<BlockMappingSuggestion> Suggest(IEnumerable<BlockElementTypeInfo> elementTypes)
        => Suggest(elementTypes, depth: 0);

    private List<BlockMappingSuggestion> Suggest(IEnumerable<BlockElementTypeInfo> elementTypes, int depth)
    {
        // Each entry: the built route plus its (mutable) target property.
        var routed = new List<RoutePlan>();

        foreach (var element in elementTypes)
        {
            var identityTokens = new[]
            {
                Normalise(element.Alias),
                Normalise(element.Name)
            };

            // Explicit skip: content-less blocks (rich text, CTA, link, button).
            if (SkipKeywords.Any(k => identityTokens.Any(t => t.Length > 0 && t.Contains(k, StringComparison.Ordinal))))
                continue;

            var propertyTokens = element.Properties.Select(Normalise)
                .Concat(element.PropertyInfos.Select(p => Normalise(p.Name)))
                .Where(t => t.Length > 0)
                .ToArray();

            // Catalogue best-fit. Block identity (alias/name) is authoritative, so a
            // stray property keyword can't hijack the type (e.g. an "emailAddress"
            // field on a hero block must not route it to PostalAddress). Fall back to
            // property keywords only when identity is inconclusive. No hit -> skip
            // (do not emit junk; respect the >=60 bar).
            var entry = MatchCatalogue(identityTokens) ?? MatchCatalogue(propertyTokens);
            if (entry is null)
                continue;

            var route = new BlockRouteSuggestion
            {
                BlockAlias = element.Alias,
                NestedSchemaType = entry.SchemaType,
                Confidence = CatalogueConfidence,
                PropertyMappings = BuildPropertyMappings(element.Alias, entry.SchemaType)
            };

            // Recurse into any property that is itself a Block List/Grid, attaching the nested
            // block's suggested routes to a property mapping keyed by that block property.
            route.PropertyMappings.AddRange(BuildNestedBlockMappings(element, depth));

            routed.Add(new RoutePlan(route, ResolveTarget(entry.SchemaType, entry.TargetProperty), entry.CanBeMainEntity));
        }

        ApplyMainEntityDominance(routed);

        // Group by final target schema property — one suggestion per target, each
        // carrying the routes (block element types) that feed it.
        return routed
            .GroupBy(r => r.Target, StringComparer.OrdinalIgnoreCase)
            .Select(g => new BlockMappingSuggestion
            {
                SchemaProperty = g.Key,
                Confidence = g.Max(r => r.Route.Confidence),
                Routes = g.Select(r => r.Route).ToList()
            })
            .ToList();
    }

    /// <summary>
    /// Returns the first catalogue entry whose keyword is contained in any of the
    /// supplied (already normalised) tokens, or null when none match.
    /// </summary>
    private static CatalogueEntry? MatchCatalogue(IReadOnlyCollection<string> tokens)
    {
        if (tokens.Count == 0)
            return null;

        return Catalogue.FirstOrDefault(c =>
            c.Keywords.Any(k => tokens.Any(t => t.Contains(k, StringComparison.Ordinal))));
    }

    /// <summary>
    /// Reuses the per-property heuristic auto-mapper against the block element type's
    /// own properties, keeping only concrete content-property mappings (skips
    /// static/reference/complex/blockContent rows and built-in __ aliases that don't
    /// resolve on block elements).
    /// </summary>
    private List<BlockRoutePropertyMappingSuggestion> BuildPropertyMappings(string elementAlias, string schemaType)
    {
        var suggestions = _autoMapper.SuggestMappings(elementAlias, schemaType);
        var result = new List<BlockRoutePropertyMappingSuggestion>();

        foreach (var s in suggestions)
        {
            if (string.IsNullOrEmpty(s.SuggestedContentTypePropertyAlias))
                continue;
            if (!string.Equals(s.SuggestedSourceType, "property", StringComparison.OrdinalIgnoreCase))
                continue;
            if (s.SuggestedContentTypePropertyAlias.StartsWith(SchemeWeaverConstants.BuiltInProperties.Prefix, StringComparison.Ordinal))
                continue;

            result.Add(new BlockRoutePropertyMappingSuggestion
            {
                SchemaProperty = s.SchemaPropertyName,
                ContentProperty = s.SuggestedContentTypePropertyAlias
            });
        }

        return result;
    }

    /// <summary>
    /// For each property on the element type that is itself a Block List/Grid, suggests routes
    /// for the nested block's element types and returns them as property mappings keyed by the
    /// nested block property. Each carries child <c>Routes</c>, recursing the routing model one
    /// level deeper. Depth-capped to mirror the resolver's nesting limit.
    /// </summary>
    private List<BlockRoutePropertyMappingSuggestion> BuildNestedBlockMappings(
        BlockElementTypeInfo element, int depth)
    {
        var result = new List<BlockRoutePropertyMappingSuggestion>();
        if (depth + 1 >= MaxNestDepth)
            return result;

        foreach (var propInfo in element.PropertyInfos.Where(p => p.NestedBlockElementTypes.Count > 0))
        {
            foreach (var suggestion in Suggest(propInfo.NestedBlockElementTypes, depth + 1))
            {
                if (suggestion.Routes.Count == 0)
                    continue;

                result.Add(new BlockRoutePropertyMappingSuggestion
                {
                    SchemaProperty = suggestion.SchemaProperty,
                    ContentProperty = propInfo.Alias,
                    Routes = suggestion.Routes
                });
            }
        }

        return result;
    }

    /// <summary>
    /// Dominance rule: at most ONE block element type may target mainEntity. The
    /// highest-confidence candidate keeps it; the rest fall back to hasPart. Ties are
    /// broken by input order (the first catalogue match wins).
    /// </summary>
    private void ApplyMainEntityDominance(List<RoutePlan> routed)
    {
        var mainCandidates = routed
            .Select((plan, index) => (plan, index))
            .Where(x => string.Equals(x.plan.Target, MainEntity, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(x => x.plan.Route.Confidence)
            .ToList();

        if (mainCandidates.Count <= 1)
            return;

        // The highest-confidence candidate keeps mainEntity; the rest fall back to a
        // range-safe supporting target (hasPart for CreativeWork, else about).
        foreach (var (plan, index) in mainCandidates.Skip(1))
            routed[index] = plan with { Target = ResolveTarget(plan.Route.NestedSchemaType, HasPart) };
    }

    /// <summary>
    /// Resolves a range-safe target page property for a nested schema type. The most
    /// common catalogue target, <c>hasPart</c>, only accepts <c>CreativeWork</c> — so a
    /// non-CreativeWork type (Person, Place, Service, Organization) routed there would be
    /// silently discarded by the strongly-typed Schema.NET model at generation time.
    /// Those fall back to <c>about</c> (range <c>Thing</c>), which accepts any entity.
    /// <c>mainEntity</c> and <c>about</c> are already <c>Thing</c>-range and pass through.
    /// </summary>
    private string ResolveTarget(string schemaType, string desiredTarget)
    {
        if (string.Equals(desiredTarget, HasPart, StringComparison.Ordinal)
            && !IsCreativeWork(schemaType))
            return About;

        return desiredTarget;
    }

    /// <summary>
    /// Walks the registry's parent-type chain to determine whether <paramref name="schemaType"/>
    /// is (or descends from) <c>CreativeWork</c>. The guard caps the walk defensively in
    /// case of an unexpected cycle in the type metadata.
    /// </summary>
    private bool IsCreativeWork(string schemaType)
    {
        var name = schemaType;
        for (var guard = 0; !string.IsNullOrEmpty(name) && guard < 50; guard++)
        {
            if (string.Equals(name, CreativeWork, StringComparison.Ordinal))
                return true;

            name = _registry.GetType(name)?.ParentTypeName;
        }

        return false;
    }

    /// <summary>Lowercases and strips all non-alphanumeric characters for tolerant matching.</summary>
    private static string Normalise(string? value)
    {
        if (string.IsNullOrEmpty(value))
            return string.Empty;

        Span<char> buffer = value.Length <= 128 ? stackalloc char[value.Length] : new char[value.Length];
        var length = 0;
        foreach (var ch in value)
        {
            if (char.IsLetterOrDigit(ch))
                buffer[length++] = char.ToLowerInvariant(ch);
        }

        return new string(buffer[..length]);
    }

    private sealed record RoutePlan(BlockRouteSuggestion Route, string Target, bool CanBeMainEntity);
}
