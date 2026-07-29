using FluentAssertions;
using Microsoft.AspNetCore.Http;
using NSubstitute;
using Xunit;
using Schema.NET;
using Umbraco.Cms.Core.Models.PublishedContent;
using Umbraco.Community.SchemeWeaver.Models.Entities;
using Umbraco.Community.SchemeWeaver.Persistence;
using Umbraco.Community.SchemeWeaver.Services;
using Umbraco.Community.SchemeWeaver.Services.Resolvers;

namespace Umbraco.Community.SchemeWeaver.Tests.Unit.Resolvers;

public class MultiNodeTreePickerResolverTests
{
    private readonly MultiNodeTreePickerResolver _sut = new();
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly ISchemaTypeRegistry _registry = new SchemaTypeRegistry();
    private readonly ISchemaMappingRepository _repository = Substitute.For<ISchemaMappingRepository>();

    public MultiNodeTreePickerResolverTests()
    {
        _httpContextAccessor = Substitute.For<IHttpContextAccessor>();
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Scheme = "https";
        httpContext.Request.Host = new HostString("example.com");
        _httpContextAccessor.HttpContext.Returns(httpContext);
    }

    [Fact]
    public void SupportedEditorAliases_ContainsMultiNodeTreePicker()
    {
        _sut.SupportedEditorAliases.Should().Contain("Umbraco.MultiNodeTreePicker");
    }

    [Fact]
    public void Priority_Returns10()
    {
        _sut.Priority.Should().Be(10);
    }

    [Fact]
    public void Resolve_NullProperty_ReturnsNull()
    {
        _sut.Resolve(CreateContext(value: null, propertyPresent: false)).Should().BeNull();
    }

    [Fact]
    public void Resolve_NullValue_ReturnsNull()
    {
        _sut.Resolve(CreateContext(value: null)).Should().BeNull();
    }

    [Fact]
    public void Resolve_StringValue_ReturnsNull()
    {
        // A string is IEnumerable<char>, not picked content — must not be treated as a list.
        _sut.Resolve(CreateContext(value: "umb://document/abc")).Should().BeNull();
    }

    [Fact]
    public void Resolve_RawUdiArray_ReturnsNull()
    {
        // Without an Umbraco context the MNTP converter returns the raw source array —
        // strict matching must reject it rather than ToString() it.
        _sut.Resolve(CreateContext(value: new object[] { new Uri("umb://document/x") }))
            .Should().BeNull();
    }

    [Fact]
    public void Resolve_SingleContent_ReturnsName_ParityWithContentPicker()
    {
        var picked = CreateNamedContent("About Us");

        var result = _sut.Resolve(CreateContext(picked));

        result.Should().Be("About Us");
    }

    [Fact]
    public void Resolve_ListOfOne_ReturnsSingleValueNotList()
    {
        var picked = CreateNamedContent("Only One");

        var result = _sut.Resolve(CreateContext(new List<IPublishedContent> { picked }));

        result.Should().Be("Only One");
    }

    [Fact]
    public void Resolve_EmptyList_ReturnsNull()
    {
        _sut.Resolve(CreateContext(new List<IPublishedContent>())).Should().BeNull();
    }

    [Fact]
    public void Resolve_MultipleWithoutConfig_ReturnsStringListOfNames()
    {
        var value = new List<IPublishedContent> { CreateNamedContent("Alpha"), CreateNamedContent("Beta") };

        var result = _sut.Resolve(CreateContext(value));

        result.Should().BeOfType<List<string>>()
            .Which.Should().Equal("Alpha", "Beta");
    }

    [Fact]
    public void Resolve_MultipleWithNestedMappings_ReturnsThingList()
    {
        var value = new List<IPublishedContent>
        {
            CreateMappedPerson("Jane Doe", mappingId: 11),
            CreateMappedPerson("John Smith", mappingId: 11)
        };

        var result = _sut.Resolve(CreateContext(value, nestedSchemaTypeName: "Person"));

        var things = result.Should().BeOfType<List<Thing>>().Subject;
        things.Should().HaveCount(2);
        things.Should().AllBeOfType<Person>();
    }

    [Fact]
    public void Resolve_MixedThingsAndStrings_PrefersThings()
    {
        // One picked node has a mapped content type, the other doesn't (falls to name).
        // A mixed List<object> would be dropped wholesale by SchemaPropertySetter, so
        // the resolver homogenises: Things win, strings are dropped.
        var mapped = CreateMappedPerson("Jane Doe", mappingId: 12);
        var unmapped = CreateNamedContent("Loose End");

        var result = _sut.Resolve(CreateContext(
            new List<IPublishedContent> { mapped, unmapped }, nestedSchemaTypeName: "Person"));

        var things = result.Should().BeOfType<List<Thing>>().Subject;
        things.Should().ContainSingle().Which.Should().BeOfType<Person>();
    }

    [Fact]
    public void Resolve_DrillDown_AcrossItems_SkipsMisses()
    {
        var withProperty = CreateContentWithProperty("First", "jobTitle", "Developer");
        var withoutProperty = CreateNamedContent("Second"); // no jobTitle property

        var result = _sut.Resolve(CreateContext(
            new List<IPublishedContent> { withProperty, withoutProperty },
            resolverConfig: """{"pickedPropertyAlias":"jobTitle"}"""));

        // Single surviving value → returned directly, not as a list.
        result.Should().Be("Developer");
    }

