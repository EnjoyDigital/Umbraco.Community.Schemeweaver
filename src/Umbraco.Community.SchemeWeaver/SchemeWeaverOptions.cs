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
    /// index under the <c>schemaOrg</c> field. Default is <c>true</c>.
    ///
    /// Set to <c>false</c> if your headless front-end has a URL structure that diverges from the
    /// Umbraco content tree and you want to generate the breadcrumb client-side from your own
    /// routing data instead. The server-rendered tag helper always emits breadcrumbs regardless
    /// of this setting.
    /// </summary>
    public bool EmitBreadcrumbsInDeliveryApi { get; set; } = true;
}
