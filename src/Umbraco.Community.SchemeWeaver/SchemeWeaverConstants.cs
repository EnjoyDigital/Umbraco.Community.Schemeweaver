namespace Umbraco.Community.SchemeWeaver;

/// <summary>
/// Constants used throughout the SchemeWeaver package.
/// </summary>
public static class SchemeWeaverConstants
{
    /// <summary>
    /// The package name.
    /// </summary>
    public const string PackageName = "SchemeWeaver";

    /// <summary>
    /// Database table names.
    /// </summary>
    public static class Tables
    {
        public const string SchemaMapping = "SchemeWeaverSchemaMapping";
        public const string PropertyMapping = "SchemeWeaverPropertyMapping";
    }

    /// <summary>
    /// Built-in IPublishedContent properties available for mapping alongside custom properties.
    /// Uses a double-underscore prefix convention to avoid collisions with Umbraco property aliases.
    /// </summary>
    public static class BuiltInProperties
    {
        public const string Prefix = "__";
        public const string Url = "__url";
        public const string Name = "__name";
        public const string CreateDate = "__createDate";
        public const string UpdateDate = "__updateDate";

        /// <summary>
        /// Synthetic editor alias used to route built-in properties through the resolver factory.
        /// </summary>
        public const string EditorAlias = "SchemeWeaver.BuiltIn";

        public static readonly IReadOnlyList<(string Alias, string DisplayName, string EditorAlias)> All =
        [
            (Url, "url", EditorAlias),
            (Name, "name", EditorAlias),
            (CreateDate, "createDate", EditorAlias),
            (UpdateDate, "updateDate", EditorAlias),
        ];

        public static bool IsBuiltIn(string? alias) =>
            alias is not null && alias.StartsWith(Prefix, StringComparison.Ordinal);
    }
}
