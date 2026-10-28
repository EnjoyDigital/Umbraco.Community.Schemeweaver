namespace Umbraco.Community.SchemeWeaver.Seo;

/// <summary>Options for the SchemeWeaver SEO satellite, bound to <c>SchemeWeaver:Seo</c>.</summary>
public class SchemeWeaverSeoOptions
{
    public const string SectionName = "SchemeWeaver:Seo";

    /// <summary>
    /// When <c>true</c> (default) the SEO package's "Schema" tab replaces the core's developer
    /// "JSON-LD" tab on content items. Set to <c>false</c> to keep both tabs.
    /// </summary>
    public bool HideDeveloperJsonLdTab { get; set; } = true;

    /// <summary>
    /// When <c>true</c> (default) the "SEO Schema" section is added to the Administrators user
    /// group once, on the first application start after install, so somebody can see it without
    /// a database edit. Other user groups are granted the section in Users &gt; User Groups.
    /// </summary>
    public bool GrantSectionToAdministrators { get; set; } = true;

    /// <summary>
    /// How long a site health report (one preview per mapped content type) is cached before
    /// the Overview re-runs it. Default is 5 minutes; <c>TimeSpan.Zero</c> disables caching.
    /// </summary>
    public TimeSpan HealthCheckCacheDuration { get; set; } = TimeSpan.FromMinutes(5);
}
