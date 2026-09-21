using NSubstitute;
using Umbraco.Cms.Core.Models;
using Umbraco.Community.SchemeWeaver.Models.Api;
using Umbraco.Community.SchemeWeaver.Services;
using Umbraco.Community.SchemeWeaver.TypeSafe.Services;

namespace Umbraco.Community.SchemeWeaver.Tests.Unit.TypeSafe.TestSupport;

/// <summary>
/// Small builders for the NSubstitute content types the TypeSafe tests map, in the same shape
/// the core auto-mapper tests use (<c>CompositionPropertyTypes</c> populated, because that is
/// what the mappers read), plus <see cref="BlockElementTypeInfo"/> rows for block properties.
/// </summary>
internal static class TypeSafeTestContentTypes
{
    public const string TextBox = "Umbraco.TextBox";
    public const string TextArea = "Umbraco.TextArea";
    public const string BlockList = "Umbraco.BlockList";
    public const string MediaPicker3 = "Umbraco.MediaPicker3";

    public static IContentType ContentType(string alias, string name, params (string Alias, string Editor)[] properties)
    {
        var contentType = Substitute.For<IContentType>();
        contentType.Alias.Returns(alias);
        contentType.Name.Returns(name);
        contentType.Description.Returns((string?)null);
        var propertyTypes = properties.Select(p => Property(p.Alias, p.Editor)).ToList();
        contentType.PropertyTypes.Returns(propertyTypes);
        contentType.CompositionPropertyTypes.Returns(propertyTypes);
        return contentType;
    }

    public static IPropertyType Property(string alias, string editor, string? name = null)
    {
        var pt = Substitute.For<IPropertyType>();
        pt.Alias.Returns(alias);
        pt.Name.Returns(name ?? alias);
        pt.PropertyEditorAlias.Returns(editor);
        pt.Description.Returns((string?)null);
        return pt;
    }

    public static BlockElementTypeInfo Block(string alias, string name, params (string Alias, string Editor)[] fields)
        => new()
        {
            Alias = alias,
            Name = name,
            Properties = fields.Select(f => f.Alias).ToList(),
            PropertyInfos = fields.Select(f => new BlockElementPropertyInfo { Alias = f.Alias, Name = f.Alias, EditorAlias = f.Editor }).ToList(),
        };
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
