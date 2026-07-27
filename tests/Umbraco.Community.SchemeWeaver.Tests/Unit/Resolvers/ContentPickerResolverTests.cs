using FluentAssertions;
using Microsoft.AspNetCore.Http;
using NSubstitute;
using Xunit;
using Umbraco.Cms.Core.Models.PublishedContent;
using Umbraco.Community.SchemeWeaver.Models.Entities;
using Umbraco.Community.SchemeWeaver.Persistence;
using Umbraco.Community.SchemeWeaver.Services;
using Umbraco.Community.SchemeWeaver.Services.Resolvers;

namespace Umbraco.Community.SchemeWeaver.Tests.Unit.Resolvers;

public class ContentPickerResolverTests
{
    private readonly ContentPickerResolver _sut = new();
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly ISchemaTypeRegistry _registry = new SchemaTypeRegistry();
    private readonly ISchemaMappingRepository _repository = Substitute.For<ISchemaMappingRepository>();

    public ContentPickerResolverTests()
    {
        _httpContextAccessor = Substitute.For<IHttpContextAccessor>();
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Scheme = "https";
        httpContext.Request.Host = new HostString("example.com");
        _httpContextAccessor.HttpContext.Returns(httpContext);
    }

    [Fact]
    public void SupportedEditorAliases_ContainsContentPicker()
    {
        _sut.SupportedEditorAliases.Should().Contain("Umbraco.ContentPicker");
    }

    [Fact]
    public void Priority_Returns10()
    {
        _sut.Priority.Should().Be(10);
    }

    [Fact]
    public void Resolve_NullProperty_ReturnsNull()
    {
        var context = CreateContext(null);

        var result = _sut.Resolve(context);

        result.Should().BeNull();
    }

    [Fact]
    public void Resolve_NullPropertyValue_ReturnsNull()
    {
        var property = Substitute.For<IPublishedProperty>();
        property.GetValue(Arg.Any<string?>(), Arg.Any<string?>()).Returns(null);

        var context = CreateContext(property);

        var result = _sut.Resolve(context);

        result.Should().BeNull();
    }

    [Fact]
    public void Resolve_NonPublishedContentValue_ReturnsNull()
    {
        var property = Substitute.For<IPublishedProperty>();
        property.GetValue(Arg.Any<string?>(), Arg.Any<string?>()).Returns("not a content item");

        var context = CreateContext(property);

        var result = _sut.Resolve(context);

        result.Should().BeNull();
    }

    [Fact]
    public void Resolve_PublishedContent_ReturnsName()
    {
        var pickedContent = Substitute.For<IPublishedContent>();
        pickedContent.Name.Returns("About Us");

        var property = Substitute.For<IPublishedProperty>();
        property.GetValue(Arg.Any<string?>(), Arg.Any<string?>()).Returns(pickedContent);

        var context = CreateContext(property);

        var result = _sut.Resolve(context);

        result.Should().Be("About Us");
    }

    [Fact]
    public void Resolve_WithNestedSchemaType_AndMappingExists_ReturnsNestedThing()
    {
        var pickedContentType = Substitute.For<IPublishedContentType>();
        pickedContentType.Alias.Returns("person");

        var nameProperty = Substitute.For<IPublishedProperty>();
        nameProperty.GetValue(Arg.Any<string?>(), Arg.Any<string?>()).Returns("John Doe");

        var pickedContent = Substitute.For<IPublishedContent>();
        pickedContent.ContentType.Returns(pickedContentType);
        pickedContent.GetProperty("fullName").Returns(nameProperty);
        pickedContent.Name.Returns("John Doe");

        var property = Substitute.For<IPublishedProperty>();
        property.GetValue(Arg.Any<string?>(), Arg.Any<string?>()).Returns(pickedContent);

        var nestedMapping = new SchemaMapping
        {
            Id = 2,
            ContentTypeAlias = "person",
            SchemaTypeName = "Person",
            IsEnabled = true
        };
        _repository.GetByContentTypeAlias("person").Returns(nestedMapping);
        _repository.GetPropertyMappings(2).Returns(new List<PropertyMapping>
        {
            new() { SchemaPropertyName = "Name", ContentTypePropertyAlias = "fullName" }
        });

        var mapping = new PropertyMapping
        {
            SchemaPropertyName = "Author",
            NestedSchemaTypeName = "Person"
        };

        var context = new PropertyResolverContext
        {
            Content = Substitute.For<IPublishedContent>(),
            Mapping = mapping,
            PropertyAlias = "author",
            SchemaTypeRegistry = _registry,
            MappingRepository = _repository,
            HttpContextAccessor = _httpContextAccessor,
            Property = property,
            RecursionDepth = 0,
            MaxRecursionDepth = 3
        };

        var result = _sut.Resolve(context);

        result.Should().BeOfType<Schema.NET.Person>();
    }

