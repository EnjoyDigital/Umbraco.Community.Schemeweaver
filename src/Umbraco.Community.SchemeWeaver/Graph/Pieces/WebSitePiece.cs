using Microsoft.Extensions.Logging;
using Schema.NET;
using Umbraco.Extensions;

namespace Umbraco.Community.SchemeWeaver.Graph.Pieces;

/// <summary>
/// Emits the WebSite node — one per site, referenced by WebPage pieces via
/// <c>isPartOf</c> and by Organization via <c>publisher</c> (in reverse). Named
/// after the root content node or the site settings node's <c>siteName</c> /
/// <c>name</c> property. Auto-wires <c>publisher</c> → Organization piece's
/// @id when present.
///
/// @id convention: <c>{siteUrl}#website</c>. Skipped entirely when there's no
/// resolvable site URL (the piece has nothing meaningful to emit).
/// </summary>
public sealed class WebSitePiece : IGraphPiece
{
    private readonly ILogger<WebSitePiece> _logger;

    public WebSitePiece(ILogger<WebSitePiece> logger)
    {
        _logger = logger;
    }

    public string Key => "website";
    public int Order => 200;
    public PieceScope Scope => PieceScope.Site;

    public string? ResolveId(GraphPieceContext ctx) =>
        ctx.SiteUrl is null ? null : $"{ctx.SiteUrl}#website";

    public Thing? Build(GraphPieceContext ctx)
    {
        if (ctx.SiteUrl is null)
            return null;

        var site = new WebSite
        {
            Url = ctx.SiteUrl,
            Name = ResolveSiteName(ctx)
        };

        // publisher → Organization cross-ref (by @id only).
        if (ctx.IdFor("organization") is { } orgId
            && Uri.TryCreate(orgId, UriKind.Absolute, out var orgUri))
        {
            site.Publisher = new Organization { Id = orgUri };
        }

        if (!string.IsNullOrWhiteSpace(ctx.Culture))
            site.InLanguage = ctx.Culture;

        return site;
    }

    private string ResolveSiteName(GraphPieceContext ctx)
    {
        // Precedence:
        //   1. siteSettings.siteName    (explicit Schema.org-shaped name)
        //   2. siteSettings.companyName (common Umbraco convention for branded sites)
        //   3. siteSettings.name        (generic name property)
        //   4. siteSettings.brandName   (another common convention)
        //   5. Umbraco content node Name on the settings node (often editor-set)
        //   6. Host component of the site URL (last-resort, always defined)
        try
        {
            if (ctx.SiteSettings is { } settings)
            {
                if (settings.Value<string>("siteName") is { Length: > 0 } siteName)
                    return siteName;
                if (settings.Value<string>("companyName") is { Length: > 0 } companyName)
                    return companyName;
                if (settings.Value<string>("name") is { Length: > 0 } nameProp)
                    return nameProp;
                if (settings.Value<string>("brandName") is { Length: > 0 } brandName)
                    return brandName;
                if (!string.IsNullOrWhiteSpace(settings.Name))
                    return settings.Name!;
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "WebSitePiece: failed to read site name from settings node");
        }

        return ctx.SiteUrl!.Host;
    }
}
