using System.Security.Cryptography;
using System.Text;
using NSubstitute;
using Umbraco.Cms.Core.Models;
using Umbraco.Cms.Core.Services;
using Umbraco.Community.SchemeWeaver.Models.Api;
using Umbraco.Community.SchemeWeaver.Services;
using Umbraco.Community.SchemeWeaver.TypeSafe.Services;

namespace Umbraco.Community.SchemeWeaver.Tests.Unit.TypeSafe.TestSupport;

/// <summary>
/// Small builders for the NSubstitute content types the TypeSafe tests map, in the same shape
/// the core auto-mapper tests use (<c>CompositionPropertyTypes</c> populated, because that is
/// what the mappers read), plus <see cref="BlockElementTypeInfo"/> rows for block properties.
/// </summary>
/// <remarks>
/// v2 needs structure as well as properties: the neighbourhood discovery reads
/// <c>Key</c>, <c>Id</c>, <c>IsElement</c>, <c>AllowedAsRoot</c> and <c>AllowedContentTypes</c>,
/// and the route planner reads a block field's <c>NestedBlockElementTypes</c>. Every type built
/// here gets a deterministic key and id derived from its alias, so an allowed-children entry
/// built from an alias alone still resolves by key, exactly as a uSync-imported declaration does.
/// </remarks>
internal static class TypeSafeTestContentTypes
{
    public const string TextBox = "Umbraco.TextBox";
    public const string TextArea = "Umbraco.TextArea";
    public const string RichText = "Umbraco.RichText";
    public const string BlockList = "Umbraco.BlockList";
    public const string BlockGrid = "Umbraco.BlockGrid";
    public const string MediaPicker3 = "Umbraco.MediaPicker3";
    public const string ContentPicker = "Umbraco.ContentPicker";
    public const string DateTimeEditor = "Umbraco.DateTime";
    public const string Label = "Umbraco.Label";

    public static IContentType ContentType(string alias, string name, params (string Alias, string Editor)[] properties)
        => Type(alias, name).Properties(properties).Build();

    /// <summary>A fluent builder for the structural facts v2 reads.</summary>
    public static ContentTypeBuilder Type(string alias, string name) => new(alias, name);

    public static IPropertyType Property(string alias, string editor, string? name = null)
    {
        var pt = Substitute.For<IPropertyType>();
        pt.Alias.Returns(alias);
        pt.Name.Returns(name ?? alias);
        pt.PropertyEditorAlias.Returns(editor);
        pt.Description.Returns((string?)null);
        // One data type per alias, so a value-schema stub can answer per property.
        pt.DataTypeKey.Returns(KeyFor("dataType:" + alias));
        pt.Variations.Returns(ContentVariation.Nothing);
        return pt;
    }

    public static BlockElementTypeInfo Block(string alias, string name, params (string Alias, string Editor)[] fields)
        => BlockOf(alias, name, fields.Select(f => Field(f.Alias, f.Editor)).ToArray());

    /// <summary>A block element type from full field infos (use <see cref="NestedField"/> for a block inside a block).</summary>
    public static BlockElementTypeInfo BlockOf(string alias, string name, params BlockElementPropertyInfo[] fields)
        => new()
        {
            Alias = alias,
            Name = name,
            Properties = fields.Select(f => f.Alias).ToList(),
            PropertyInfos = fields.ToList(),
        };

    public static BlockElementPropertyInfo Field(string alias, string editor, string? valueSchema = null)
        => new() { Alias = alias, Name = alias, EditorAlias = editor, ValueSchema = valueSchema };

    /// <summary>A block field that is itself a Block List/Grid, with the element types the core would have discovered inside it.</summary>
    public static BlockElementPropertyInfo NestedField(string alias, string editor, params BlockElementTypeInfo[] nested)
        => new() { Alias = alias, Name = alias, EditorAlias = editor, NestedBlockElementTypes = nested.ToList() };