    [Fact]
    public void Resolve_WithNestedSchemaType_AtMaxRecursionDepth_ReturnsName()
    {
        var pickedContent = Substitute.For<IPublishedContent>();
        pickedContent.Name.Returns("Some Person");

        var property = Substitute.For<IPublishedProperty>();
        property.GetValue(Arg.Any<string?>(), Arg.Any<string?>()).Returns(pickedContent);

        var mapping = new PropertyMapping
        {
            SchemaPropertyName = "Author",
            NestedSchemaTypeName = "Person"
        };

        var context = new PropertyResolverContext
        {
            Content = Substitute.For<IPublishedContent>(),
            Mapping = mapping,
            PropertyAlias = "author",
            SchemaTypeRegistry = _registry,
            MappingRepository = _repository,
            HttpContextAccessor = _httpContextAccessor,
            Property = property,
            RecursionDepth = 3,
            MaxRecursionDepth = 3
        };

        var result = _sut.Resolve(context);

        // Should fall back to Name since recursion depth is at max
        result.Should().Be("Some Person");
    }

    [Fact]
    public void Resolve_WithNestedSchemaType_NoNestedMapping_ReturnsName()
    {
        var pickedContentType = Substitute.For<IPublishedContentType>();
        pickedContentType.Alias.Returns("person");

        var pickedContent = Substitute.For<IPublishedContent>();
        pickedContent.ContentType.Returns(pickedContentType);
        pickedContent.Name.Returns("John");

        var property = Substitute.For<IPublishedProperty>();
        property.GetValue(Arg.Any<string?>(), Arg.Any<string?>()).Returns(pickedContent);

        _repository.GetByContentTypeAlias("person").Returns((SchemaMapping?)null);

        var mapping = new PropertyMapping
        {
            SchemaPropertyName = "Author",
            NestedSchemaTypeName = "Person"
        };

        var context = new PropertyResolverContext
        {
            Content = Substitute.For<IPublishedContent>(),
            Mapping = mapping,
            PropertyAlias = "author",
            SchemaTypeRegistry = _registry,
            MappingRepository = _repository,
            HttpContextAccessor = _httpContextAccessor,
            Property = property,
            RecursionDepth = 0,
            MaxRecursionDepth = 3
        };

        var result = _sut.Resolve(context);

        // Falls back to Name when no nested mapping exists
        result.Should().Be("John");
    }

    [Fact]
    public void Resolve_SelfReferencingContent_WithNestedMapping_ResolvesNestedThing()
    {
        // Content picker pointing to the same content node (self-reference).
        // At depth 0 the resolver should still produce a nested Thing.
        var selfContentType = Substitute.For<IPublishedContentType>();
        selfContentType.Alias.Returns("article");

        var nameProperty = Substitute.For<IPublishedProperty>();
        nameProperty.GetValue(Arg.Any<string?>(), Arg.Any<string?>()).Returns("Self Article");

        var selfContent = Substitute.For<IPublishedContent>();
        selfContent.ContentType.Returns(selfContentType);
        selfContent.GetProperty("headline").Returns(nameProperty);
        selfContent.Name.Returns("Self Article");

        // The property on the content returns itself
        var property = Substitute.For<IPublishedProperty>();
        property.GetValue(Arg.Any<string?>(), Arg.Any<string?>()).Returns(selfContent);

        var nestedMapping = new SchemaMapping
        {
            Id = 5,
            ContentTypeAlias = "article",
            SchemaTypeName = "Article",
            IsEnabled = true
        };
        _repository.GetByContentTypeAlias("article").Returns(nestedMapping);
        _repository.GetPropertyMappings(5).Returns(new List<PropertyMapping>
        {
            new() { SchemaPropertyName = "Headline", ContentTypePropertyAlias = "headline" }
        });

        var mapping = new PropertyMapping
        {
            SchemaPropertyName = "RelatedArticle",
            NestedSchemaTypeName = "Article"
        };

        var context = new PropertyResolverContext
        {
            Content = selfContent, // same content node
            Mapping = mapping,
            PropertyAlias = "relatedArticle",
            SchemaTypeRegistry = _registry,
            MappingRepository = _repository,
            HttpContextAccessor = _httpContextAccessor,
            Property = property,
            RecursionDepth = 0,
            MaxRecursionDepth = 3
        };

        var result = _sut.Resolve(context);

        result.Should().BeOfType<Schema.NET.Article>();
    }

