namespace Umbraco.Community.SchemeWeaver.Graph;

/// <summary>
/// Supplies the site's identity (who publishes it) for the graph. Resolved once per graph
/// request by <see cref="GraphGenerator"/> and handed to every piece through
/// <see cref="GraphPieceContext.SiteIdentity"/>. Returning <c>null</c> means "nothing
/// configured": the Organization and WebSite pieces then fall back to the
/// <c>schemaSiteSettings</c> content-node convention exactly as before.
///
/// The SchemeWeaver SEO satellite registers an implementation backed by its backoffice
/// "Site identity" form; a site can also register its own (code-first identity for headless
/// builds, for example). Implementations are resolved from the request scope.
/// </summary>
public interface ISiteIdentityProvider
{
    SiteIdentity? Resolve();
}

/// <summary>
/// Default registration: no identity. Keeps core behaviour identical when no satellite or
/// site-specific provider is installed.
/// </summary>
public sealed class NullSiteIdentityProvider : ISiteIdentityProvider
{
    public static readonly NullSiteIdentityProvider Instance = new();

    public SiteIdentity? Resolve() => null;
}
