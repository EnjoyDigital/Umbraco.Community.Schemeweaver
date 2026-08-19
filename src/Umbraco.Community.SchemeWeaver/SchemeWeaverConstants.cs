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
        /// Editor aliases whose value is picked CONTENT — a reference to another node rather than a
        /// value of its own. The single source of truth for "this property resolves through the
        /// <see cref="Resolvers.PickedContentResolver"/> ladder" — referenced by the auto-mapper
        /// (recognising picker rows), by the complex-type sub-row config forwarding in
        /// <see cref="Services.JsonLdGenerator"/>, and by the range validator.
        /// </summary>
        public static readonly HashSet<string> ContentPickerAliases = new(StringComparer.OrdinalIgnoreCase)
        {
            "Umbraco.ContentPicker",
            "Umbraco.MultiNodeTreePicker"
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

        /// <summary>
        /// Editor aliases whose resolved value is prose text — the plain-text editors plus every
        /// <see cref="HtmlProducingEditorAliases"/> member. Referenced by
        /// <see cref="Resolvers.BlockContentResolver"/>'s basic text extraction (a block editor
        /// mapped in plain <c>property</c> mode emits the joined text of these block properties).
        /// Declared after <see cref="HtmlProducingEditorAliases"/> — field initialisers run in
        /// declaration order.
        /// </summary>
        public static readonly HashSet<string> TextProducingEditorAliases = new(
            HtmlProducingEditorAliases.Concat(new[]
            {
                "Umbraco.TextBox",
                "Umbraco.TextArea"
            }),
            StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Source-type discriminators for a <see cref="Models.Entities.PropertyMapping"/> — they say
    /// where a Schema.org property's value comes from. Persisted verbatim (author-controlled, always
    /// lowercase). These constants are the single source of truth for the string literals that were
    /// previously duplicated across the resolver, auto-mapper, validation, advisory, enrichment,
    /// serialization and AI layers.
    /// </summary>
    /// <remarks>
    /// The render path (<see cref="Services.JsonLdGenerator"/>, auto-mapper defaults) compares these
    /// with case-sensitive <c>==</c>/<c>switch</c>; the advisory/validation/enrichment paths use
    /// <c>OrdinalIgnoreCase</c>. Swapping a literal for the matching constant preserves the value, so
    /// each call site keeps its existing comparison policy unchanged.
    /// </remarks>
    public static class SourceTypes
    {
        /// <summary>A scalar or media value read from a property on the current node (the default).</summary>
        public const string Property = "property";

        /// <summary>A fixed literal value stored on the mapping (<c>StaticValue</c>).</summary>
        public const string Static = "static";

        /// <summary>A nested Schema.org entity resolved from a complex-type configuration.</summary>
        public const string ComplexType = "complexType";

        /// <summary>A nested Schema.org entity (or entities) resolved from a Block List / Block Grid.</summary>
        public const string BlockContent = "blockContent";

        /// <summary>A shared graph piece referenced by key (emits a range-typed <c>@id</c> shell).</summary>
        public const string Reference = "reference";

        /// <summary>A value read from the parent node.</summary>
        public const string Parent = "parent";

        /// <summary>A value read from an ancestor node.</summary>
        public const string Ancestor = "ancestor";

        /// <summary>A value read from a sibling node.</summary>
        public const string Sibling = "sibling";

        private static readonly HashSet<string> CrossNode =
            new(StringComparer.OrdinalIgnoreCase) { Parent, Ancestor, Sibling, Reference };

        private static readonly HashSet<string> NestedThing =
            new(StringComparer.OrdinalIgnoreCase) { ComplexType, BlockContent };

        /// <summary>
        /// Source types whose value is resolved from a node other than the current one
        /// (<see cref="Parent"/>, <see cref="Ancestor"/>, <see cref="Sibling"/>, <see cref="Reference"/>).
        /// A change to such a source can invalidate JSON-LD cached against a different node, so the
        /// cache invalidator fans out across relationships for these. Compared case-insensitively.
        /// </summary>
        public static bool IsCrossNode(string? sourceType) =>
            sourceType is not null && CrossNode.Contains(sourceType);

        /// <summary>
        /// Source types that resolve to a nested Schema.org <c>Thing</c> rather than a scalar
        /// (<see cref="ComplexType"/>, <see cref="BlockContent"/>). Compared case-insensitively.
        /// </summary>
        public static bool ResolvesToNestedThing(string? sourceType) =>
            sourceType is not null && NestedThing.Contains(sourceType);
    }
}