    [Fact]
    public void Resolve_RecursionDepthExceedsMax_ReturnsName()
    {
        // When recursion depth exceeds max (not just equals), should still fall back to name
        var pickedContent = Substitute.For<IPublishedContent>();
        pickedContent.Name.Returns("Deep Content");

        var property = Substitute.For<IPublishedProperty>();
        property.GetValue(Arg.Any<string?>(), Arg.Any<string?>()).Returns(pickedContent);

        var mapping = new PropertyMapping
        {
            SchemaPropertyName = "Author",
            NestedSchemaTypeName = "Person"
        };

        var context = new PropertyResolverContext
        {
            Content = Substitute.For<IPublishedContent>(),
            Mapping = mapping,
            PropertyAlias = "author",
            SchemaTypeRegistry = _registry,
            MappingRepository = _repository,
            HttpContextAccessor = _httpContextAccessor,
            Property = property,
            RecursionDepth = 5,
            MaxRecursionDepth = 3
        };

        var result = _sut.Resolve(context);

        result.Should().Be("Deep Content");
    }

    // --- Drill-down (pickedPropertyAlias in ResolverConfig) ---

    [Fact]
    public void Resolve_DrillDownConfig_ResolvesPickedNodesProperty()
    {
        var pickedContent = CreatePickedContent("Jane Doe", "jobTitle", "Principal Developer",
            editorAlias: "Umbraco.TextBox");
        var context = CreateDrillContext(pickedContent, """{"pickedPropertyAlias":"jobTitle"}""");

        var result = _sut.Resolve(context);

        result.Should().Be("Principal Developer");
    }

    [Fact]
    public void Resolve_DrillDownConfig_WinsOverNestedSchemaType()
    {
        var pickedContent = CreatePickedContent("Jane Doe", "jobTitle", "Principal Developer",
            editorAlias: "Umbraco.TextBox");
        var context = CreateDrillContext(pickedContent, """{"pickedPropertyAlias":"jobTitle"}""",
            nestedSchemaTypeName: "Person");

        var result = _sut.Resolve(context);

        // Not a nested Person, not the name — the drilled scalar.
        result.Should().Be("Principal Developer");
    }

    [Fact]
    public void Resolve_DrillDownConfig_BuiltInName_ResolvesViaBuiltInResolver()
    {
        var pickedContent = CreatePickedContent("Jane Doe", "unused", "x", editorAlias: "Umbraco.TextBox");
        var context = CreateDrillContext(pickedContent, """{"pickedPropertyAlias":"__name"}""");

        var result = _sut.Resolve(context);

        result.Should().Be("Jane Doe");
    }

    [Fact]
    public void Resolve_DrillDownConfig_MissingProperty_ReturnsNull_NotName()
    {
        var pickedContent = Substitute.For<IPublishedContent>();
        pickedContent.Name.Returns("Jane Doe");
        pickedContent.GetProperty("nonExistent").Returns((IPublishedProperty?)null);

        var context = CreateDrillContext(pickedContent, """{"pickedPropertyAlias":"nonExistent"}""");

        var result = _sut.Resolve(context);

        result.Should().BeNull();
    }

    [Fact]
    public void Resolve_DrillDownConfig_AtMaxDepth_ReturnsNull_NotName()
    {
        var pickedContent = CreatePickedContent("Jane Doe", "jobTitle", "Principal Developer",
            editorAlias: "Umbraco.TextBox");
        var context = CreateDrillContext(pickedContent, """{"pickedPropertyAlias":"jobTitle"}""",
            recursionDepth: 3);

        var result = _sut.Resolve(context);

        result.Should().BeNull();
    }

    [Fact]
    public void Resolve_DrillDownConfig_CaseInsensitiveKeys()
    {
        var pickedContent = CreatePickedContent("Jane Doe", "jobTitle", "Principal Developer",
            editorAlias: "Umbraco.TextBox");
        var context = CreateDrillContext(pickedContent, """{"PickedPropertyAlias":"jobTitle"}""");

        var result = _sut.Resolve(context);

        result.Should().Be("Principal Developer");
    }