    [Fact]
    public void Resolve_DrillDown_MultipleValues_ReturnsStringList()
    {
        var first = CreateContentWithProperty("First", "jobTitle", "Developer");
        var second = CreateContentWithProperty("Second", "jobTitle", "Designer");

        var result = _sut.Resolve(CreateContext(
            new List<IPublishedContent> { first, second },
            resolverConfig: """{"pickedPropertyAlias":"jobTitle"}"""));

        result.Should().BeOfType<List<string>>()
            .Which.Should().Equal("Developer", "Designer");
    }

    [Fact]
    public void Resolve_VisitedItem_FallsBackToNameForThatItem()
    {
        var visitedKey = Guid.NewGuid();
        var visited = CreateMappedPerson("Cycle Node", mappingId: 13);
        visited.Key.Returns(visitedKey);
        var fresh = CreateMappedPerson("Fresh Node", mappingId: 13);

        var context = CreateContext(
            new List<IPublishedContent> { visited, fresh },
            nestedSchemaTypeName: "Person",
            visitedKeys: [visitedKey]);

        var result = _sut.Resolve(context);

        // The visited item degrades to its name (a string); homogenisation then
        // prefers the fresh item's Thing.
        var things = result.Should().BeOfType<List<Thing>>().Subject;
        things.Should().ContainSingle().Which.Should().BeOfType<Person>();
    }

    [Fact]
    public void Resolve_PerItemException_OtherItemsStillEmitted()
    {
        var throwing = Substitute.For<IPublishedContent>();
        throwing.Name.Returns(_ => throw new InvalidOperationException("boom"));
        var healthy = CreateNamedContent("Survivor");

        var result = _sut.Resolve(CreateContext(new List<IPublishedContent> { throwing, healthy }));

        result.Should().Be("Survivor");
    }

    // --- helpers ---

    private static IPublishedContent CreateNamedContent(string name)
    {
        var contentType = Substitute.For<IPublishedContentType>();
        contentType.Alias.Returns("unmappedType");

        var content = Substitute.For<IPublishedContent>();
        content.Name.Returns(name);
        content.Key.Returns(Guid.NewGuid());
        content.ContentType.Returns(contentType);
        return content;
    }

    private static IPublishedContent CreateContentWithProperty(string name, string alias, object value)
    {
        var content = CreateNamedContent(name);

        var propertyType = Substitute.For<IPublishedPropertyType>();
        propertyType.EditorAlias.Returns("Umbraco.TextBox");

        var property = Substitute.For<IPublishedProperty>();
        property.GetValue(Arg.Any<string?>(), Arg.Any<string?>()).Returns(value);
        property.PropertyType.Returns(propertyType);

        content.GetProperty(alias).Returns(property);
        return content;
    }

    private IPublishedContent CreateMappedPerson(string fullName, int mappingId)
    {
        var contentType = Substitute.For<IPublishedContentType>();
        contentType.Alias.Returns("person");

        var content = CreateContentWithProperty(fullName, "fullName", fullName);
        content.ContentType.Returns(contentType);

        _repository.GetByContentTypeAlias("person").Returns(new SchemaMapping
        {
            Id = mappingId,
            ContentTypeAlias = "person",
            SchemaTypeName = "Person",
            IsEnabled = true
        });
        _repository.GetPropertyMappings(mappingId).Returns(new List<PropertyMapping>
        {
            new() { SchemaPropertyName = "Name", ContentTypePropertyAlias = "fullName" }
        });

        return content;
    }

    private PropertyResolverContext CreateContext(
        object? value,
        string? nestedSchemaTypeName = null,
        string? resolverConfig = null,
        HashSet<Guid>? visitedKeys = null,
        bool propertyPresent = true)
    {
        IPublishedProperty? property = null;
        if (propertyPresent)
        {
            property = Substitute.For<IPublishedProperty>();
            property.GetValue(Arg.Any<string?>(), Arg.Any<string?>()).Returns(value);
        }

        var urlProvider = Substitute.For<Umbraco.Cms.Core.Routing.IPublishedUrlProvider>();
        var factory = new PropertyValueResolverFactory(new IPropertyValueResolver[]
        {
            new DefaultPropertyValueResolver(),
            new BuiltInPropertyResolver(urlProvider),
            new ContentPickerResolver(),
            new MultiNodeTreePickerResolver()
        });

        return new PropertyResolverContext
        {
            Content = Substitute.For<IPublishedContent>(),
            Mapping = new PropertyMapping
            {
                SchemaPropertyName = "Author",
                NestedSchemaTypeName = nestedSchemaTypeName,
                ResolverConfig = resolverConfig
            },
            PropertyAlias = "authors",
            SchemaTypeRegistry = _registry,
            MappingRepository = _repository,
            HttpContextAccessor = _httpContextAccessor,
            ResolverFactory = factory,
            Property = property,
            RecursionDepth = 0,
            MaxRecursionDepth = 3,
            VisitedContentKeys = visitedKeys ?? []
        };
    }
}
