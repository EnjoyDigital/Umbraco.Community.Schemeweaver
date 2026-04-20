using Umbraco.Cms.Core.Models.PublishedContent;

namespace Umbraco.Community.SchemeWeaver.Graph;

/// <summary>
/// Resolves the site-level settings content node (Organization / WebSite source
/// of truth) per the convention configured in <c>SchemeWeaverOptions.SiteSettings</c>.
/// Returns null when the convention matches no content — pieces that depend on
/// the settings node should then return null from <see cref="IGraphPiece.ResolveId"/>
/// so the graph degrades gracefully to per-page output only.
/// </summary>
public interface ISiteSettingsResolver
{
    IPublishedContent? Resolve();
}

/// <summary>
/// No-op resolver used until Phase C ships the real implementation. Always
/// returns null; pieces that require the settings node simply skip
/// themselves, which keeps behaviour identical to the legacy per-mapping
/// output while the pieces model is being built out.
/// </summary>
internal sealed class NullSiteSettingsResolver : ISiteSettingsResolver
{
    public IPublishedContent? Resolve() => null;
}
