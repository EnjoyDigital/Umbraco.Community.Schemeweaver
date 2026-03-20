using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using NSubstitute;
using Xunit;
using Umbraco.Cms.Core.Models.Blocks;
using Umbraco.Cms.Core.Models.PublishedContent;
using Umbraco.Community.SchemeWeaver.Models.Entities;
using Umbraco.Community.SchemeWeaver.Persistence;
using Umbraco.Community.SchemeWeaver.Services;
using Umbraco.Community.SchemeWeaver.Services.Resolvers;

namespace Umbraco.Community.SchemeWeaver.Tests.Unit.Resolvers;

public class BlockContentResolverTests
{
    private readonly BlockContentResolver _sut = new();
    private readonly ISchemaTypeRegistry _registry = new SchemaTypeRegistry();
    private readonly ISchemaMappingRepository _repository = Substitute.For<ISchemaMappingRepository>();
    private readonly IHttpContextAccessor _httpContextAccessor = Substitute.For<IHttpContextAccessor>();

    [Fact]
    public void SupportedEditorAliases_ContainsBlockList()
    {
        _sut.SupportedEditorAliases.Should().Contain("Umbraco.BlockList");
    }

    [Fact]
    public void SupportedEditorAliases_ContainsBlockGrid()
    {
        _sut.SupportedEditorAliases.Should().Contain("Umbraco.BlockGrid");
    }

    [Fact]
    public void Priority_Returns10()
    {
        _sut.Priority.Should().Be(10);
    }

    [Fact]
    public void Resolve_NullProperty_ReturnsNull()
    {
        var context = CreateContext(null, nestedSchemaTypeName: "Question");

        var result = _sut.Resolve(context);

        result.Should().BeNull();
    }

    [Fact]
    public void Resolve_NullPropertyValue_ReturnsNull()
    {
        var property = Substitute.For<IPublishedProperty>();
        property.GetValue(Arg.Any<string?>(), Arg.Any<string?>()).Returns(null);

        var context = CreateContext(property, nestedSchemaTypeName: "Question");

        var result = _sut.Resolve(context);

        result.Should().BeNull();
    }

    [Fact]
    public void Resolve_NoNestedSchemaTypeName_ReturnsNull()
    {
        var blockListModel = CreateBlockListModel(CreateBlockElement("faqItem", new Dictionary<string, object?>
        {
            ["name"] = "What is SchemeWeaver?"
        }));

        var property = Substitute.For<IPublishedProperty>();
        property.GetValue(Arg.Any<string?>(), Arg.Any<string?>()).Returns(blockListModel);

        var context = CreateContext(property, nestedSchemaTypeName: null);

        var result = _sut.Resolve(context);

        result.Should().BeNull();
    }

    [Fact]
    public void Resolve_MaxRecursionDepthReached_ReturnsNull()
    {
        var blockListModel = CreateBlockListModel(CreateBlockElement("faqItem", new Dictionary<string, object?>
        {
            ["name"] = "What is SchemeWeaver?"
        }));

        var property = Substitute.For<IPublishedProperty>();
        property.GetValue(Arg.Any<string?>(), Arg.Any<string?>()).Returns(blockListModel);

        var context = CreateContext(property, nestedSchemaTypeName: "Question", recursionDepth: 3);

        var result = _sut.Resolve(context);

        result.Should().BeNull();
    }

    [Fact]
    public void Resolve_BlockListWithAutoMap_ReturnsMappedThings()
    {
        var blockElement = CreateBlockElement("faqItem", new Dictionary<string, object?>
        {
            ["name"] = "What is SchemeWeaver?"
        });
        var blockListModel = CreateBlockListModel(blockElement);

        var property = Substitute.For<IPublishedProperty>();
        property.GetValue(Arg.Any<string?>(), Arg.Any<string?>()).Returns(blockListModel);

        var context = CreateContext(property, nestedSchemaTypeName: "Question");

        var result = _sut.Resolve(context);

        result.Should().NotBeNull();
        result.Should().BeAssignableTo<IEnumerable<Schema.NET.Thing>>();
        var things = ((IEnumerable<Schema.NET.Thing>)result!).ToList();
        things.Should().HaveCount(1);
        things[0].Should().BeOfType<Schema.NET.Question>();
    }

