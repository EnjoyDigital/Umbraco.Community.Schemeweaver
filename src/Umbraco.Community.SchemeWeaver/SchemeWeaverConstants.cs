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

    /// <summary>
    /// Umbraco property editor aliases used for block-based editors.
    /// </summary>
    public static class PropertyEditors
    {
        public static readonly HashSet<string> BlockEditorAliases = new(StringComparer.OrdinalIgnoreCase)
        {
            "Umbraco.BlockList",
            "Umbraco.BlockGrid"
        };

        /// <summary>
        /// Editor aliases for media pickers. The single source of truth for "this property
        /// resolves to media" — referenced by <see cref="Resolvers.MediaPickerResolver"/>
        /// (what it resolves), the auto-mapper (media-shaped properties stay plain
        /// <c>property</c> mappings so the resolver can emit a full ImageObject) and the
        /// range validator (flagging media bound onto string-only nested sub-properties).
        /// </summary>
        public static readonly HashSet<string> MediaPickerAliases = new(StringComparer.OrdinalIgnoreCase)
        {
            "Umbraco.MediaPicker3",
            "Umbraco.MediaPicker"
        };

        /// <summary>
        /// Editor aliases whose resolved value is HTML/markup. The single source of truth for
        /// "this source produces HTML" — referenced by <see cref="Resolvers.RichTextResolver"/>
        /// (what it resolves) and by the mapping advisor (which suggests <c>stripHtml</c> when one
        /// of these feeds a plain-text Schema.org property).
        /// </summary>
        public static readonly HashSet<string> HtmlProducingEditorAliases = new(StringComparer.OrdinalIgnoreCase)
        {
            "Umbraco.RichText",
            "Umbraco.TinyMCE",
            "Umbraco.MarkdownEditor"
        };
    }
}
