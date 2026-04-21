namespace Umbraco.Community.SchemeWeaver;

/// <summary>
/// Configuration options for SchemeWeaver, bound to the "SchemeWeaver" section in appsettings.json.
/// </summary>
public class SchemeWeaverOptions
{
    /// <summary>
    /// Maximum recursion depth for nested property resolution (content pickers, block lists).
    /// Prevents infinite loops in circular content structures. Default is 3.
    /// </summary>
    public int MaxRecursionDepth { get; set; } = 3;

    /// <summary>
    /// Whether <see cref="Schema.NET.BreadcrumbList"/> JSON-LD is included in the Delivery API
    /// output under the <c>schemaOrg</c> field. Default is <c>true</c>.
    ///
    /// Set to <c>false</c> if your headless front-end has a URL structure that diverges from the
    /// Umbraco content tree and you want to generate the breadcrumb client-side from your own
    /// routing data instead. The server-rendered tag helper always emits breadcrumbs regardless
    /// of this setting.
    /// </summary>
    public bool EmitBreadcrumbsInDeliveryApi { get; set; } = true;

    /// <summary>
    /// Absolute cache duration for the per-content JSON-LD blocks served by the Delivery API
    /// endpoint (<c>GET /umbraco/delivery/api/v2/schemeweaver/json-ld</c>). Acts only as a
    /// safety-net — the real cache invalidation is event-driven, triggered by content publish,
    /// unpublish, move and delete notifications. Default is 30 minutes.
    /// </summary>
    public TimeSpan CacheDuration { get; set; } = TimeSpan.FromMinutes(30);

    /// <summary>
    /// Controls the shape of emitted JSON-LD. Both modes are supported long-term.
    /// <list type="bullet">
    ///   <item><description><c>true</c> (default): a single Yoast-style
    ///   <c>@graph</c> envelope composed from the registered
    ///   <c>IGraphPiece</c>s, with cross-referenced <c>@id</c>s. Best for
    ///   modern SEO pipelines — matches what Yoast, Rank Math et al. emit.</description></item>
    ///   <item><description><c>false</c>: one <c>&lt;script type="application/ld+json"&gt;</c>
    ///   block per source of data (inherited mappings, breadcrumb, main mapping,
    ///   block elements). Useful when consumers prefer per-entity diffing,
    ///   stricter CSP granularity, or just don't need cross-linking.</description></item>
    /// </list>
    /// This flag propagates through the tag helper, Delivery API, Examine
    /// index handler and backoffice preview so the backoffice shows whatever
    /// actually ships.
    /// </summary>
    public bool UseGraphModel { get; set; } = true;

    /// <summary>
    /// Site-wide settings content node resolution. Used by the built-in
    /// Organization / WebSite pieces to locate the singleton node whose
    /// SchemaMapping drives the site-level part of the graph.
    /// </summary>
    public SiteSettingsOptions SiteSettings { get; set; } = new();
}

/// <summary>
/// Configures how the site-settings singleton content node is located.
/// </summary>
public class SiteSettingsOptions
{
    /// <summary>
    /// Content type alias of the settings node (default <c>schemaSiteSettings</c>).
    /// The resolver picks the first published content of this type, or whichever
    /// is pointed to by <see cref="ContentKey"/> when that's set.
    /// </summary>
    public string ContentTypeAlias { get; set; } = "schemaSiteSettings";

    /// <summary>
    /// Optional explicit GUID of the settings node. Overrides the alias-based
    /// lookup when set — useful when the convention doesn't fit.
    /// </summary>
    public Guid? ContentKey { get; set; }
}