    [Fact]
    public void Resolve_BlockListWithResolverConfig_UsesConfigMappings()
    {
        var blockElement = CreateBlockElement("faqItem", new Dictionary<string, object?>
        {
            ["questionText"] = "What is SchemeWeaver?"
        });
        var blockListModel = CreateBlockListModel(blockElement);

        var resolverConfig = JsonSerializer.Serialize(new ResolverConfigModel
        {
            NestedMappings = new List<NestedPropertyMapping>
            {
                new()
                {
                    BlockAlias = "faqItem",
                    SchemaProperty = "name",
                    ContentProperty = "questionText"
                }
            }
        });

        var property = Substitute.For<IPublishedProperty>();
        property.GetValue(Arg.Any<string?>(), Arg.Any<string?>()).Returns(blockListModel);

        var context = CreateContext(property, nestedSchemaTypeName: "Question", resolverConfig: resolverConfig);

        var result = _sut.Resolve(context);

        result.Should().NotBeNull();
        result.Should().BeAssignableTo<IEnumerable<Schema.NET.Thing>>();
        var things = ((IEnumerable<Schema.NET.Thing>)result!).ToList();
        things.Should().HaveCount(1);
        things[0].Should().BeOfType<Schema.NET.Question>();
    }

    [Fact]
    public void Resolve_BlockListWithWrapInType_WrapsValueInNestedThing()
    {
        var blockElement = CreateBlockElement("faqItem", new Dictionary<string, object?>
        {
            ["questionText"] = "What is SchemeWeaver?",
            ["answerText"] = "A Schema.org mapping tool for Umbraco."
        });
        var blockListModel = CreateBlockListModel(blockElement);

        var resolverConfig = JsonSerializer.Serialize(new ResolverConfigModel
        {
            NestedMappings = new List<NestedPropertyMapping>
            {
                new()
                {
                    BlockAlias = "faqItem",
                    SchemaProperty = "name",
                    ContentProperty = "questionText"
                },
                new()
                {
                    BlockAlias = "faqItem",
                    SchemaProperty = "acceptedAnswer",
                    ContentProperty = "answerText",
                    WrapInType = "Answer",
                    WrapInProperty = "Text"
                }
            }
        });

        var property = Substitute.For<IPublishedProperty>();
        property.GetValue(Arg.Any<string?>(), Arg.Any<string?>()).Returns(blockListModel);

        var context = CreateContext(property, nestedSchemaTypeName: "Question", resolverConfig: resolverConfig);

        var result = _sut.Resolve(context);

        result.Should().NotBeNull();
        result.Should().BeAssignableTo<IEnumerable<Schema.NET.Thing>>();
        var things = ((IEnumerable<Schema.NET.Thing>)result!).ToList();
        things.Should().HaveCount(1);
        things[0].Should().BeOfType<Schema.NET.Question>();
    }

    [Fact]
    public void Resolve_MultipleBlockItems_ReturnsMultipleThings()
    {
        var block1 = CreateBlockElement("faqItem", new Dictionary<string, object?>
        {
            ["name"] = "Question 1"
        });
        var block2 = CreateBlockElement("faqItem", new Dictionary<string, object?>
        {
            ["name"] = "Question 2"
        });
        var blockListModel = CreateBlockListModel(block1, block2);

        var property = Substitute.For<IPublishedProperty>();
        property.GetValue(Arg.Any<string?>(), Arg.Any<string?>()).Returns(blockListModel);

        var context = CreateContext(property, nestedSchemaTypeName: "Question");

        var result = _sut.Resolve(context);

        result.Should().NotBeNull();
        var things = ((IEnumerable<Schema.NET.Thing>)result!).ToList();
        things.Should().HaveCount(2);
    }

    [Fact]
    public void Resolve_EmptyBlockList_ReturnsNull()
    {
        var blockListModel = new BlockListModel(new List<BlockListItem>());

        var property = Substitute.For<IPublishedProperty>();
        property.GetValue(Arg.Any<string?>(), Arg.Any<string?>()).Returns(blockListModel);

        var context = CreateContext(property, nestedSchemaTypeName: "Question");

        var result = _sut.Resolve(context);

        result.Should().BeNull();
    }

