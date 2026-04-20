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
    /// When <c>true</c> (the default for v1.4+), JSON-LD is emitted as a single
    /// <c>@graph</c> composed from the registered <c>IGraphPiece</c>s (Yoast-style).
    /// Set to <c>false</c> to fall back to the pre-v1.4 one-JSON-LD-block-per-mapping
    /// behaviour if the new model causes problems. Kept as an escape hatch only —
    /// expected to be removed once the graph model has been proven in production.
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
