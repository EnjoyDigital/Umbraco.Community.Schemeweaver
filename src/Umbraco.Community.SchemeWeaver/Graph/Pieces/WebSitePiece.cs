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

        var identity = ctx.SiteIdentity;
        var siteName = ResolveSiteName(ctx);
        var site = new WebSite
        {
            Url = ctx.SiteUrl,
            Name = siteName
        };

        // The identity's alternate name only applies when the identity also supplied the name.
        if (identity?.AlternateName is { Length: > 0 } alternateName
            && string.Equals(siteName, identity.Name, StringComparison.Ordinal))
            site.AlternateName = alternateName;

        // publisher → cross-ref by @id only. Pure references collapse to {"@id"} in the
        // serialised graph, so the Organization shell is correct whatever the publisher's type.
        if (ctx.IdFor("organization") is { } orgId
            && Uri.TryCreate(orgId, UriKind.Absolute, out var orgUri))
        {
            site.Publisher = new Organization { Id = orgUri };
        }

        if (!string.IsNullOrWhiteSpace(ctx.Culture))
            site.InLanguage = ctx.Culture;

        ApplySiteSearchAction(site, identity);

        return site;
    }

    /// <summary>
    /// Emits <c>potentialAction</c> as a <c>SearchAction</c> when
    /// <c>SchemeWeaver:SiteSearch:UrlTemplate</c> is configured — the markup
    /// Google requires for the sitelinks search box. Off (no potentialAction)
    /// when unconfigured. A template missing its <c>{placeholder}</c> is still
    /// emitted (Google tolerates it) but logged as a warning.
    /// </summary>
    private void ApplySiteSearchAction(WebSite site, SiteIdentity? identity)
    {
        // An identity-supplied search template (SEO satellite's Site identity form) takes
        // precedence over appsettings; either source may be absent.
        var search = _options.SiteSearch;
        var template = identity?.SearchUrlTemplate is { Length: > 0 } fromIdentity
            ? fromIdentity
            : search?.UrlTemplate;
        if (string.IsNullOrWhiteSpace(template))
            return;

        var configuredInputName = identity?.SearchUrlTemplate is { Length: > 0 }
            ? identity.SearchQueryInputName
            : search?.QueryInputName;
        var inputName = string.IsNullOrWhiteSpace(configuredInputName)
            ? "search_term_string"
            : configuredInputName;

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
        //   6. The backoffice/code-managed site identity's name (ISiteIdentityProvider)
        //   7. Umbraco content node Name on the settings node (often editor-set)
        //   8. Host component of the site URL (last-resort, always defined)
        //
        // A name an editor typed into a named settings field stays the site name even
        // once an identity exists (a person's blog keeps its title; the person is the
        // publisher). The node Name sits below both: settings singletons are routinely
        // called "Settings" or "Site Config".
        var settings = ctx.SiteSettings;
        try
        {
            if (settings is not null)
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
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "WebSitePiece: failed to read site name from settings node");
        }

        if (ctx.SiteIdentity?.Name is { Length: > 0 } identityName)
            return identityName;

        if (!string.IsNullOrWhiteSpace(settings?.Name))
            return settings.Name!;

        return ctx.SiteUrl!.Host;
    }
}