    [Fact]
    public void Resolve_MalformedResolverConfig_FallsBackToName()
    {
        var pickedContent = Substitute.For<IPublishedContent>();
        pickedContent.Name.Returns("Jane Doe");

        var context = CreateDrillContext(pickedContent, "{not json");

        var result = _sut.Resolve(context);

        result.Should().Be("Jane Doe");
    }

    [Fact]
    public void Resolve_DrillIntoContentPickerProperty_DoesNotLeakOuterMapping()
    {
        // The drilled property is itself a ContentPicker. The outer row's
        // NestedSchemaTypeName/ResolverConfig must NOT leak into the child
        // resolution — the inner picker should fall to its picked node's name,
        // not re-drill or build a nested Thing from the outer config.
        var innerPicked = Substitute.For<IPublishedContent>();
        innerPicked.Name.Returns("Inner Node");

        var innerPickerProperty = Substitute.For<IPublishedProperty>();
        innerPickerProperty.GetValue(Arg.Any<string?>(), Arg.Any<string?>()).Returns(innerPicked);
        var innerPropertyType = Substitute.For<IPublishedPropertyType>();
        innerPropertyType.EditorAlias.Returns("Umbraco.ContentPicker");
        innerPickerProperty.PropertyType.Returns(innerPropertyType);

        var pickedContent = Substitute.For<IPublishedContent>();
        pickedContent.Name.Returns("Outer Node");
        pickedContent.GetProperty("related").Returns(innerPickerProperty);

        var context = CreateDrillContext(pickedContent,
            """{"pickedPropertyAlias":"related"}""", nestedSchemaTypeName: "Person");

        var result = _sut.Resolve(context);

        result.Should().Be("Inner Node");
    }

    private static IPublishedContent CreatePickedContent(
        string name, string propertyAlias, object propertyValue, string editorAlias)
    {
        var propertyType = Substitute.For<IPublishedPropertyType>();
        propertyType.EditorAlias.Returns(editorAlias);

        var property = Substitute.For<IPublishedProperty>();
        property.GetValue(Arg.Any<string?>(), Arg.Any<string?>()).Returns(propertyValue);
        property.PropertyType.Returns(propertyType);

        var content = Substitute.For<IPublishedContent>();
        content.Name.Returns(name);
        content.GetProperty(propertyAlias).Returns(property);

        return content;
    }

    private PropertyResolverContext CreateDrillContext(
        IPublishedContent pickedContent,
        string? resolverConfig,
        string? nestedSchemaTypeName = null,
        int recursionDepth = 0)
    {
        var pickerProperty = Substitute.For<IPublishedProperty>();
        pickerProperty.GetValue(Arg.Any<string?>(), Arg.Any<string?>()).Returns(pickedContent);

        return new PropertyResolverContext
        {
            Content = Substitute.For<IPublishedContent>(),
            Mapping = new PropertyMapping
            {
                SchemaPropertyName = "Author",
                ResolverConfig = resolverConfig,
                NestedSchemaTypeName = nestedSchemaTypeName
            },
            PropertyAlias = "authorNode",
            SchemaTypeRegistry = _registry,
            MappingRepository = _repository,
            HttpContextAccessor = _httpContextAccessor,
            ResolverFactory = CreateResolverFactory(),
            Property = pickerProperty,
            RecursionDepth = recursionDepth,
            MaxRecursionDepth = 3
        };
    }

    private IPropertyValueResolverFactory CreateResolverFactory()
    {
        var urlProvider = Substitute.For<Umbraco.Cms.Core.Routing.IPublishedUrlProvider>();
        return new PropertyValueResolverFactory(new IPropertyValueResolver[]
        {
            new DefaultPropertyValueResolver(),
            new BuiltInPropertyResolver(urlProvider),
            new ContentPickerResolver()
        });
    }

    private PropertyResolverContext CreateContext(IPublishedProperty? property)
    {
        return new PropertyResolverContext
        {
            Content = Substitute.For<IPublishedContent>(),
            Mapping = new PropertyMapping { SchemaPropertyName = "Link" },
            PropertyAlias = "link",
            SchemaTypeRegistry = _registry,
            MappingRepository = _repository,
            HttpContextAccessor = _httpContextAccessor,
            Property = property
        };
    }
}
