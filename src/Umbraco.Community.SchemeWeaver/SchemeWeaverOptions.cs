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

    /// <summary>
    /// Site search declaration for the WebSite graph node. When
    /// <see cref="SiteSearchOptions.UrlTemplate"/> is configured, the built-in
    /// WebSite piece emits a <c>potentialAction</c> <c>SearchAction</c> —
    /// the markup Google requires for the sitelinks search box. Off by default.
    /// </summary>
    public SiteSearchOptions SiteSearch { get; set; } = new();

    /// <summary>
    /// When <c>true</c>, the optional uSync addon exports a mapping to its uSync
    /// data folder every time it is saved or deleted in the backoffice, so the
    /// change is ready to commit to source control. Default is <c>false</c> —
    /// this is a SchemeWeaver-owned flag, deliberately independent of uSync's
    /// global <c>ExportOnSave</c> so enabling doc-type export-on-save never
    /// silently starts writing mapping files. Has no effect without the
    /// <c>Umbraco.Community.SchemeWeaver.uSync</c> package installed.
    /// </summary>
    public bool ExportMappingsToUSyncOnSave { get; set; }

    /// <summary>
    /// Controls how the optional uSync addon imports committed mapping <c>.config</c> files on
    /// boot. Default <see cref="BootImportMode.Off"/> preserves the historical behaviour
    /// (first-boot-only seeding: import only when the DB has zero mappings), so backoffice edits
    /// are never overwritten on restart. Opt into <see cref="BootImportMode.Seed"/> or
    /// <see cref="BootImportMode.Upsert"/> for config-as-code reproduction. Has no effect without
    /// the <c>Umbraco.Community.SchemeWeaver.uSync</c> package installed.
    /// </summary>
    public BootImportMode USyncBootImport { get; set; } = BootImportMode.Off;
}

/// <summary>
/// How committed uSync mapping <c>.config</c> files are imported on application start.
/// </summary>
public enum BootImportMode
{
    /// <summary>
    /// First-boot-only seeding (default, historical behaviour): import all configs only when the
    /// DB has zero mappings; once populated, do nothing on boot. Backoffice edits always survive
    /// restarts.
    /// </summary>
    Off,

    /// <summary>
    /// Create-missing on every boot: import a config only when no mapping with that alias exists
    /// in the DB. Never overwrites an existing mapping, so backoffice edits survive — but a
    /// committed config for a backoffice-deleted mapping is recreated on restart.
    /// </summary>
    Seed,

    /// <summary>
    /// Disk-wins on every boot: import/overwrite all configs from disk on each start (full
    /// config-as-code). Unexported backoffice edits are overwritten on restart.
    /// </summary>
    Upsert
}

/// <summary>
/// Tuning knobs for the heuristic auto-mapper (<see cref="Services.SchemaAutoMapper"/>),
/// bound to the <c>SchemeWeaver:AutoMapper</c> configuration section.
/// </summary>
public class SchemaAutoMapperOptions
{
    /// <summary>
    /// Suggestions scoring at least this confidence are auto-applied
    /// (<c>IsAutoMapped = true</c>) — the user sees them pre-ticked. Canonical
    /// matches (exact alias, synonym, the built-in url/name/date fallbacks) clear
    /// this bar. Default is <c>80</c>.
    /// </summary>
    public int AutoApplyConfidenceThreshold { get; set; } = 80;

    /// <summary>
    /// Suggestions scoring below this confidence are dropped entirely rather than
    /// returned, hiding the "always wrong" rows (partial-name matches, generic
    /// block fallbacks, no-match slots). Suggestions between this value and
    /// <see cref="AutoApplyConfidenceThreshold"/> are returned but not auto-applied,
    /// so the UI can offer them as "click to accept". Default is <c>60</c>.
    /// </summary>
    public int ShowConfidenceThreshold { get; set; } = 60;
}

/// <summary>
/// Configures the WebSite node's sitelinks-search-box <c>SearchAction</c>,
/// bound to the <c>SchemeWeaver:SiteSearch</c> configuration section. Unset by
/// default — SchemeWeaver cannot guess a site's search URL, so the consumer
/// declares it here and the WebSite piece does the rest.
/// </summary>
public class SiteSearchOptions
{
    /// <summary>
    /// Absolute URL template of the site's search results page, containing the
    /// literal query placeholder — e.g.
    /// <c>https://example.com/search?q={search_term_string}</c>. When null or
    /// empty (the default) no <c>potentialAction</c> is emitted. The placeholder
    /// name must match <see cref="QueryInputName"/>; a template without the
    /// placeholder is still emitted (Google tolerates it) but logs a warning.
    /// </summary>
    public string? UrlTemplate { get; set; }

    /// <summary>
    /// The variable name declared in the <c>query-input</c> property
    /// (<c>required name={value}</c>) and expected as the <c>{placeholder}</c>
    /// inside <see cref="UrlTemplate"/>. Default is Google's conventional
    /// <c>search_term_string</c> — you rarely need to change it.
    /// </summary>
    public string QueryInputName { get; set; } = "search_term_string";
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
