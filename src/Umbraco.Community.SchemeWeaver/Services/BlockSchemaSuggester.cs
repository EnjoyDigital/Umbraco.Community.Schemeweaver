using Umbraco.Community.SchemeWeaver.Models.Api;
using Umbraco.Community.SchemeWeaver.Services.Advisory;
using Umbraco.Community.SchemeWeaver.Services.Validation;

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
    private readonly ISchemaRangeChecker _rangeChecker;

    /// <summary>Base confidence awarded to a catalogue (keyword) hit.</summary>
    private const int CatalogueConfidence = 80;

    private const string MainEntity = "mainEntity";
    private const string HasPart = "hasPart";
    private const string About = "about";
    private const string ReviewTarget = "review";
    private const string CreativeWork = "CreativeWork";

    /// <summary>
    /// Targets every context type can host: the Thing/CreativeWork catch-alls the
    /// catalogue has always used. Anything else (currently <see cref="ReviewTarget"/>)
    /// is type-specific and only survives <see cref="ResolveTarget"/> when the context
    /// schema type actually declares the property.
    /// </summary>
    private static readonly string[] UniversalTargets = [MainEntity, HasPart, About];

    /// <summary>
    /// Keyword catalogue. Keywords match (after alphanumeric-lowercasing) the block
    /// alias/name AND any property alias/name. First entry whose keyword is contained
    /// in any token wins. <see cref="CanBeMainEntity"/> marks page-defining types that
    /// may claim the single mainEntity slot.
    /// </summary>
    private sealed record CatalogueEntry(
        string[] Keywords, string SchemaType, string TargetProperty, bool CanBeMainEntity = false,
        MappingTemplate[]? Mappings = null);

    /// <summary>
    /// A keyword-resolved property mapping for a catalogue entry: the nested Schema.org property
    /// to set, the content-property keywords that select the block element's source property
    /// (matched the same way the catalogue matches block identity), and optional wrap fields when
    /// the value must be wrapped in a Schema.org object (e.g. an answer wrapped in <c>Answer.Text</c>).
    /// Resolved against the block element's real properties; unresolved templates are simply skipped.
    /// Recipes mirror <see cref="SchemaAutoMapper.PopularSchemaDefaults"/> so the suggester and the
    /// top-level auto-mapper agree.
    /// </summary>
    private sealed record MappingTemplate(
        string SchemaProperty, string[] ContentKeywords, string? WrapInType = null, string? WrapInProperty = null);

    private static readonly CatalogueEntry[] Catalogue =
    [
        new(["faq", "question", "accordion"], "Question", MainEntity, CanBeMainEntity: true,
            Mappings:
            [
                new("name", ["question", "title", "heading"]),
                new("acceptedAnswer", ["answer", "response"], WrapInType: "Answer", WrapInProperty: "Text"),
            ]),
        new(["team", "member", "people", "staff"], "Person", HasPart,
            Mappings:
            [
                new("name", ["name", "fullname"]),
                new("jobTitle", ["jobtitle", "role", "position", "title"]),
                new("image", ["image", "photo", "picture", "avatar", "headshot"]),
                new("description", ["bio", "biography", "description", "about"]),
            ]),
        new(["contact", "addressdetails", "address", "locationdetails"], "PostalAddress", "about",
            Mappings:
            [
                new("streetAddress", ["street", "addressline", "line1", "address"]),
                new("addressLocality", ["city", "town", "locality"]),
                new("addressRegion", ["region", "county", "state", "province"]),
                new("postalCode", ["postcode", "postalcode", "zip"]),
            ]),
        new(["map", "geo", "coordinates"], "Place", HasPart),
        // Preferred target is the type-specific `review` (right for Product-like pages);
        // ResolveTarget demotes it to hasPart when the page type doesn't declare review —
        // safe because Review IS a CreativeWork, so the hasPart→about rule never fires.
        new(["testimonial", "review", "quote"], "Review", ReviewTarget,
            Mappings:
            [
                new("author", ["author", "reviewer", "name", "customer"]),
                new("reviewBody", ["review", "body", "quote", "testimonial", "text", "comment"]),
                new("reviewRating", ["rating", "score", "stars"], WrapInType: "Rating", WrapInProperty: "RatingValue"),
            ]),
        new(["hero", "banner", "masthead"], "WPHeader", HasPart),
        new(["service", "feature", "offering"], "Service", HasPart,
            Mappings:
            [
                new("name", ["name", "title", "heading"]),
                new("description", ["description", "summary", "body", "intro"]),
            ]),
        new(["logo", "partner", "client"], "Organization", HasPart,
            Mappings:
            [
                new("name", ["name", "title"]),
                new("logo", ["logo", "image"]),
                new("url", ["url", "link", "website"]),
            ]),
    ];

    /// <summary>
    /// Block-identity keywords that mark a block as carrying no mappable schema entity
    /// (rich text bodies, CTAs, links/buttons). Matched against the block alias/name
    /// only, and applied before the catalogue so an explicitly content-less block is
    /// never force-fitted to a schema type.
    /// </summary>
    private static readonly string[] SkipKeywords =
        ["richtext", "body", "cta", "calltoaction", "link", "button"];

    public BlockSchemaSuggester(ISchemaAutoMapper autoMapper, ISchemaTypeRegistry registry, ISchemaRangeChecker rangeChecker)
    {
        _autoMapper = autoMapper;
        _registry = registry;
        _rangeChecker = rangeChecker;
    }

    /// <summary>Maximum block-nesting depth the suggester will descend.</summary>
    private const int MaxNestDepth = 3;

    public IEnumerable<BlockMappingSuggestion> Suggest(
        IEnumerable<BlockElementTypeInfo> elementTypes,
        string? pageSchemaType = null,
        string? targetSchemaProperty = null)
    {
        var suggestions = Suggest(elementTypes, pageSchemaType, depth: 0);

        if (!string.IsNullOrWhiteSpace(targetSchemaProperty))
            AnnotateTargetFit(suggestions, pageSchemaType, targetSchemaProperty!);

        return suggestions;
    }

    private List<BlockMappingSuggestion> Suggest(IEnumerable<BlockElementTypeInfo> elementTypes, string? contextSchemaType, int depth)
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
                PropertyMappings = BuildPropertyMappings(element, entry)
            };

            // Recurse into any property that is itself a Block List/Grid, attaching the nested
            // block's suggested routes to a property mapping keyed by that block property.
            // The nested context type is THIS route's schema type: the nested target property
            // is set on the parent block's entity, not on the page.
            route.PropertyMappings.AddRange(BuildNestedBlockMappings(element, entry.SchemaType, depth));

            routed.Add(new RoutePlan(route, ResolveTarget(entry.SchemaType, entry.TargetProperty, contextSchemaType), entry.CanBeMainEntity));
        }

        ApplyMainEntityDominance(routed, contextSchemaType);

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
    /// Builds the per-property mappings for a route. First resolves the catalogue entry's
    /// keyword <see cref="CatalogueEntry.Mappings"/> template against the element's real
    /// properties (robust regardless of the auto-mapper's confidence threshold and the
    /// built-in-alias filter), then SUPPLEMENTS with the per-property heuristic auto-mapper
    /// for any schema property the template did not already cover — keeping only concrete
    /// content-property rows (skips static/reference/complex/blockContent rows and built-in
    /// __ aliases that don't resolve on block elements).
    /// </summary>
    private List<BlockRoutePropertyMappingSuggestion> BuildPropertyMappings(BlockElementTypeInfo element, CatalogueEntry entry)
    {
        var result = ResolveTemplate(element, entry);
        var covered = new HashSet<string>(result.Select(r => r.SchemaProperty), StringComparer.OrdinalIgnoreCase);

        var suggestions = _autoMapper.SuggestMappings(element.Alias, entry.SchemaType);
        // Concrete content-property rows only; the keyword template wins for
        // schema properties it already mapped.
        foreach (var s in suggestions.Where(s =>
                     !string.IsNullOrEmpty(s.SuggestedContentTypePropertyAlias)
                     && string.Equals(s.SuggestedSourceType, "property", StringComparison.OrdinalIgnoreCase)
                     && !s.SuggestedContentTypePropertyAlias.StartsWith(SchemeWeaverConstants.BuiltInProperties.Prefix, StringComparison.Ordinal)
                     && !covered.Contains(s.SchemaPropertyName)))
        {
            result.Add(new BlockRoutePropertyMappingSuggestion
            {
                SchemaProperty = s.SchemaPropertyName,
                ContentProperty = s.SuggestedContentTypePropertyAlias!, // non-null: guarded in the Where above
                // §3b: pre-fill stripHtml when a rich-text source feeds a plain-text nested property,
                // so the suggested route emits clean text by default (revertible by the author).
                TransformType = ShouldStripHtml(s) ? "stripHtml" : null,
            });
        }

        return result;
    }

    /// <summary>
    /// Resolves a catalogue entry's keyword mapping template against the block element's real
    /// properties: for each template row, picks the first not-yet-used property whose normalised
    /// alias/name contains one of the template's content keywords. Produces robust, threshold-free
    /// mappings for well-known shapes (FAQ → Question, Team → Person, …) so the auto-map wand
    /// always populates real values when the block matches a catalogue entry.
    /// </summary>
    private static List<BlockRoutePropertyMappingSuggestion> ResolveTemplate(BlockElementTypeInfo element, CatalogueEntry entry)
    {
        var result = new List<BlockRoutePropertyMappingSuggestion>();
        if (entry.Mappings is null || entry.Mappings.Length == 0)
            return result;

        // Candidate source properties as (actual alias, normalised tokens). Prefer the richer
        // PropertyInfos (alias + name) and fall back to plain aliases.
        var candidates = (element.PropertyInfos.Count > 0
                ? element.PropertyInfos.Select(p => (Alias: p.Alias, Tokens: new[] { Normalise(p.Alias), Normalise(p.Name) }))
                : element.Properties.Select(a => (Alias: a, Tokens: new[] { Normalise(a) })))
            .ToList();

        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var template in entry.Mappings)
        {
            var match = candidates.FirstOrDefault(c =>
                !used.Contains(c.Alias)
                && template.ContentKeywords.Any(k => c.Tokens.Any(t => t.Length > 0 && t.Contains(k, StringComparison.Ordinal))));

            if (string.IsNullOrEmpty(match.Alias))
                continue;

            used.Add(match.Alias);
            result.Add(new BlockRoutePropertyMappingSuggestion
            {
                SchemaProperty = template.SchemaProperty,
                ContentProperty = match.Alias,
                WrapInType = template.WrapInType,
                WrapInProperty = template.WrapInProperty,
            });
        }

        return result;
    }

    /// <summary>
    /// True when the suggested source property is a rich-text/HTML-producing editor and the nested
    /// Schema.org target is a plain-text range — the exact case where the raw value would otherwise
    /// emit HTML markup. Reuses the shared advisory heuristics so the suggester and the reactive
    /// <c>MappingAdvisor</c> agree.
    /// </summary>
    private static bool ShouldStripHtml(PropertyMappingSuggestion s)
        => s.EditorAlias is { } editor
           && SchemeWeaverConstants.PropertyEditors.HtmlProducingEditorAliases.Contains(editor)
           && SchemaPrimitiveTypes.IsPlainTextRange(s.AcceptedTypes);

    /// <summary>
    /// For each property on the element type that is itself a Block List/Grid, suggests routes
    /// for the nested block's element types and returns them as property mappings keyed by the
    /// nested block property. Each carries child <c>Routes</c>, recursing the routing model one
    /// level deeper. Depth-capped to mirror the resolver's nesting limit.
    /// </summary>
    private List<BlockRoutePropertyMappingSuggestion> BuildNestedBlockMappings(
        BlockElementTypeInfo element, string parentSchemaType, int depth)
    {
        var result = new List<BlockRoutePropertyMappingSuggestion>();
        if (depth + 1 >= MaxNestDepth)
            return result;

        foreach (var propInfo in element.PropertyInfos.Where(p => p.NestedBlockElementTypes.Count > 0))
        {
            foreach (var suggestion in Suggest(propInfo.NestedBlockElementTypes, parentSchemaType, depth + 1)
                         .Where(s => s.Routes.Count > 0))
            {
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
    private void ApplyMainEntityDominance(List<RoutePlan> routed, string? contextSchemaType)
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
            routed[index] = plan with { Target = ResolveTarget(plan.Route.NestedSchemaType, HasPart, contextSchemaType) };
    }

    /// <summary>
    /// Resolves a range-safe target page property for a nested schema type, in two steps.
    /// (1) A type-specific target (currently <c>review</c>) only exists on some context
    /// types — Product declares it, plain WebPage-ish types don't — so when the context
    /// schema type is unknown or doesn't declare the property, the desired target demotes
    /// to <c>hasPart</c> and step 2 runs on that. (2) <c>hasPart</c> only accepts
    /// <c>CreativeWork</c> — a non-CreativeWork type (Person, Place, Service, Organization)
    /// routed there would be silently discarded by the strongly-typed Schema.NET model at
    /// generation time — so those fall back to <c>about</c> (range <c>Thing</c>), which
    /// accepts any entity. <c>mainEntity</c> and <c>about</c> are already
    /// <c>Thing</c>-range and pass through.
    /// </summary>
    private string ResolveTarget(string schemaType, string desiredTarget, string? contextSchemaType)
    {
        if (!UniversalTargets.Contains(desiredTarget, StringComparer.OrdinalIgnoreCase)
            && !TypeDeclaresProperty(contextSchemaType, desiredTarget))
        {
            desiredTarget = HasPart;
        }

        if (string.Equals(desiredTarget, HasPart, StringComparison.Ordinal)
            && !IsCreativeWork(schemaType))
            return About;

        return desiredTarget;
    }

    /// <summary>
    /// Whether <paramref name="schemaTypeName"/> declares a property named
    /// <paramref name="propertyName"/> (case-insensitive: the registry surfaces Schema.NET's
    /// PascalCase CLR names while catalogue targets use Schema.org camelCase). Unknown or
    /// absent context types answer false, so type-specific targets never survive without
    /// positive evidence.
    /// </summary>
    private bool TypeDeclaresProperty(string? schemaTypeName, string propertyName)
        => !string.IsNullOrWhiteSpace(schemaTypeName)
           && _registry.GetProperties(schemaTypeName!)
               .Any(p => string.Equals(p.Name, propertyName, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Row-scoped fit annotation: resolves the accepted object types of
    /// <paramref name="targetSchemaProperty"/> on the page schema type (the same
    /// AcceptedTypes surface <c>SchemaRangeValidator</c> range-checks against) and marks
    /// every top-level route — across ALL suggestion groups, since the caller may re-route
    /// fitting routes onto its row — with whether its nested type is assignable to that
    /// range. Nested routes (blocks inside blocks) stay unannotated: they feed properties
    /// of their parent block's type, not the caller's row. When the range cannot be
    /// resolved (no page schema type, unknown property) every FitsTarget stays null,
    /// keeping the wire shape identical to a request without the parameter.
    /// </summary>
    private void AnnotateTargetFit(List<BlockMappingSuggestion> suggestions, string? pageSchemaType, string targetSchemaProperty)
    {
        if (string.IsNullOrWhiteSpace(pageSchemaType))
            return;

        var targetProp = _registry.GetProperties(pageSchemaType!)
            .FirstOrDefault(p => string.Equals(p.Name, targetSchemaProperty, StringComparison.OrdinalIgnoreCase));
        if (targetProp is null)
            return;

        foreach (var route in suggestions.SelectMany(s => s.Routes))
            route.FitsTarget = _rangeChecker.IsInRange(route.NestedSchemaType, targetProp.AcceptedTypes);
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