    /// <summary>
    /// Registers every type on <paramref name="service"/> for <c>Get(alias)</c> and <c>GetAll()</c>,
    /// which is what structural neighbourhood discovery reads.
    /// </summary>
    public static void Structure(IContentTypeService service, params IContentType[] types)
    {
        foreach (var type in types)
            service.Get(type.Alias).Returns(type);

        service.GetAll().Returns(types);
    }

    /// <summary>A stable key for an alias, so keys and aliases in allowed-children entries always agree.</summary>
    public static Guid KeyFor(string alias)
        => new(MD5.HashData(Encoding.UTF8.GetBytes(alias)));

    /// <summary>A stable positive id for an alias.</summary>
    public static int IdFor(string alias)
        => 1000 + (int)(BitConverter.ToUInt32(MD5.HashData(Encoding.UTF8.GetBytes(alias)), 0) % 100_000);

    public sealed class ContentTypeBuilder
    {
        private readonly string _alias;
        private readonly string _name;
        private readonly List<(string Alias, string Editor, string? Name)> _properties = [];
        private readonly List<string> _allowed = [];
        private Guid? _key;
        private int? _id;
        private bool _isElement;
        private bool _allowedAsRoot;

        public ContentTypeBuilder(string alias, string name)
        {
            _alias = alias;
            _name = name;
        }

        public ContentTypeBuilder Property(string alias, string editor, string? name = null)
        {
            _properties.Add((alias, editor, name));
            return this;
        }

        public ContentTypeBuilder Properties(params (string Alias, string Editor)[] properties)
        {
            foreach (var (alias, editor) in properties)
                _properties.Add((alias, editor, null));
            return this;
        }

        public ContentTypeBuilder Key(Guid key)
        {
            _key = key;
            return this;
        }

        public ContentTypeBuilder Id(int id)
        {
            _id = id;
            return this;
        }

        public ContentTypeBuilder IsElement(bool isElement = true)
        {
            _isElement = isElement;
            return this;
        }

        public ContentTypeBuilder AllowedAsRoot(bool allowedAsRoot = true)
        {
            _allowedAsRoot = allowedAsRoot;
            return this;
        }

        /// <summary>Allowed children by alias; each entry carries the child's deterministic key too.</summary>
        public ContentTypeBuilder Allows(params string[] childAliases)
        {
            _allowed.AddRange(childAliases);
            return this;
        }

        public IContentType Build()
        {
            var contentType = Substitute.For<IContentType>();
            contentType.Alias.Returns(_alias);
            contentType.Name.Returns(_name);
            contentType.Description.Returns((string?)null);
            contentType.Key.Returns(_key ?? KeyFor(_alias));
            contentType.Id.Returns(_id ?? IdFor(_alias));
            contentType.IsElement.Returns(_isElement);
            contentType.AllowedAsRoot.Returns(_allowedAsRoot);

            var propertyTypes = _properties.Select(p => TypeSafeTestContentTypes.Property(p.Alias, p.Editor, p.Name)).ToList();
            contentType.PropertyTypes.Returns(propertyTypes);
            contentType.CompositionPropertyTypes.Returns(propertyTypes);

            var allowed = _allowed.Select((alias, i) => new ContentTypeSort(KeyFor(alias), i, alias)).ToList();
            contentType.AllowedContentTypes.Returns(allowed);
            return contentType;
        }
    }
}

/// <summary>
/// One real <see cref="SchemaTypeRegistry"/> (a Schema.NET assembly scan) and one real
/// <see cref="SchemaTypeGraph"/> over it, shared across the TypeSafe unit tests exactly as the
/// app shares them (both are singletons). Keeps the suite fast without weakening anything:
/// the registry is immutable once built.
/// </summary>
internal static class SharedSchemaRegistry
{
    public static readonly SchemaTypeRegistry Registry = new();

    public static readonly SchemaTypeGraph Graph = new(Registry);
}
