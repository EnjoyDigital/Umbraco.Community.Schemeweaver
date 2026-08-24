using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Schema.NET;
using Umbraco.Extensions;

namespace Umbraco.Community.SchemeWeaver.Graph.Pieces;

/// <summary>
/// Emits the WebSite node — one per site, referenced by WebPage pieces via
/// <c>isPartOf</c> and by Organization via <c>publisher</c> (in reverse). Named
/// after the root content node or the site settings node's <c>siteName</c> /
/// <c>name</c> property. Auto-wires <c>publisher</c> → Organization piece's
/// @id when present. When <c>SchemeWeaver:SiteSearch:UrlTemplate</c> is
/// configured, also emits a <c>potentialAction</c> <c>SearchAction</c>
/// (sitelinks search box).
///
/// @id convention: <c>{siteUrl}#website</c>. Skipped entirely when there's no
/// resolvable site URL (the piece has nothing meaningful to emit).
/// </summary>
public sealed class WebSitePiece : IGraphPiece
{
    private readonly ILogger<WebSitePiece> _logger;
    private readonly SchemeWeaverOptions _options;

    public WebSitePiece(ILogger<WebSitePiece> logger, IOptions<SchemeWeaverOptions> options)
    {
        _logger = logger;
        _options = options.Value;
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

        ApplySiteSearchAction(site);

        return site;
    }

    /// <summary>
    /// Emits <c>potentialAction</c> as a <c>SearchAction</c> when
    /// <c>SchemeWeaver:SiteSearch:UrlTemplate</c> is configured — the markup
    /// Google requires for the sitelinks search box. Off (no potentialAction)
    /// when unconfigured. A template missing its <c>{placeholder}</c> is still
    /// emitted (Google tolerates it) but logged as a warning.
    /// </summary>
    private void ApplySiteSearchAction(WebSite site)
    {
        var search = _options.SiteSearch;
        if (string.IsNullOrWhiteSpace(search?.UrlTemplate))
            return;

        var template = search.UrlTemplate;
        var inputName = string.IsNullOrWhiteSpace(search.QueryInputName)
            ? "search_term_string"
            : search.QueryInputName;

        var placeholder = $"{{{inputName}}}";
        if (!template.Contains(placeholder, StringComparison.Ordinal))
        {
            _logger.LogWarning(
                "SchemeWeaver:SiteSearch:UrlTemplate {UrlTemplate} does not contain the {Placeholder} placeholder — "
                + "emitting the SearchAction anyway, but the sitelinks search box needs the placeholder to inject the query",
                template, placeholder);
        }

        site.PotentialAction = new SearchAction
        {
            Target = new EntryPoint { UrlTemplate = template },
            QueryInput = $"required name={inputName}",
        };
    }

    private string ResolveSiteName(GraphPieceContext ctx)
    {
        // Precedence:
        //   1. siteSettings.siteName    (explicit Schema.org-shaped name)
        //   2. siteSettings.companyName (common Umbraco convention for branded sites)
        //   3. siteSettings.company     (the same convention, unsuffixed)
        //   4. siteSettings.name        (generic name property)
        //   5. siteSettings.brandName   (another common convention)
        //   6. Umbraco content node Name on the settings node (often editor-set)
        //   7. Host component of the site URL (last-resort, always defined)
        //
        // The node Name deliberately sits BELOW every convention property: settings
        // singletons are routinely called "Settings" or "Site Config", which is a
        // worse site name than anything an editor typed into a named field.
        try
        {
            if (ctx.SiteSettings is { } settings)
            {
                if (settings.Value<string>("siteName") is { Length: > 0 } siteName)
                    return siteName;
                if (settings.Value<string>("companyName") is { Length: > 0 } companyName)
                    return companyName;
                if (settings.Value<string>("company") is { Length: > 0 } company)
                    return company;
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
