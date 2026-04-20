using Microsoft.Extensions.Logging;
using Schema.NET;
using Umbraco.Community.SchemeWeaver.Services;

namespace Umbraco.Community.SchemeWeaver.Graph.Pieces;

/// <summary>
/// Emits the current page's mapped Schema.NET Thing — the "main entity" of
/// the page. Delegates to the existing <see cref="IJsonLdGenerator"/> so the
/// mapping → Thing logic (property resolvers, transforms, @id override, etc.)
/// is reused exactly as today.
///
/// @id comes from the Thing itself (set by <c>JsonLdGenerator</c> per the
/// precedence: explicit Id property mapping → mapping.IdOverride → default
/// <c>{pageUrl}#{type}</c>). The piece exposes that same @id via
/// <see cref="ResolveId"/> so other pieces (e.g. WebPage) can reference it.
///
/// If the main entity is a <see cref="WebPage"/> subtype (AboutPage,
/// ContactPage, FAQPage, ItemPage, …) the piece auto-wires:
///   - <c>isPartOf</c> → WebSite piece
///   - <c>breadcrumb</c> → BreadcrumbList piece
///   - <c>primaryImageOfPage</c> → PrimaryImage piece
/// provided each target piece is emitted in the current graph and the
/// user hasn't already set the property via an explicit mapping.
/// </summary>
public sealed class MainEntityPiece : IGraphPiece
{
    private readonly IJsonLdGenerator _generator;
    private readonly ILogger<MainEntityPiece> _logger;

    // Cache the built Thing between ResolveId and Build to avoid rebuilding.
    // Piece is scoped per request so this field is safe.
    private Thing? _cached;
    private bool _built;

    public MainEntityPiece(IJsonLdGenerator generator, ILogger<MainEntityPiece> logger)
    {
        _generator = generator;
        _logger = logger;
    }

    public string Key => "main-entity";
    public int Order => 300;

    public string? ResolveId(GraphPieceContext ctx)
    {
        var thing = GetOrBuild(ctx);
        return thing?.Id?.ToString();
    }

    public Thing? Build(GraphPieceContext ctx)
    {
        var thing = GetOrBuild(ctx);
        if (thing is null)
            return null;

        AutoWirePageReferences(thing, ctx);
        return thing;
    }

    private Thing? GetOrBuild(GraphPieceContext ctx)
    {
        if (_built)
            return _cached;

        try
        {
            // Pass the graph context so `reference` source-typed property
            // mappings can resolve @ids of other pieces in the graph.
            _cached = _generator.GenerateJsonLd(ctx.Content, ctx.Culture, ctx);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "MainEntityPiece failed to build Thing for content {ContentId}",
                ctx.Content.Id);
            _cached = null;
        }

        _built = true;
        return _cached;
    }

    private static void AutoWirePageReferences(Thing thing, GraphPieceContext ctx)
    {
        // Only WebPage-subtype entities get the page-level cross-refs. Article,
        // Product, etc. don't carry isPartOf / breadcrumb / primaryImageOfPage
        // in any Yoast-compatible sense — for those, the user can wire refs
        // explicitly via the `reference` source type.
        if (thing is not WebPage page)
            return;

        if (page.IsPartOf.Count == 0 && ctx.IdFor("website") is { } websiteId
            && Uri.TryCreate(websiteId, UriKind.Absolute, out var websiteUri))
            page.IsPartOf = new WebSite { Id = websiteUri };

        if (page.Breadcrumb.Count == 0 && ctx.IdFor("breadcrumb") is { } breadcrumbId
            && Uri.TryCreate(breadcrumbId, UriKind.Absolute, out var breadcrumbUri))
            page.Breadcrumb = new BreadcrumbList { Id = breadcrumbUri };

        if (page.PrimaryImageOfPage.Count == 0 && ctx.IdFor("primary-image") is { } imageId
            && Uri.TryCreate(imageId, UriKind.Absolute, out var imageUri))
            page.PrimaryImageOfPage = new ImageObject { Id = imageUri };

        // AboutPage / ContactPage describe the Organization by convention — if
        // the user hasn't set `about` / `mainEntity` via an explicit mapping,
        // auto-wire them to the Organization piece. Scoped to these two
        // subtypes to avoid surprising users who map, say, an Article inside
        // a WebPage wrapper.
        if (thing is AboutPage or ContactPage)
        {
            if (ctx.IdFor("organization") is { } orgId
                && Uri.TryCreate(orgId, UriKind.Absolute, out var orgUri))
            {
                if (page.About.Count == 0)
                    page.About = new Organization { Id = orgUri };
                if (page.MainEntity.Count == 0)
                    page.MainEntity = new Organization { Id = orgUri };
            }
        }
    }
}
