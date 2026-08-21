namespace Umbraco.Community.SchemeWeaver.Services;

/// <summary>
/// Resolves the origin (scheme + host) all absolute URLs in emitted JSON-LD are
/// anchored to, honouring <see cref="SchemeWeaverOptions.PublicSiteUrl"/> for
/// headless/decoupled sites where the request reaches Umbraco on a different
/// host (a cms.* subdomain) than the one the public front-end serves pages on.
/// </summary>
public interface ISiteOriginResolver
{
    /// <summary>
    /// The origin the site's JSON-LD presents itself as: the configured
    /// <see cref="SchemeWeaverOptions.PublicSiteUrl"/> when set (even with no
    /// HTTP context — so site-level pieces still emit during Examine indexing),
    /// otherwise the current request's <c>scheme://host</c>, or null when
    /// neither is available.
    /// </summary>
    Uri? ResolveOrigin();

    /// <summary>
    /// Rewrites absolute URLs in serialised JSON-LD that sit on the CURRENT
    /// REQUEST's origin onto the configured public origin. URLs on any other
    /// host (CDNs, external <c>sameAs</c> links, editor-entered absolute URLs)
    /// are left untouched. A no-op when <see cref="SchemeWeaverOptions.PublicSiteUrl"/>
    /// is unset, when there is no request to rebase from, or when the request
    /// already arrives on the public origin.
    /// </summary>
    string RebaseToPublicOrigin(string json);
}