    [Fact]
    public void Resolve_InvalidResolverConfigJson_FallsBackToAutoMap()
    {
        var blockElement = CreateBlockElement("faqItem", new Dictionary<string, object?>
        {
            ["name"] = "What is SchemeWeaver?"
        });
        var blockListModel = CreateBlockListModel(blockElement);

        var property = Substitute.For<IPublishedProperty>();
        property.GetValue(Arg.Any<string?>(), Arg.Any<string?>()).Returns(blockListModel);

        var context = CreateContext(property, nestedSchemaTypeName: "Question", resolverConfig: "invalid json{{{");

        var result = _sut.Resolve(context);

        result.Should().NotBeNull();
        result.Should().BeAssignableTo<IEnumerable<Schema.NET.Thing>>();
        var things = ((IEnumerable<Schema.NET.Thing>)result!).ToList();
        things.Should().HaveCount(1);
    }

    [Fact]
    public void Resolve_UnknownSchemaType_ReturnsNull()
    {
        var blockElement = CreateBlockElement("faqItem", new Dictionary<string, object?>
        {
            ["name"] = "Test"
        });
        var blockListModel = CreateBlockListModel(blockElement);

        var property = Substitute.For<IPublishedProperty>();
        property.GetValue(Arg.Any<string?>(), Arg.Any<string?>()).Returns(blockListModel);

        var context = CreateContext(property, nestedSchemaTypeName: "NonExistentSchemaType");

        var result = _sut.Resolve(context);

        result.Should().BeNull();
    }

    [Fact]
    public void Resolve_ConfigWithEmptyBlockAlias_MatchesAllBlocks()
    {
        var blockElement = CreateBlockElement("anyBlockType", new Dictionary<string, object?>
        {
            ["questionText"] = "Test question"
        });
        var blockListModel = CreateBlockListModel(blockElement);

        var resolverConfig = JsonSerializer.Serialize(new ResolverConfigModel
        {
            NestedMappings = new List<NestedPropertyMapping>
            {
                new()
                {
                    BlockAlias = "", // empty = match all
                    SchemaProperty = "name",
                    ContentProperty = "questionText"
                }
            }
        });

        var property = Substitute.For<IPublishedProperty>();
        property.GetValue(Arg.Any<string?>(), Arg.Any<string?>()).Returns(blockListModel);

        var context = CreateContext(property, nestedSchemaTypeName: "Question", resolverConfig: resolverConfig);

        var result = _sut.Resolve(context);

        result.Should().NotBeNull();
        var things = ((IEnumerable<Schema.NET.Thing>)result!).ToList();
        things.Should().HaveCount(1);
    }

    private static IPublishedElement CreateBlockElement(string contentTypeAlias, Dictionary<string, object?> properties)
    {
        var element = Substitute.For<IPublishedElement>();
        var contentType = Substitute.For<IPublishedContentType>();
        contentType.Alias.Returns(contentTypeAlias);
        element.ContentType.Returns(contentType);
        element.Key.Returns(Guid.NewGuid());

        foreach (var kvp in properties)
        {
            var prop = Substitute.For<IPublishedProperty>();
            prop.GetValue(Arg.Any<string?>(), Arg.Any<string?>()).Returns(kvp.Value);
            element.GetProperty(kvp.Key).Returns(prop);
        }

        return element;
    }

    private static BlockListModel CreateBlockListModel(params IPublishedElement[] elements)
    {
        var items = elements.Select(e =>
            new BlockListItem(Guid.NewGuid(), e, null, null))
            .ToList();
        return new BlockListModel(items);
    }

    private PropertyResolverContext CreateContext(
        IPublishedProperty? property,
        string? nestedSchemaTypeName = null,
        string? resolverConfig = null,
        int recursionDepth = 0)
    {
        return new PropertyResolverContext
        {
            Content = Substitute.For<IPublishedContent>(),
            Mapping = new PropertyMapping
            {
                SchemaPropertyName = "MainEntity",
                NestedSchemaTypeName = nestedSchemaTypeName,
                ResolverConfig = resolverConfig
            },
            PropertyAlias = "faqItems",
            SchemaTypeRegistry = _registry,
            MappingRepository = _repository,
            HttpContextAccessor = _httpContextAccessor,
            Property = property,
            RecursionDepth = recursionDepth,
            MaxRecursionDepth = 3
        };
    }
}
