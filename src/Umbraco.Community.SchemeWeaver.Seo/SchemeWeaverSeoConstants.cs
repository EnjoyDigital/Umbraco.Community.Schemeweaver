namespace Umbraco.Community.SchemeWeaver.Seo;

/// <summary>Constants for the SchemeWeaver SEO satellite.</summary>
public static class SchemeWeaverSeoConstants
{
    /// <summary>Migration plan / package name.</summary>
    public const string PackageName = "SchemeWeaver.Seo";

    /// <summary>
    /// Alias of the backoffice section this package adds. It is BOTH the alias of the frontend
    /// <c>section</c> manifest and the string stored on user groups' allowed sections, and the
    /// API policy compares it verbatim, so it must never drift from
    /// <c>App_Plugins/SchemeWeaverSeo/src/constants.ts</c>.
    /// </summary>
    public const string SectionAlias = "SchemeWeaver.Seo.Section";

    public static class Tables
    {
        public const string PageOverride = "SchemeWeaverSeoPageOverride";
    }

    /// <summary>Keys in Umbraco's key-value store (<c>umbracoKeyValue</c>).</summary>
    public static class KeyValueKeys
    {
        public const string SiteIdentity = "SchemeWeaver.Seo.SiteIdentity";
        public const string SectionGranted = "SchemeWeaver.Seo.SectionGranted.v1";
    }

    /// <summary>Template / slot vocabulary shared with the frontend (serialised verbatim).</summary>
    public static class Importance
    {
        public const string Required = "required";
        public const string Recommended = "recommended";
        public const string Optional = "optional";
    }

    public static class FeatureStatus
    {
        public const string RichResult = "rich-result";
        public const string Foundation = "foundation";
        public const string Retired = "retired";
    }

    /// <summary>How a slot is filled. Serialised lowercase; the frontend switches on these.</summary>
    public static class SlotValueKinds
    {
        public const string None = "none";
        public const string Property = "property";
        public const string Fixed = "fixed";
        public const string SiteIdentity = "siteIdentity";
        public const string Nested = "nested";
        public const string Blocks = "blocks";
        public const string Advanced = "advanced";
    }

    public static class ContentTypeStatus
    {
        public const string NotSetUp = "not-set-up";
        public const string NeedsAttention = "needs-attention";
        public const string SetUp = "set-up";
        public const string DeveloperManaged = "developer-managed";
    }
}
