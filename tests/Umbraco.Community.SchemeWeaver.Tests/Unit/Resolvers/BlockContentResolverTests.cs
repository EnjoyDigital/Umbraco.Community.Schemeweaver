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

    [Fact]
    public void Resolve_StringListExtraction_ReturnsListOfStrings()
    {
        var block1 = CreateBlockElement("ingredientItem", new Dictionary<string, object?>
        {
            ["ingredient"] = "200g flour"
        });
        var block2 = CreateBlockElement("ingredientItem", new Dictionary<string, object?>
        {
            ["ingredient"] = "100g sugar"
        });
        var block3 = CreateBlockElement("ingredientItem", new Dictionary<string, object?>
        {
            ["ingredient"] = "2 eggs"
        });
        var blockListModel = CreateBlockListModel(block1, block2, block3);

        var resolverConfig = JsonSerializer.Serialize(new { extractAs = "stringList", contentProperty = "ingredient" });

        var property = Substitute.For<IPublishedProperty>();
        property.GetValue(Arg.Any<string?>(), Arg.Any<string?>()).Returns(blockListModel);

        var context = CreateContext(property, nestedSchemaTypeName: null, resolverConfig: resolverConfig);

        var result = _sut.Resolve(context);

        result.Should().NotBeNull();
        result.Should().BeAssignableTo<IEnumerable<string>>();
        var strings = ((IEnumerable<string>)result!).ToList();
        strings.Should().HaveCount(3);
        strings[0].Should().Be("200g flour");
        strings[1].Should().Be("100g sugar");
        strings[2].Should().Be("2 eggs");
    }

    [Fact]
    public void Resolve_StringListExtraction_SkipsEmptyValues()
    {
        var block1 = CreateBlockElement("ingredientItem", new Dictionary<string, object?>
        {
            ["ingredient"] = "200g flour"
        });
        var block2 = CreateBlockElement("ingredientItem", new Dictionary<string, object?>
        {
            ["ingredient"] = null
        });
        var block3 = CreateBlockElement("ingredientItem", new Dictionary<string, object?>
        {
            ["ingredient"] = "2 eggs"
        });
        var blockListModel = CreateBlockListModel(block1, block2, block3);

        var resolverConfig = JsonSerializer.Serialize(new { extractAs = "stringList", contentProperty = "ingredient" });

        var property = Substitute.For<IPublishedProperty>();
        property.GetValue(Arg.Any<string?>(), Arg.Any<string?>()).Returns(blockListModel);

        var context = CreateContext(property, nestedSchemaTypeName: null, resolverConfig: resolverConfig);

        var result = _sut.Resolve(context);

        result.Should().NotBeNull();
        result.Should().BeAssignableTo<IEnumerable<string>>();
        var strings = ((IEnumerable<string>)result!).ToList();
        strings.Should().HaveCount(2);
        strings[0].Should().Be("200g flour");
        strings[1].Should().Be("2 eggs");
    }

    [Fact]
    public void Resolve_StringListExtraction_EmptyBlocks_ReturnsNull()
    {
        var blockListModel = new BlockListModel(new List<BlockListItem>());

        var resolverConfig = JsonSerializer.Serialize(new { extractAs = "stringList", contentProperty = "ingredient" });

        var property = Substitute.For<IPublishedProperty>();
        property.GetValue(Arg.Any<string?>(), Arg.Any<string?>()).Returns(blockListModel);

        var context = CreateContext(property, nestedSchemaTypeName: null, resolverConfig: resolverConfig);

        var result = _sut.Resolve(context);

        result.Should().BeNull();
    }

    [Fact]
    public void Resolve_StringListExtraction_NoNestedSchemaTypeRequired()
    {
        var block1 = CreateBlockElement("ingredientItem", new Dictionary<string, object?>
        {
            ["ingredient"] = "200g flour"
        });
        var block2 = CreateBlockElement("ingredientItem", new Dictionary<string, object?>
        {
            ["ingredient"] = "100g sugar"
        });
        var blockListModel = CreateBlockListModel(block1, block2);

        var resolverConfig = JsonSerializer.Serialize(new { extractAs = "stringList", contentProperty = "ingredient" });

        var property = Substitute.For<IPublishedProperty>();
        property.GetValue(Arg.Any<string?>(), Arg.Any<string?>()).Returns(blockListModel);

        // NestedSchemaTypeName is explicitly null — string extraction should still work
        var context = CreateContext(property, nestedSchemaTypeName: null, resolverConfig: resolverConfig);

        var result = _sut.Resolve(context);

        result.Should().NotBeNull();
        result.Should().BeAssignableTo<IEnumerable<string>>();
        var strings = ((IEnumerable<string>)result!).ToList();
        strings.Should().HaveCount(2);
        strings[0].Should().Be("200g flour");
        strings[1].Should().Be("100g sugar");
    }

    [Fact]
    public void Resolve_ReviewBlockWithConfig_MapsAuthorAndBody()
    {
        var blockElement = CreateBlockElement("reviewItem", new Dictionary<string, object?>
        {
            ["reviewAuthor"] = "Jane Smith",
            ["ratingValue"] = "5",
            ["reviewBody"] = "Excellent product, highly recommended."
        });
        var blockListModel = CreateBlockListModel(blockElement);

        var resolverConfig = JsonSerializer.Serialize(new ResolverConfigModel
        {
            NestedMappings = new List<NestedPropertyMapping>
            {
                new() { SchemaProperty = "author", ContentProperty = "reviewAuthor" },
                new() { SchemaProperty = "reviewBody", ContentProperty = "reviewBody" }
            }
        });

        var property = Substitute.For<IPublishedProperty>();
        property.GetValue(Arg.Any<string?>(), Arg.Any<string?>()).Returns(blockListModel);

        var context = CreateContext(property, nestedSchemaTypeName: "Review", resolverConfig: resolverConfig);

        var result = _sut.Resolve(context);

        result.Should().NotBeNull();
        result.Should().BeAssignableTo<IEnumerable<Schema.NET.Thing>>();
        var things = ((IEnumerable<Schema.NET.Thing>)result!).ToList();
        things.Should().HaveCount(1);
        things[0].Should().BeOfType<Schema.NET.Review>();

        var review = (Schema.NET.Review)things[0];
        var jsonLd = review.ToString();
        jsonLd.Should().Contain("Excellent product, highly recommended.");
    }

    [Fact]
    public void Resolve_HowToStepBlockWithConfig_MapsNameAndText()
    {
        var blockElement = CreateBlockElement("instructionStep", new Dictionary<string, object?>
        {
            ["stepName"] = "Preheat Oven",
            ["stepText"] = "Preheat your oven to 180°C (350°F)."
        });
        var blockListModel = CreateBlockListModel(blockElement);

        var resolverConfig = JsonSerializer.Serialize(new ResolverConfigModel
        {
            NestedMappings = new List<NestedPropertyMapping>
            {
                new() { SchemaProperty = "name", ContentProperty = "stepName" },
                new() { SchemaProperty = "text", ContentProperty = "stepText" }
            }
        });

        var property = Substitute.For<IPublishedProperty>();
        property.GetValue(Arg.Any<string?>(), Arg.Any<string?>()).Returns(blockListModel);

        var context = CreateContext(property, nestedSchemaTypeName: "HowToStep", resolverConfig: resolverConfig);

        var result = _sut.Resolve(context);

        result.Should().NotBeNull();
        result.Should().BeAssignableTo<IEnumerable<Schema.NET.Thing>>();
        var things = ((IEnumerable<Schema.NET.Thing>)result!).ToList();
        things.Should().HaveCount(1);
        things[0].Should().BeOfType<Schema.NET.HowToStep>();

        var step = (Schema.NET.HowToStep)things[0];
        var jsonLd = step.ToString();
        jsonLd.Should().Contain("Preheat Oven");
        jsonLd.Should().Contain("Preheat your oven to 180");
    }

    [Fact]
    public void Resolve_FAQWithWrapInType_ProducesQuestionWithAnswer()
    {
        var blockElement = CreateBlockElement("faqItem", new Dictionary<string, object?>
        {
            ["questionText"] = "What is structured data?",
            ["answerText"] = "Structured data is a standardised format for providing information about a page."
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

        var question = (Schema.NET.Question)things[0];
        var jsonLd = question.ToString();
        jsonLd.Should().Contain("What is structured data?");
        jsonLd.Should().Contain("Answer");
        jsonLd.Should().Contain("Structured data is a standardised format for providing information about a page.");
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
