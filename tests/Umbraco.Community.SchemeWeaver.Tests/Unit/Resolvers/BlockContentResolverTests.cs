using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;
using Umbraco.Cms.Core.Models;
using Umbraco.Cms.Core.Models.Blocks;
using Umbraco.Cms.Core.Models.PublishedContent;
using Umbraco.Cms.Core.PropertyEditors.ValueConverters;
using Umbraco.Cms.Core.Routing;
using Umbraco.Cms.Core.Strings;
using Umbraco.Community.SchemeWeaver;
using Umbraco.Community.SchemeWeaver.Models.Entities;
using Umbraco.Community.SchemeWeaver.Persistence;
using Umbraco.Community.SchemeWeaver.Services;
using Umbraco.Community.SchemeWeaver.Services.Resolvers;
using Umbraco.Community.SchemeWeaver.Tests.Unit.TestSupport;

namespace Umbraco.Community.SchemeWeaver.Tests.Unit.Resolvers;

public class BlockContentResolverTests
{
    private readonly BlockContentResolver _sut = new(NullLogger<BlockContentResolver>.Instance);
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
    public void Resolve_BlockContentModeWithoutNestedSchemaTypeName_ReturnsNull()
    {
        var blockListModel = CreateBlockListModel(CreateBlockElement("faqItem", new Dictionary<string, object?>
        {
            ["name"] = "What is SchemeWeaver?"
        }));

        var property = Substitute.For<IPublishedProperty>();
        property.GetValue(Arg.Any<string?>(), Arg.Any<string?>()).Returns(blockListModel);

        var context = CreateContext(property, nestedSchemaTypeName: null,
            sourceType: SchemeWeaverConstants.SourceTypes.BlockContent);

        var result = _sut.Resolve(context);

        result.Should().BeNull();
    }

    // --- Basic text extraction (#39): a block editor in plain property mode with no block
    // configuration emits the blocks' text/rich-text contents as one joined string. ---

    [Fact]
    public void Resolve_PropertyModeNoConfig_ExtractsTextAndRichTextAsJoinedString()
    {
        var blockListModel = CreateBlockListModel(CreateBlockElementWithEditors("textSection",
            ("heading", "Intro heading", "Umbraco.TextBox"),
            ("body", new HtmlEncodedString("<p>Rich text <strong>body</strong>.</p>"), "Umbraco.RichText")));

        var property = Substitute.For<IPublishedProperty>();
        property.GetValue(Arg.Any<string?>(), Arg.Any<string?>()).Returns(blockListModel);

        var context = CreateContext(property);

        var result = _sut.Resolve(context);

        result.Should().Be("Intro heading Rich text body.");
    }

    [Fact]
    public void Resolve_PropertyModeNoConfig_WithResolverFactory_DoesNotDoubleStrip()
    {
        // Through the factory the RTE is stripped+decoded ONCE by RichTextResolver; the encoded
        // "&lt;100&gt;" must survive as literal text rather than being eaten by a second strip.
        var blockListModel = CreateBlockListModel(CreateBlockElementWithEditors("textSection",
            ("body", new HtmlEncodedString("<p>score &lt;100&gt;</p>"), "Umbraco.RichText")));

        var property = Substitute.For<IPublishedProperty>();
        property.GetValue(Arg.Any<string?>(), Arg.Any<string?>()).Returns(blockListModel);

        var factory = new PropertyValueResolverFactory(
            [new RichTextResolver(), new DefaultPropertyValueResolver()]);
        var context = CreateContext(property, resolverFactory: factory);

        var result = _sut.Resolve(context);

        result.Should().Be("score <100>");
    }

    [Fact]
    public void Resolve_PropertyModeNoConfig_FactoryWithoutRichTextResolver_StillStripsHtml()
    {
        // With a factory whose default resolver serves the RTE alias, the value arrives as raw
        // HTML (ToString of the encoded string) and must still be stripped.
        var blockListModel = CreateBlockListModel(CreateBlockElementWithEditors("textSection",
            ("body", new HtmlEncodedString("<p>Hello <strong>World</strong></p>"), "Umbraco.RichText")));

        var property = Substitute.For<IPublishedProperty>();
        property.GetValue(Arg.Any<string?>(), Arg.Any<string?>()).Returns(blockListModel);

        var factory = new PropertyValueResolverFactory([new DefaultPropertyValueResolver()]);
        var context = CreateContext(property, resolverFactory: factory);

        var result = _sut.Resolve(context);

        result.Should().Be("Hello World");
    }

    [Fact]
    public void Resolve_PropertyModeNoConfig_PlainTextWithAngleBrackets_NotMangled()
    {
        var blockListModel = CreateBlockListModel(CreateBlockElementWithEditors("textSection",
            ("heading", "a<b and c>d", "Umbraco.TextBox")));

        var property = Substitute.For<IPublishedProperty>();
        property.GetValue(Arg.Any<string?>(), Arg.Any<string?>()).Returns(blockListModel);

        var context = CreateContext(property);

        var result = _sut.Resolve(context);

        result.Should().Be("a<b and c>d");
    }

    [Fact]
    public void Resolve_PropertyModeNoConfig_NonTextProperties_Ignored()
    {
        var blockListModel = CreateBlockListModel(CreateBlockElementWithEditors("textSection",
            ("count", 42, "Umbraco.Integer"),
            ("image", "some-media-value", "Umbraco.MediaPicker3"),
            ("title", "Hello", "Umbraco.TextBox")));

        var property = Substitute.For<IPublishedProperty>();
        property.GetValue(Arg.Any<string?>(), Arg.Any<string?>()).Returns(blockListModel);

        var context = CreateContext(property);

        var result = _sut.Resolve(context);

        result.Should().Be("Hello");
    }

    [Fact]
    public void Resolve_PropertyModeNoConfig_AllValuesEmptyOrWhitespace_ReturnsNull()
    {
        var blockListModel = CreateBlockListModel(CreateBlockElementWithEditors("textSection",
            ("heading", "   ", "Umbraco.TextBox"),
            ("body", new HtmlEncodedString("<p> </p>"), "Umbraco.RichText")));

        var property = Substitute.For<IPublishedProperty>();
        property.GetValue(Arg.Any<string?>(), Arg.Any<string?>()).Returns(blockListModel);

        var context = CreateContext(property);

        var result = _sut.Resolve(context);

        result.Should().BeNull();
    }

    [Fact]
    public void Resolve_PropertyModeNoConfig_MultipleBlocks_JoinsInOrder()
    {
        var blockListModel = CreateBlockListModel(
            CreateBlockElementWithEditors("textSection", ("heading", "First", "Umbraco.TextBox")),
            CreateBlockElementWithEditors("textSection", ("heading", "Second", "Umbraco.TextArea")));

        var property = Substitute.For<IPublishedProperty>();
        property.GetValue(Arg.Any<string?>(), Arg.Any<string?>()).Returns(blockListModel);

        var context = CreateContext(property);

        var result = _sut.Resolve(context);

        result.Should().Be("First Second");
    }

    [Fact]
    public void Resolve_PropertyModeNoConfig_NestedBlockList_IncludesNestedText()
    {
        var nestedList = CreateBlockListModel(
            CreateBlockElementWithEditors("textSection", ("heading", "Inner", "Umbraco.TextBox")));
        var blockListModel = CreateBlockListModel(CreateBlockElementWithEditors("sectionGroup",
            ("heading", "Outer", "Umbraco.TextBox"),
            ("sections", nestedList, "Umbraco.BlockList")));

        var property = Substitute.For<IPublishedProperty>();
        property.GetValue(Arg.Any<string?>(), Arg.Any<string?>()).Returns(blockListModel);

        var context = CreateContext(property);

        var result = _sut.Resolve(context);

        result.Should().Be("Outer Inner");
    }

    [Fact]
    public void Resolve_PropertyModeNoConfig_NestedBlocksBeyondMaxDepth_Skipped()
    {
        var nestedList = CreateBlockListModel(
            CreateBlockElementWithEditors("textSection", ("heading", "Inner", "Umbraco.TextBox")));
        var blockListModel = CreateBlockListModel(CreateBlockElementWithEditors("sectionGroup",
            ("heading", "Outer", "Umbraco.TextBox"),
            ("sections", nestedList, "Umbraco.BlockList")));

        var property = Substitute.For<IPublishedProperty>();
        property.GetValue(Arg.Any<string?>(), Arg.Any<string?>()).Returns(blockListModel);

        // Depth 2 of max 3: the top level may still resolve, but descending into the nested
        // block list would hit the cap, so only the outer text is extracted.
        var context = CreateContext(property, recursionDepth: 2);

        var result = _sut.Resolve(context);

        result.Should().Be("Outer");
    }

    [Fact]
    public void Resolve_PropertyModeNoConfig_BlockGrid_ExtractsText()
    {
        var blockGridModel = CreateBlockGridModel(
            CreateBlockElementWithEditors("textSection", ("heading", "Grid text", "Umbraco.TextBox")));

        var property = Substitute.For<IPublishedProperty>();
        property.GetValue(Arg.Any<string?>(), Arg.Any<string?>()).Returns(blockGridModel);

        var context = CreateContext(property);

        var result = _sut.Resolve(context);

        result.Should().Be("Grid text");
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
    public void Resolve_StringListExtraction_MultiUrlPickerProperty_ReturnsAbsoluteUrls()
    {
        // extractAs:stringList over blocks whose contentProperty is a MultiUrlPicker
        // (e.g. a social-profile link block feeding Organization.sameAs). The value is
        // IEnumerable<Link>, not a string — the stringList path must route it through the
        // factory's MultiUrlPickerResolver instead of falling back to .ToString(), which
        // yields "System.Collections.Generic.List`1[Umbraco.Cms.Core.Models.Link]" garbage.
        var block1 = CreateBlockElementWithEditors("socialLink",
            ("href", new List<Link> { new() { Url = "https://twitter.com/acme", Name = "Twitter" } }, "Umbraco.MultiUrlPicker"));
        var block2 = CreateBlockElementWithEditors("socialLink",
            ("href", new List<Link> { new() { Url = "https://facebook.com/acme", Name = "Facebook" } }, "Umbraco.MultiUrlPicker"));
        var blockListModel = CreateBlockListModel(block1, block2);

        var resolverConfig = JsonSerializer.Serialize(new { extractAs = "stringList", contentProperty = "href" });

        var property = Substitute.For<IPublishedProperty>();
        property.GetValue(Arg.Any<string?>(), Arg.Any<string?>()).Returns(blockListModel);

        var factory = new PropertyValueResolverFactory([
            new MultiUrlPickerResolver(),
            new DefaultPropertyValueResolver()
        ]);
        var context = CreateContext(property, resolverConfig: resolverConfig, resolverFactory: factory);

        var result = _sut.Resolve(context);

        var strings = result.Should().BeOfType<List<string>>().Subject;
        strings.Should().Equal("https://twitter.com/acme", "https://facebook.com/acme");
    }

    [Fact]
    public void Resolve_StringListExtraction_NumericBlockProperty_EmitsInvariantString()
    {
        // extractAs:stringList over a block whose contentProperty is Umbraco.Integer.
        // The factory routes it to NumericResolver, which returns a boxed int — the
        // stringList path must reduce it to its invariant string form ("5"), matching
        // what the legacy ResolveElementPropertyValue path emitted, not drop it.
        var block = CreateBlockElementWithEditors("stepBlock", ("minutes", 5, "Umbraco.Integer"));
        var blockListModel = CreateBlockListModel(block);

        var resolverConfig = JsonSerializer.Serialize(new { extractAs = "stringList", contentProperty = "minutes" });

        var property = Substitute.For<IPublishedProperty>();
        property.GetValue(Arg.Any<string?>(), Arg.Any<string?>()).Returns(blockListModel);

        var factory = new PropertyValueResolverFactory([
            new NumericResolver(),
            new DefaultPropertyValueResolver()
        ]);
        var context = CreateContext(property, resolverConfig: resolverConfig, resolverFactory: factory);

        var result = _sut.Resolve(context);

        var strings = result.Should().BeOfType<List<string>>().Subject;
        strings.Should().Equal("5");
    }

    [Fact]
    public void Resolve_StringListExtraction_BooleanBlockProperty_EmitsStringForm()
    {
        // extractAs:stringList over a block whose contentProperty is Umbraco.TrueFalse.
        // BooleanResolver returns a bool — the stringList path must emit its string form
        // ("True", as the legacy ToString() path did), not silently drop the value.
        var block = CreateBlockElementWithEditors("featureBlock", ("included", true, "Umbraco.TrueFalse"));
        var blockListModel = CreateBlockListModel(block);

        var resolverConfig = JsonSerializer.Serialize(new { extractAs = "stringList", contentProperty = "included" });

        var property = Substitute.For<IPublishedProperty>();
        property.GetValue(Arg.Any<string?>(), Arg.Any<string?>()).Returns(blockListModel);

        var factory = new PropertyValueResolverFactory([
            new BooleanResolver(),
            new DefaultPropertyValueResolver()
        ]);
        var context = CreateContext(property, resolverConfig: resolverConfig, resolverFactory: factory);

        var result = _sut.Resolve(context);

        var strings = result.Should().BeOfType<List<string>>().Subject;
        strings.Should().Equal("True");
    }

    [Fact]
    public void Resolve_StringListExtraction_MediaPickerBlockProperty_EmitsMediaUrl()
    {
        // extractAs:stringList over a block whose contentProperty is a MediaPicker3.
        // The factory routes it to MediaPickerResolver, which returns ImageObject(s) —
        // the stringList path must reduce each to its URL string (the legacy
        // TryExtractMediaUrl behaviour), not silently drop the value.
        HermeticStaticServiceProvider.EnsureInstalled();

        var media = Substitute.For<IPublishedContent>();
        var urlProvider = Substitute.For<IPublishedUrlProvider>();
        urlProvider
            .GetMediaUrl(media, UrlMode.Absolute, Arg.Any<string?>(), Arg.Any<string>(), Arg.Any<Uri?>())
            .Returns("https://example.com/media/photo.jpg");
        var mediaWithCrops = new MediaWithCrops(
            media, Substitute.For<IPublishedValueFallback>(), new ImageCropperValue());

        var block = CreateBlockElementWithEditors("galleryBlock",
            ("photo", mediaWithCrops, "Umbraco.MediaPicker3"));
        var blockListModel = CreateBlockListModel(block);

        var resolverConfig = JsonSerializer.Serialize(new { extractAs = "stringList", contentProperty = "photo" });

        var property = Substitute.For<IPublishedProperty>();
        property.GetValue(Arg.Any<string?>(), Arg.Any<string?>()).Returns(blockListModel);

        var factory = new PropertyValueResolverFactory([
            new MediaPickerResolver(NullLogger<MediaPickerResolver>.Instance, urlProvider),
            new DefaultPropertyValueResolver()
        ]);
        var context = CreateContext(property, resolverConfig: resolverConfig, resolverFactory: factory);

        var result = _sut.Resolve(context);

        var strings = result.Should().BeOfType<List<string>>().Subject;
        strings.Should().Equal("https://example.com/media/photo.jpg");
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

    [Fact]
    public void Resolve_ReviewWithWrapInPersonName_PersonContainsName()
    {
        var blockElement = CreateBlockElement("reviewItem", new Dictionary<string, object?>
        {
            ["reviewAuthor"] = "Alice Smith",
            ["ratingValue"] = "5",
            ["reviewBody"] = "Excellent product, highly recommended."
        });
        var blockListModel = CreateBlockListModel(blockElement);

        var resolverConfig = JsonSerializer.Serialize(new ResolverConfigModel
        {
            NestedMappings = new List<NestedPropertyMapping>
            {
                new()
                {
                    SchemaProperty = "author",
                    ContentProperty = "reviewAuthor",
                    WrapInType = "Person",
                    WrapInProperty = "Name"
                },
                new()
                {
                    SchemaProperty = "reviewBody",
                    ContentProperty = "reviewBody"
                }
            }
        });

        var property = Substitute.For<IPublishedProperty>();
        property.GetValue(Arg.Any<string?>(), Arg.Any<string?>()).Returns(blockListModel);

        var context = CreateContext(property, nestedSchemaTypeName: "Review", resolverConfig: resolverConfig);

        var result = _sut.Resolve(context);

        result.Should().NotBeNull();
        var things = ((IEnumerable<Schema.NET.Thing>)result!).ToList();
        things.Should().HaveCount(1);

        var review = (Schema.NET.Review)things[0];
        var jsonLd = review.ToString();
        jsonLd.Should().Contain("Person");
        jsonLd.Should().Contain("Alice Smith");
        jsonLd.Should().Contain("Excellent product, highly recommended.");
    }

    [Fact]
    public void Resolve_WrapInType_EmptyValue_DoesNotCreateEmptyWrapper()
    {
        var blockElement = CreateBlockElement("reviewItem", new Dictionary<string, object?>
        {
            ["reviewAuthor"] = null,
            ["reviewBody"] = "Good product."
        });
        var blockListModel = CreateBlockListModel(blockElement);

        var resolverConfig = JsonSerializer.Serialize(new ResolverConfigModel
        {
            NestedMappings = new List<NestedPropertyMapping>
            {
                new()
                {
                    SchemaProperty = "author",
                    ContentProperty = "reviewAuthor",
                    WrapInType = "Person",
                    WrapInProperty = "Name"
                },
                new()
                {
                    SchemaProperty = "reviewBody",
                    ContentProperty = "reviewBody"
                }
            }
        });

        var property = Substitute.For<IPublishedProperty>();
        property.GetValue(Arg.Any<string?>(), Arg.Any<string?>()).Returns(blockListModel);

        var context = CreateContext(property, nestedSchemaTypeName: "Review", resolverConfig: resolverConfig);

        var result = _sut.Resolve(context);

        result.Should().NotBeNull();
        var things = ((IEnumerable<Schema.NET.Thing>)result!).ToList();
        things.Should().HaveCount(1);

        var review = (Schema.NET.Review)things[0];
        var jsonLd = review.ToString();
        // Should contain the review body but NOT an empty Person wrapper
        jsonLd.Should().Contain("Good product.");
        jsonLd.Should().NotContain("\"author\"");
    }

    [Fact]
    public void Resolve_NullResolverConfig_UsesAutoMap()
    {
        // When resolverConfig is explicitly null, the resolver should fall back to auto-mapping
        // by matching block element property names to schema property names.
        // Auto-mapping uses PascalCase schema property names (from reflection), so the
        // block element property must match that casing for NSubstitute's GetProperty mock.
        var blockElement = CreateBlockElement("faqItem", new Dictionary<string, object?>
        {
            ["Name"] = "Auto-mapped question"
        });
        var blockListModel = CreateBlockListModel(blockElement);

        var property = Substitute.For<IPublishedProperty>();
        property.GetValue(Arg.Any<string?>(), Arg.Any<string?>()).Returns(blockListModel);

        // Explicitly pass null resolverConfig
        var context = CreateContext(property, nestedSchemaTypeName: "Question", resolverConfig: null);

        var result = _sut.Resolve(context);

        result.Should().NotBeNull();
        result.Should().BeAssignableTo<IEnumerable<Schema.NET.Thing>>();
        var things = ((IEnumerable<Schema.NET.Thing>)result!).ToList();
        things.Should().HaveCount(1);
        things[0].Should().BeOfType<Schema.NET.Question>();

        var question = (Schema.NET.Question)things[0];
        var jsonLd = question.ToString();
        jsonLd.Should().Contain("Auto-mapped question");
    }

    [Fact]
    public void Resolve_BlockGridModel_ReturnsMappedThings()
    {
        // BlockGridModel should be handled the same as BlockListModel.
        // Auto-mapping uses PascalCase schema property names (from reflection), so the
        // block element property must match that casing for NSubstitute's GetProperty mock.
        var blockElement = CreateBlockElement("faqItem", new Dictionary<string, object?>
        {
            ["Name"] = "Grid question"
        });
        var blockGridModel = CreateBlockGridModel(blockElement);

        var property = Substitute.For<IPublishedProperty>();
        property.GetValue(Arg.Any<string?>(), Arg.Any<string?>()).Returns(blockGridModel);

        var context = CreateContext(property, nestedSchemaTypeName: "Question");

        var result = _sut.Resolve(context);

        result.Should().NotBeNull();
        result.Should().BeAssignableTo<IEnumerable<Schema.NET.Thing>>();
        var things = ((IEnumerable<Schema.NET.Thing>)result!).ToList();
        things.Should().HaveCount(1);
        things[0].Should().BeOfType<Schema.NET.Question>();

        var question = (Schema.NET.Question)things[0];
        var jsonLd = question.ToString();
        jsonLd.Should().Contain("Grid question");
    }

    // Regression: Map Block → Place.geo via GeoCoordinates. Two sub-mappings
    // (lat → latitude, lng → longitude) both target the same wrapper; prior to
    // the grouping fix in BlockContentResolver.MapBlockToThing, each mapping
    // recreated the wrapper so only the last sub-property survived.
    [Fact]
    public void Resolve_MapBlockToPlace_WithLatAndLngWrappedInGeoCoordinates_SetsBothOnSameInstance()
    {
        var blockElement = CreateBlockElement("mapBlock", new Dictionary<string, object?>
        {
            ["lat"] = 53.8008m,
            ["lng"] = -1.5491m,
        });
        var blockListModel = CreateBlockListModel(blockElement);

        var resolverConfig = JsonSerializer.Serialize(new ResolverConfigModel
        {
            NestedMappings = new List<NestedPropertyMapping>
            {
                new()
                {
                    BlockAlias = "mapBlock",
                    SchemaProperty = "Geo",
                    ContentProperty = "lat",
                    WrapInType = "GeoCoordinates",
                    WrapInProperty = "Latitude"
                },
                new()
                {
                    BlockAlias = "mapBlock",
                    SchemaProperty = "Geo",
                    ContentProperty = "lng",
                    WrapInType = "GeoCoordinates",
                    WrapInProperty = "Longitude"
                }
            }
        });

        var property = Substitute.For<IPublishedProperty>();
        property.GetValue(Arg.Any<string?>(), Arg.Any<string?>()).Returns(blockListModel);

        var context = CreateContext(property, nestedSchemaTypeName: "Place", resolverConfig: resolverConfig);

        var result = _sut.Resolve(context);

        result.Should().NotBeNull();
        var things = ((IEnumerable<Schema.NET.Thing>)result!).ToList();
        things.Should().HaveCount(1);

        var place = things[0].Should().BeOfType<Schema.NET.Place>().Subject;
        // Place.Geo is Values<OneOrMany<IGeoCoordinates>, OneOrMany<IGeoShape>>:
        // Value1 yields the IGeoCoordinates side, then Single() unwraps the
        // single coordinate instance inside the OneOrMany.
        var coords = ((IEnumerable<Schema.NET.IGeoCoordinates>)place.Geo.Value1!)
            .Single()
            .Should().BeOfType<Schema.NET.GeoCoordinates>().Subject;
        coords.Latitude.HasValue.Should().BeTrue("latitude should survive when longitude also maps to geo");
        coords.Longitude.HasValue.Should().BeTrue("longitude should survive when latitude also maps to geo");
    }

    // Regression guard: grouping by (SchemaProperty, WrapInType) must not collapse
    // unrelated wrappers targeting different schema properties.
    [Fact]
    public void Resolve_TwoBlockProperties_WrappedInDifferentTypes_KeepsEachOnItsOwnSchemaProperty()
    {
        var blockElement = CreateBlockElement("mapBlock", new Dictionary<string, object?>
        {
            ["lat"] = 53.8008m,
            ["markerText"] = "Leeds office"
        });
        var blockListModel = CreateBlockListModel(blockElement);

        var resolverConfig = JsonSerializer.Serialize(new ResolverConfigModel
        {
            NestedMappings = new List<NestedPropertyMapping>
            {
                new()
                {
                    BlockAlias = "mapBlock",
                    SchemaProperty = "Geo",
                    ContentProperty = "lat",
                    WrapInType = "GeoCoordinates",
                    WrapInProperty = "Latitude"
                },
                new()
                {
                    BlockAlias = "mapBlock",
                    SchemaProperty = "Name",
                    ContentProperty = "markerText"
                }
            }
        });

        var property = Substitute.For<IPublishedProperty>();
        property.GetValue(Arg.Any<string?>(), Arg.Any<string?>()).Returns(blockListModel);

        var context = CreateContext(property, nestedSchemaTypeName: "Place", resolverConfig: resolverConfig);

        var result = _sut.Resolve(context);

        var place = ((IEnumerable<Schema.NET.Thing>)result!).Cast<Schema.NET.Place>().Single();
        place.Name.First().Should().Be("Leeds office");
        ((Schema.NET.GeoCoordinates)place.Geo.Value1!).Latitude.HasValue.Should().BeTrue();
    }

    // If every sub-mapping resolves to null the resolver emits no empty wrapper — and, since
    // the parent Thing then has no resolved property at all, P2.1 drops the parent entirely.
    [Fact]
    public void Resolve_MultipleWrappedSubMappings_AllNullValues_DoesNotEmitEmptyWrapper()
    {
        var blockElement = CreateBlockElement("mapBlock", new Dictionary<string, object?>
        {
            ["lat"] = null,
            ["lng"] = null,
        });
        var blockListModel = CreateBlockListModel(blockElement);

        var resolverConfig = JsonSerializer.Serialize(new ResolverConfigModel
        {
            NestedMappings = new List<NestedPropertyMapping>
            {
                new() { BlockAlias = "mapBlock", SchemaProperty = "Geo", ContentProperty = "lat",
                    WrapInType = "GeoCoordinates", WrapInProperty = "Latitude" },
                new() { BlockAlias = "mapBlock", SchemaProperty = "Geo", ContentProperty = "lng",
                    WrapInType = "GeoCoordinates", WrapInProperty = "Longitude" },
            }
        });

        var property = Substitute.For<IPublishedProperty>();
        property.GetValue(Arg.Any<string?>(), Arg.Any<string?>()).Returns(blockListModel);

        var context = CreateContext(property, nestedSchemaTypeName: "Place", resolverConfig: resolverConfig);

        var result = _sut.Resolve(context);

        // P2.1: with no resolved properties the Place itself is dropped, so the whole block list
        // yields nothing — an even better outcome than an empty Place with an empty GeoCoordinates.
        result.Should().BeNull("a nested Thing that resolves no properties must not be emitted");
    }

    // --- WS3/WS4: per-block-type routes ---

    // New routed form: a heterogeneous block list (hero + team + map) feeding a single
    // property mapping. Each block element type resolves via its own route to a DIFFERENT
    // Schema.org type. Blocks with no matching route are skipped (not aborted).
    [Fact]
    public void Resolve_RoutedConfig_HeterogeneousBlocks_ProduceDifferentTypedThings()
    {
        var hero = CreateBlockElement("heroBlock", new Dictionary<string, object?>
        {
            ["heading"] = "Welcome"
        });
        var team = CreateBlockElement("teamBlock", new Dictionary<string, object?>
        {
            ["personName"] = "Ada Lovelace"
        });
        var map = CreateBlockElement("mapBlock", new Dictionary<string, object?>
        {
            ["placeName"] = "Leeds Office"
        });
        // A block element type with no route — must be skipped, not abort the list.
        var unmapped = CreateBlockElement("richTextBlock", new Dictionary<string, object?>
        {
            ["body"] = "Some prose"
        });
        var blockListModel = CreateBlockListModel(hero, team, map, unmapped);

        var resolverConfig = JsonSerializer.Serialize(new ResolverConfigModel
        {
            Routes = new List<BlockRoute>
            {
                new()
                {
                    BlockAlias = "heroBlock",
                    NestedSchemaType = "WPHeader",
                    PropertyMappings = new List<NestedPropertyMapping>
                    {
                        new() { SchemaProperty = "name", ContentProperty = "heading" }
                    }
                },
                new()
                {
                    BlockAlias = "teamBlock",
                    NestedSchemaType = "Person",
                    PropertyMappings = new List<NestedPropertyMapping>
                    {
                        new() { SchemaProperty = "name", ContentProperty = "personName" }
                    }
                },
                new()
                {
                    BlockAlias = "mapBlock",
                    NestedSchemaType = "Place",
                    PropertyMappings = new List<NestedPropertyMapping>
                    {
                        new() { SchemaProperty = "name", ContentProperty = "placeName" }
                    }
                }
            }
        });

        var property = Substitute.For<IPublishedProperty>();
        property.GetValue(Arg.Any<string?>(), Arg.Any<string?>()).Returns(blockListModel);

        // Routes present — the mapping-level NestedSchemaTypeName is deliberately null and
        // must NOT abort resolution.
        var context = CreateContext(property, nestedSchemaTypeName: null, resolverConfig: resolverConfig);

        var result = _sut.Resolve(context);

        result.Should().NotBeNull();
        var things = ((IEnumerable<Schema.NET.Thing>)result!).ToList();
        things.Should().HaveCount(3, "the unmapped richTextBlock is skipped, not aborted");
        things.Should().ContainSingle(t => t is Schema.NET.WPHeader);
        things.Should().ContainSingle(t => t is Schema.NET.Person);
        things.Should().ContainSingle(t => t is Schema.NET.Place);

        ((Schema.NET.Person)things.Single(t => t is Schema.NET.Person)).Name.First()
            .Should().Be("Ada Lovelace");
    }

    // Routes present but NestedSchemaTypeName missing must NOT abort (regression of the
    // old silent-failure mode that returned null whenever NestedSchemaTypeName was empty).
    [Fact]
    public void Resolve_RoutedConfig_NoMappingLevelNestedSchemaType_StillResolves()
    {
        var faq = CreateBlockElement("faqBlock", new Dictionary<string, object?>
        {
            ["question"] = "What is this?"
        });
        var blockListModel = CreateBlockListModel(faq);

        var resolverConfig = JsonSerializer.Serialize(new ResolverConfigModel
        {
            Routes = new List<BlockRoute>
            {
                new()
                {
                    BlockAlias = "faqBlock",
                    NestedSchemaType = "Question",
                    PropertyMappings = new List<NestedPropertyMapping>
                    {
                        new() { SchemaProperty = "name", ContentProperty = "question" }
                    }
                }
            }
        });

        var property = Substitute.For<IPublishedProperty>();
        property.GetValue(Arg.Any<string?>(), Arg.Any<string?>()).Returns(blockListModel);

        var context = CreateContext(property, nestedSchemaTypeName: null, resolverConfig: resolverConfig);

        var result = _sut.Resolve(context);

        result.Should().NotBeNull();
        var things = ((IEnumerable<Schema.NET.Thing>)result!).ToList();
        things.Should().ContainSingle();
        things[0].Should().BeOfType<Schema.NET.Question>();
    }

    // Routed form where a route's blockAlias matches NONE of the blocks → that block is
    // skipped; if no block matches any route the resolver returns null (empty output).
    [Fact]
    public void Resolve_RoutedConfig_NoBlockMatchesAnyRoute_ReturnsNull()
    {
        var block = CreateBlockElement("unknownBlock", new Dictionary<string, object?>
        {
            ["foo"] = "bar"
        });
        var blockListModel = CreateBlockListModel(block);

        var resolverConfig = JsonSerializer.Serialize(new ResolverConfigModel
        {
            Routes = new List<BlockRoute>
            {
                new()
                {
                    BlockAlias = "faqBlock",
                    NestedSchemaType = "Question",
                    PropertyMappings = new List<NestedPropertyMapping>
                    {
                        new() { SchemaProperty = "name", ContentProperty = "question" }
                    }
                }
            }
        });

        var property = Substitute.For<IPublishedProperty>();
        property.GetValue(Arg.Any<string?>(), Arg.Any<string?>()).Returns(blockListModel);

        var context = CreateContext(property, nestedSchemaTypeName: null, resolverConfig: resolverConfig);

        var result = _sut.Resolve(context);

        result.Should().BeNull();
    }

    // Back-compat: the legacy flat NestedMappings + mapping-level NestedSchemaTypeName
    // shape must still parse and resolve as a single implicit route.
    [Fact]
    public void Resolve_LegacyFlatConfig_StillResolvesAsSingleImplicitRoute()
    {
        var blockElement = CreateBlockElement("faqItem", new Dictionary<string, object?>
        {
            ["questionText"] = "Legacy still works?"
        });
        var blockListModel = CreateBlockListModel(blockElement);

        var resolverConfig = JsonSerializer.Serialize(new ResolverConfigModel
        {
            NestedMappings = new List<NestedPropertyMapping>
            {
                new() { BlockAlias = "faqItem", SchemaProperty = "name", ContentProperty = "questionText" }
            }
        });

        var property = Substitute.For<IPublishedProperty>();
        property.GetValue(Arg.Any<string?>(), Arg.Any<string?>()).Returns(blockListModel);

        var context = CreateContext(property, nestedSchemaTypeName: "Question", resolverConfig: resolverConfig);

        var result = _sut.Resolve(context);

        result.Should().NotBeNull();
        var things = ((IEnumerable<Schema.NET.Thing>)result!).ToList();
        things.Should().ContainSingle();
        things[0].Should().BeOfType<Schema.NET.Question>();
        ((Schema.NET.Question)things[0]).Name.First().Should().Be("Legacy still works?");
    }

    // --- WS-A: nested blocks (block-editor inside a block) + Block Grid areas ---

    // A block element whose own property is itself a Block List (a block nested inside a
    // block). The outer route maps the nested-block property to a schema property and carries
    // child routes; resolution must recurse and emit the inner Things.
    [Fact]
    public void Resolve_NestedBlockEditorProperty_RecursesToInnerThings()
    {
        var innerFaq = CreateBlockElement("faqItem", new Dictionary<string, object?>
        {
            ["question"] = "Nested question?"
        });
        var innerList = CreateBlockListModel(innerFaq);

        var section = CreateBlockElementWithNestedBlock(
            "sectionBlock", "items", "Umbraco.BlockList", innerList);

        var outerList = CreateBlockListModel(section);

        var resolverConfig = JsonSerializer.Serialize(new ResolverConfigModel
        {
            Routes = new List<BlockRoute>
            {
                new()
                {
                    BlockAlias = "sectionBlock",
                    NestedSchemaType = "FAQPage",
                    PropertyMappings = new List<NestedPropertyMapping>
                    {
                        new()
                        {
                            SchemaProperty = "mainEntity",
                            ContentProperty = "items",
                            Routes = new List<BlockRoute>
                            {
                                new()
                                {
                                    BlockAlias = "faqItem",
                                    NestedSchemaType = "Question",
                                    PropertyMappings = new List<NestedPropertyMapping>
                                    {
                                        new() { SchemaProperty = "name", ContentProperty = "question" }
                                    }
                                }
                            }
                        }
                    }
                }
            }
        });

        var property = Substitute.For<IPublishedProperty>();
        property.GetValue(Arg.Any<string?>(), Arg.Any<string?>()).Returns(outerList);

        var context = CreateContext(property, nestedSchemaTypeName: null, resolverConfig: resolverConfig);

        var result = _sut.Resolve(context);

        result.Should().NotBeNull();
        var things = ((IEnumerable<Schema.NET.Thing>)result!).ToList();
        things.Should().ContainSingle();
        var faqPage = things[0].Should().BeOfType<Schema.NET.FAQPage>().Subject;

        var jsonLd = faqPage.ToString();
        jsonLd.Should().Contain("Question");
        jsonLd.Should().Contain("Nested question?");
    }

    // Block Grid stores nested blocks inside layout Areas. A block placed in an area must
    // resolve just like a top-level grid block (areas are flattened, not dropped).
    [Fact]
    public void Resolve_BlockGridAreas_TraversesNestedAreaBlocks()
    {
        var topFaq = CreateBlockElement("faqItem", new Dictionary<string, object?>
        {
            ["question"] = "Top-level grid question"
        });
        var areaFaq = CreateBlockElement("faqItem", new Dictionary<string, object?>
        {
            ["question"] = "Area-nested grid question"
        });

        var areaItem = new BlockGridItem(Guid.NewGuid(), areaFaq, null, null);
        var area = new BlockGridArea(new List<BlockGridItem> { areaItem }, "main", 1, 1);
        var topItem = new BlockGridItem(Guid.NewGuid(), topFaq, null, null) { Areas = new[] { area } };
        var grid = new BlockGridModel(new List<BlockGridItem> { topItem }, 12);

        var resolverConfig = JsonSerializer.Serialize(new ResolverConfigModel
        {
            Routes = new List<BlockRoute>
            {
                new()
                {
                    BlockAlias = "faqItem",
                    NestedSchemaType = "Question",
                    PropertyMappings = new List<NestedPropertyMapping>
                    {
                        new() { SchemaProperty = "name", ContentProperty = "question" }
                    }
                }
            }
        });

        var property = Substitute.For<IPublishedProperty>();
        property.GetValue(Arg.Any<string?>(), Arg.Any<string?>()).Returns(grid);

        var context = CreateContext(property, nestedSchemaTypeName: null, resolverConfig: resolverConfig);

        var result = _sut.Resolve(context);

        result.Should().NotBeNull();
        var things = ((IEnumerable<Schema.NET.Thing>)result!).ToList();
        things.Should().HaveCount(2, "the top-level grid block AND the area-nested block both resolve");
        things.Should().AllBeOfType<Schema.NET.Question>();
        var names = things.Cast<Schema.NET.Question>().SelectMany(q => q.Name).ToList();
        names.Should().Contain("Top-level grid question");
        names.Should().Contain("Area-nested grid question");
    }

    // The nested property is skipped once recursion would exceed MaxRecursionDepth. Since the
    // FAQPage's only mapping is that nested property, the outer Thing ends up with no resolved
    // property and P2.1 drops it — the nested content certainly never appears.
    [Fact]
    public void Resolve_NestedBlockEditorProperty_DepthCapped_DoesNotRecurse()
    {
        var innerFaq = CreateBlockElement("faqItem", new Dictionary<string, object?>
        {
            ["question"] = "Should not appear"
        });
        var innerList = CreateBlockListModel(innerFaq);
        var section = CreateBlockElementWithNestedBlock(
            "sectionBlock", "items", "Umbraco.BlockList", innerList);
        var outerList = CreateBlockListModel(section);

        var resolverConfig = JsonSerializer.Serialize(new ResolverConfigModel
        {
            Routes = new List<BlockRoute>
            {
                new()
                {
                    BlockAlias = "sectionBlock",
                    NestedSchemaType = "FAQPage",
                    PropertyMappings = new List<NestedPropertyMapping>
                    {
                        new()
                        {
                            SchemaProperty = "mainEntity",
                            ContentProperty = "items",
                            Routes = new List<BlockRoute>
                            {
                                new()
                                {
                                    BlockAlias = "faqItem",
                                    NestedSchemaType = "Question",
                                    PropertyMappings = new List<NestedPropertyMapping>
                                    {
                                        new() { SchemaProperty = "name", ContentProperty = "question" }
                                    }
                                }
                            }
                        }
                    }
                }
            }
        });

        var property = Substitute.For<IPublishedProperty>();
        property.GetValue(Arg.Any<string?>(), Arg.Any<string?>()).Returns(outerList);

        // Start one below the cap: resolving the section is allowed (depth 2 < 3), but
        // recursing into its nested "items" (depth 3) is not.
        var context = CreateContext(property, nestedSchemaTypeName: null,
            resolverConfig: resolverConfig, recursionDepth: 2);

        var result = _sut.Resolve(context);

        // P2.1: the depth cap nulls the FAQPage's only mapping (mainEntity), leaving it with no
        // resolved property, so the now-empty FAQPage is dropped entirely.
        result.Should().BeNull("a depth-capped FAQPage with no other resolved property is dropped");
    }

    // A block whose nested Block List contains the same block element (a cycle) must
    // terminate via the VisitedContentKeys guard rather than recurse forever.
    [Fact]
    public void Resolve_NestedBlockEditorProperty_SelfReference_Terminates()
    {
        var section = Substitute.For<IPublishedElement>();
        var contentType = Substitute.For<IPublishedContentType>();
        contentType.Alias.Returns("sectionBlock");
        section.ContentType.Returns(contentType);
        section.Key.Returns(Guid.NewGuid());

        // The section's nested "items" Block List references the section itself.
        var selfList = CreateBlockListModel(section);
        var itemsProp = Substitute.For<IPublishedProperty>();
        var itemsPropType = Substitute.For<IPublishedPropertyType>();
        itemsPropType.EditorAlias.Returns("Umbraco.BlockList");
        itemsProp.PropertyType.Returns(itemsPropType);
        itemsProp.Alias.Returns("items");
        itemsProp.GetValue(Arg.Any<string?>(), Arg.Any<string?>()).Returns(selfList);
        section.GetProperty("items").Returns(itemsProp);

        // A scalar property so the outer FAQPage keeps one resolved property (name) even though
        // the self-referential mainEntity is cycle-nulled — keeps the test about cycle
        // termination, not P2.1's empty-Thing drop.
        var headingProp = Substitute.For<IPublishedProperty>();
        headingProp.GetValue(Arg.Any<string?>(), Arg.Any<string?>()).Returns("Our FAQs");
        section.GetProperty("heading").Returns(headingProp);

        var outerList = CreateBlockListModel(section);

        var resolverConfig = JsonSerializer.Serialize(new ResolverConfigModel
        {
            Routes = new List<BlockRoute>
            {
                new()
                {
                    BlockAlias = "sectionBlock",
                    NestedSchemaType = "FAQPage",
                    PropertyMappings = new List<NestedPropertyMapping>
                    {
                        new() { SchemaProperty = "name", ContentProperty = "heading" },
                        new()
                        {
                            SchemaProperty = "mainEntity",
                            ContentProperty = "items",
                            Routes = new List<BlockRoute>
                            {
                                new() { BlockAlias = "sectionBlock", NestedSchemaType = "FAQPage" }
                            }
                        }
                    }
                }
            }
        });

        var property = Substitute.For<IPublishedProperty>();
        property.GetValue(Arg.Any<string?>(), Arg.Any<string?>()).Returns(outerList);

        var context = CreateContext(property, nestedSchemaTypeName: null, resolverConfig: resolverConfig);

        var act = () => _sut.Resolve(context);

        act.Should().NotThrow("the cycle guard must stop self-referential block recursion");
        var things = ((IEnumerable<Schema.NET.Thing>)act()!).ToList();
        things.Should().ContainSingle();
        things[0].Should().BeOfType<Schema.NET.FAQPage>();
    }

    // --- P2.1: drop empty / degenerate nested Things ---

    [Fact]
    public void Resolve_RoutedConfig_OnePopulatedOneBlank_EmitsSingleThing()
    {
        var populated = CreateBlockElement("faqItem", new Dictionary<string, object?>
        {
            ["questionText"] = "What is SchemeWeaver?"
        });
        var blank = CreateBlockElement("faqItem", new Dictionary<string, object?>
        {
            ["questionText"] = null
        });
        var blockListModel = CreateBlockListModel(populated, blank);

        var resolverConfig = JsonSerializer.Serialize(new ResolverConfigModel
        {
            Routes = new List<BlockRoute>
            {
                new()
                {
                    BlockAlias = "faqItem",
                    NestedSchemaType = "Question",
                    PropertyMappings = new List<NestedPropertyMapping>
                    {
                        new() { SchemaProperty = "name", ContentProperty = "questionText" }
                    }
                }
            }
        });

        var property = Substitute.For<IPublishedProperty>();
        property.GetValue(Arg.Any<string?>(), Arg.Any<string?>()).Returns(blockListModel);

        var context = CreateContext(property, resolverConfig: resolverConfig);

        var result = _sut.Resolve(context);

        var things = ((IEnumerable<Schema.NET.Thing>)result!).ToList();
        things.Should().ContainSingle("the blank faqItem resolves no properties and is dropped");
        things[0].Should().BeOfType<Schema.NET.Question>();
        ((Schema.NET.Question)things[0]).Name.First().Should().Be("What is SchemeWeaver?");
    }

    [Fact]
    public void Resolve_LegacyConfig_BlankBlock_IsDropped()
    {
        var blank = CreateBlockElement("faqItem", new Dictionary<string, object?>
        {
            ["name"] = null
        });
        var blockListModel = CreateBlockListModel(blank);

        var property = Substitute.For<IPublishedProperty>();
        property.GetValue(Arg.Any<string?>(), Arg.Any<string?>()).Returns(blockListModel);

        // Auto-map path (no config): the blank block resolves nothing → dropped → empty result.
        var context = CreateContext(property, nestedSchemaTypeName: "Question");

        var result = _sut.Resolve(context);

        result.Should().BeNull("a block that resolves no properties must not emit an empty Thing");
    }

    [Fact]
    public void Resolve_RoutedConfig_RequiredPropertyMissing_DropsThing()
    {
        // The block resolves `name` but not the configured-required `acceptedAnswer`.
        var block = CreateBlockElement("faqItem", new Dictionary<string, object?>
        {
            ["questionText"] = "Why?"
        });
        var blockListModel = CreateBlockListModel(block);

        var resolverConfig = JsonSerializer.Serialize(new ResolverConfigModel
        {
            Routes = new List<BlockRoute>
            {
                new()
                {
                    BlockAlias = "faqItem",
                    NestedSchemaType = "Question",
                    RequiredProperties = new List<string> { "acceptedAnswer" },
                    PropertyMappings = new List<NestedPropertyMapping>
                    {
                        new() { SchemaProperty = "name", ContentProperty = "questionText" }
                    }
                }
            }
        });

        var property = Substitute.For<IPublishedProperty>();
        property.GetValue(Arg.Any<string?>(), Arg.Any<string?>()).Returns(blockListModel);

        var context = CreateContext(property, resolverConfig: resolverConfig);

        var result = _sut.Resolve(context);

        result.Should().BeNull("acceptedAnswer is required but did not resolve, so the Question is dropped");
    }

    // --- P2.2: transforms on nested property mappings ---

    [Fact]
    public void Resolve_NestedMapping_StripHtml_EmitsPlainText()
    {
        var block = CreateBlockElement("faqItem", new Dictionary<string, object?>
        {
            ["answerHtml"] = "<p>Because <strong>schema</strong>.</p>"
        });
        var blockListModel = CreateBlockListModel(block);

        var resolverConfig = JsonSerializer.Serialize(new ResolverConfigModel
        {
            Routes = new List<BlockRoute>
            {
                new()
                {
                    BlockAlias = "faqItem",
                    NestedSchemaType = "Question",
                    PropertyMappings = new List<NestedPropertyMapping>
                    {
                        new() { SchemaProperty = "name", ContentProperty = "answerHtml", TransformType = "stripHtml" }
                    }
                }
            }
        });

        var property = Substitute.For<IPublishedProperty>();
        property.GetValue(Arg.Any<string?>(), Arg.Any<string?>()).Returns(blockListModel);

        var context = CreateContext(property, resolverConfig: resolverConfig);

        var result = _sut.Resolve(context);

        var question = ((IEnumerable<Schema.NET.Thing>)result!).Cast<Schema.NET.Question>().Single();
        question.Name.First().Should().Be("Because schema.");
    }

    [Fact]
    public void Resolve_WrappedGroup_StripHtml_Applies()
    {
        var block = CreateBlockElement("faqItem", new Dictionary<string, object?>
        {
            ["answerHtml"] = "<p>Plain answer.</p>"
        });
        var blockListModel = CreateBlockListModel(block);

        var resolverConfig = JsonSerializer.Serialize(new ResolverConfigModel
        {
            Routes = new List<BlockRoute>
            {
                new()
                {
                    BlockAlias = "faqItem",
                    NestedSchemaType = "Question",
                    PropertyMappings = new List<NestedPropertyMapping>
                    {
                        new()
                        {
                            SchemaProperty = "acceptedAnswer",
                            ContentProperty = "answerHtml",
                            WrapInType = "Answer",
                            WrapInProperty = "Text",
                            TransformType = "stripHtml"
                        }
                    }
                }
            }
        });

        var property = Substitute.For<IPublishedProperty>();
        property.GetValue(Arg.Any<string?>(), Arg.Any<string?>()).Returns(blockListModel);

        var context = CreateContext(property, resolverConfig: resolverConfig);

        var result = _sut.Resolve(context);

        var question = ((IEnumerable<Schema.NET.Thing>)result!).Cast<Schema.NET.Question>().Single();
        var answer = (Schema.NET.Answer)question.AcceptedAnswer.First()!;
        answer.Text.First().Should().Be("Plain answer.");
    }

    [Fact]
    public void Resolve_NestedMapping_ToAbsoluteUrl_UsesHttpContext()
    {
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Scheme = "https";
        httpContext.Request.Host = new HostString("example.com");
        _httpContextAccessor.HttpContext.Returns(httpContext);

        var block = CreateBlockElement("linkItem", new Dictionary<string, object?>
        {
            ["link"] = "/about-us"
        });
        var blockListModel = CreateBlockListModel(block);

        var resolverConfig = JsonSerializer.Serialize(new ResolverConfigModel
        {
            Routes = new List<BlockRoute>
            {
                new()
                {
                    BlockAlias = "linkItem",
                    NestedSchemaType = "WebPage",
                    PropertyMappings = new List<NestedPropertyMapping>
                    {
                        new() { SchemaProperty = "url", ContentProperty = "link", TransformType = "toAbsoluteUrl" }
                    }
                }
            }
        });

        var property = Substitute.For<IPublishedProperty>();
        property.GetValue(Arg.Any<string?>(), Arg.Any<string?>()).Returns(blockListModel);

        var context = CreateContext(property, resolverConfig: resolverConfig);

        var result = _sut.Resolve(context);

        var page = ((IEnumerable<Schema.NET.Thing>)result!).Cast<Schema.NET.WebPage>().Single();
        page.Url.First().Should().Be(new Uri("https://example.com/about-us"));
    }

    // --- P2.3: opt-in ordered ItemList (ListItem + position) ---

    [Fact]
    public void Resolve_RoutedConfig_WrapInListItem_EmitsSequentialListItems()
    {
        var blockListModel = CreateBlockListModel(
            CreateBlockElement("serviceItem", new Dictionary<string, object?> { ["title"] = "Alpha" }),
            CreateBlockElement("serviceItem", new Dictionary<string, object?> { ["title"] = "Beta" }),
            CreateBlockElement("serviceItem", new Dictionary<string, object?> { ["title"] = "Gamma" }));

        var resolverConfig = JsonSerializer.Serialize(new ResolverConfigModel
        {
            WrapInListItem = true,
            Routes = new List<BlockRoute>
            {
                new()
                {
                    BlockAlias = "serviceItem",
                    NestedSchemaType = "Service",
                    PropertyMappings = new List<NestedPropertyMapping>
                    {
                        new() { SchemaProperty = "name", ContentProperty = "title" }
                    }
                }
            }
        });

        var property = Substitute.For<IPublishedProperty>();
        property.GetValue(Arg.Any<string?>(), Arg.Any<string?>()).Returns(blockListModel);

        var context = CreateContext(property, resolverConfig: resolverConfig);

        var result = _sut.Resolve(context);

        var items = ((IEnumerable<Schema.NET.Thing>)result!).Cast<Schema.NET.ListItem>().ToList();
        items.Should().HaveCount(3);
        items[0].Position.Value1.First().Should().Be(1);
        items[1].Position.Value1.First().Should().Be(2);
        items[2].Position.Value1.First().Should().Be(3);
        ((Schema.NET.Service)items[0].Item.First()!).Name.First().Should().Be("Alpha");
    }

    [Fact]
    public void Resolve_WrapInListItem_WithPositionProperty_UsesExplicitPositions()
    {
        var blockListModel = CreateBlockListModel(
            CreateBlockElement("serviceItem", new Dictionary<string, object?> { ["title"] = "Alpha", ["pos"] = "5" }),
            CreateBlockElement("serviceItem", new Dictionary<string, object?> { ["title"] = "Beta", ["pos"] = "2" }));

        var resolverConfig = JsonSerializer.Serialize(new ResolverConfigModel
        {
            WrapInListItem = true,
            PositionProperty = "pos",
            Routes = new List<BlockRoute>
            {
                new()
                {
                    BlockAlias = "serviceItem",
                    NestedSchemaType = "Service",
                    PropertyMappings = new List<NestedPropertyMapping>
                    {
                        new() { SchemaProperty = "name", ContentProperty = "title" }
                    }
                }
            }
        });

        var property = Substitute.For<IPublishedProperty>();
        property.GetValue(Arg.Any<string?>(), Arg.Any<string?>()).Returns(blockListModel);

        var context = CreateContext(property, resolverConfig: resolverConfig);

        var result = _sut.Resolve(context);

        var items = ((IEnumerable<Schema.NET.Thing>)result!).Cast<Schema.NET.ListItem>().ToList();
        items.Should().HaveCount(2);
        items[0].Position.Value1.First().Should().Be(5);
        items[1].Position.Value1.First().Should().Be(2);
    }

    [Fact]
    public void Resolve_WrapInListItem_BlankItemSkipped_PositionsStaySequential()
    {
        var blockListModel = CreateBlockListModel(
            CreateBlockElement("serviceItem", new Dictionary<string, object?> { ["title"] = "Alpha" }),
            CreateBlockElement("serviceItem", new Dictionary<string, object?> { ["title"] = null }),
            CreateBlockElement("serviceItem", new Dictionary<string, object?> { ["title"] = "Gamma" }));

        var resolverConfig = JsonSerializer.Serialize(new ResolverConfigModel
        {
            WrapInListItem = true,
            Routes = new List<BlockRoute>
            {
                new()
                {
                    BlockAlias = "serviceItem",
                    NestedSchemaType = "Service",
                    PropertyMappings = new List<NestedPropertyMapping>
                    {
                        new() { SchemaProperty = "name", ContentProperty = "title" }
                    }
                }
            }
        });

        var property = Substitute.For<IPublishedProperty>();
        property.GetValue(Arg.Any<string?>(), Arg.Any<string?>()).Returns(blockListModel);

        var context = CreateContext(property, resolverConfig: resolverConfig);

        var result = _sut.Resolve(context);

        var items = ((IEnumerable<Schema.NET.Thing>)result!).Cast<Schema.NET.ListItem>().ToList();
        items.Should().HaveCount(2, "the blank item is dropped (P2.1) before numbering");
        items[0].Position.Value1.First().Should().Be(1);
        items[1].Position.Value1.First().Should().Be(2);
        ((Schema.NET.Service)items[1].Item.First()!).Name.First().Should().Be("Gamma");
    }

    [Fact]
    public void Resolve_NoWrapInListItem_ReturnsBareThings()
    {
        var blockListModel = CreateBlockListModel(
            CreateBlockElement("serviceItem", new Dictionary<string, object?> { ["title"] = "Alpha" }));

        var resolverConfig = JsonSerializer.Serialize(new ResolverConfigModel
        {
            Routes = new List<BlockRoute>
            {
                new()
                {
                    BlockAlias = "serviceItem",
                    NestedSchemaType = "Service",
                    PropertyMappings = new List<NestedPropertyMapping>
                    {
                        new() { SchemaProperty = "name", ContentProperty = "title" }
                    }
                }
            }
        });

        var property = Substitute.For<IPublishedProperty>();
        property.GetValue(Arg.Any<string?>(), Arg.Any<string?>()).Returns(blockListModel);

        var context = CreateContext(property, resolverConfig: resolverConfig);

        var result = _sut.Resolve(context);

        var things = ((IEnumerable<Schema.NET.Thing>)result!).ToList();
        things.Should().ContainSingle();
        things[0].Should().BeOfType<Schema.NET.Service>("the default path emits bare Things, not ListItems");
    }

    // --- v3 §4: wrap/position config must propagate one nesting level deeper ---

    // Regression for the LS Services blocker: a block list nested INSIDE an ItemList stayed a bare
    // Service[] because ResolveNestedBlockProperty copied only Routes, dropping WrapInListItem.
    [Fact]
    public void Resolve_NestedRoute_WrapInListItem_EmitsSequentialListItems()
    {
        var innerList = CreateBlockListModel(
            CreateBlockElement("serviceItem", new Dictionary<string, object?> { ["title"] = "Alpha" }),
            CreateBlockElement("serviceItem", new Dictionary<string, object?> { ["title"] = "Beta" }),
            CreateBlockElement("serviceItem", new Dictionary<string, object?> { ["title"] = "Gamma" }));

        var section = CreateBlockElementWithNestedBlock(
            "servicesSection", "items", "Umbraco.BlockList", innerList);
        var outerList = CreateBlockListModel(section);

        var resolverConfig = JsonSerializer.Serialize(new ResolverConfigModel
        {
            Routes = new List<BlockRoute>
            {
                new()
                {
                    BlockAlias = "servicesSection",
                    NestedSchemaType = "ItemList",
                    PropertyMappings = new List<NestedPropertyMapping>
                    {
                        new()
                        {
                            SchemaProperty = "itemListElement",
                            ContentProperty = "items",
                            WrapInListItem = true,
                            Routes = new List<BlockRoute>
                            {
                                new()
                                {
                                    BlockAlias = "serviceItem",
                                    NestedSchemaType = "Service",
                                    PropertyMappings = new List<NestedPropertyMapping>
                                    {
                                        new() { SchemaProperty = "name", ContentProperty = "title" }
                                    }
                                }
                            }
                        }
                    }
                }
            }
        });

        var property = Substitute.For<IPublishedProperty>();
        property.GetValue(Arg.Any<string?>(), Arg.Any<string?>()).Returns(outerList);

        var context = CreateContext(property, resolverConfig: resolverConfig);

        var result = _sut.Resolve(context);

        var itemList = ((IEnumerable<Schema.NET.Thing>)result!).Cast<Schema.NET.ItemList>().Single();
        var items = itemList.ItemListElement.OfType<Schema.NET.ListItem>().ToList();
        items.Should().HaveCount(3, "the nested list must now wrap as ListItems, not stay bare Service[]");
        items[0].Position.Value1.First().Should().Be(1);
        items[1].Position.Value1.First().Should().Be(2);
        items[2].Position.Value1.First().Should().Be(3);
        ((Schema.NET.Service)items[0].Item.First()!).Name.First().Should().Be("Alpha");
    }

    [Fact]
    public void Resolve_NestedStringList_StripHtml_EmitsPlainText()
    {
        var innerList = CreateBlockListModel(
            CreateBlockElement("ingredientItem", new Dictionary<string, object?> { ["ingredient"] = "<p>200g <strong>flour</strong></p>" }),
            CreateBlockElement("ingredientItem", new Dictionary<string, object?> { ["ingredient"] = "<p>2 eggs</p>" }));

        var section = CreateBlockElementWithNestedBlock(
            "recipeSection", "ingredients", "Umbraco.BlockList", innerList);
        var outerList = CreateBlockListModel(section);

        var resolverConfig = JsonSerializer.Serialize(new ResolverConfigModel
        {
            Routes = new List<BlockRoute>
            {
                new()
                {
                    BlockAlias = "recipeSection",
                    NestedSchemaType = "Recipe",
                    PropertyMappings = new List<NestedPropertyMapping>
                    {
                        new()
                        {
                            SchemaProperty = "recipeIngredient",
                            ContentProperty = "ingredients",
                            ExtractAs = "stringList",
                            NestedContentProperty = "ingredient",
                            TransformType = "stripHtml"
                        }
                    }
                }
            }
        });

        var property = Substitute.For<IPublishedProperty>();
        property.GetValue(Arg.Any<string?>(), Arg.Any<string?>()).Returns(outerList);

        var context = CreateContext(property, resolverConfig: resolverConfig);

        var result = _sut.Resolve(context);

        var recipe = ((IEnumerable<Schema.NET.Thing>)result!).Cast<Schema.NET.Recipe>().Single();
        recipe.RecipeIngredient.Should().BeEquivalentTo("200g flour", "2 eggs");
    }

    [Fact]
    public void Resolve_StringListExtraction_StripHtml_EmitsPlainText()
    {
        var blockListModel = CreateBlockListModel(
            CreateBlockElement("ingredientItem", new Dictionary<string, object?> { ["ingredient"] = "<p>200g <strong>flour</strong></p>" }),
            CreateBlockElement("ingredientItem", new Dictionary<string, object?> { ["ingredient"] = "  <span>2 eggs</span>  " }));

        var resolverConfig = JsonSerializer.Serialize(new ResolverConfigModel
        {
            ExtractAs = "stringList",
            ContentProperty = "ingredient",
            TransformType = "stripHtml"
        });

        var property = Substitute.For<IPublishedProperty>();
        property.GetValue(Arg.Any<string?>(), Arg.Any<string?>()).Returns(blockListModel);

        var context = CreateContext(property, resolverConfig: resolverConfig);

        var result = _sut.Resolve(context);

        var list = ((IEnumerable<string>)result!).ToList();
        list.Should().BeEquivalentTo(new[] { "200g flour", "2 eggs" });
    }

    // --- Block element property routing through the resolver factory (media ImageObject seam) ---

    // A block media property must flow through the per-editor resolver pipeline (via the factory)
    // rather than the static raw-JSON helper. Here a stub resolver stands in for MediaPickerResolver
    // and returns a Schema.NET ImageObject; the routing must wire the BLOCK ELEMENT property into the
    // factory-resolved value and set it on the mapped block Thing.
    [Fact]
    public void ResolveBlockElementProperty_WithFactory_UsesFactoryResolver()
    {
        var block = CreateBlockElementWithEditors(
            "reviewItem",
            ("photo", "[{\"mediaKey\":\"abc\",\"umbracoFile\":\"/media/x.jpg\"}]", "Umbraco.MediaPicker3"));
        var blockListModel = CreateBlockListModel(block);

        var imageObject = new Schema.NET.ImageObject { Url = new Uri("https://cdn.example.com/photo.jpg") };

        // Stub resolver standing in for MediaPickerResolver — captures the child context so we can
        // assert the block element property (not the page property) was routed to it.
        PropertyResolverContext? captured = null;
        var mediaResolver = Substitute.For<IPropertyValueResolver>();
        mediaResolver
            .Resolve(Arg.Do<PropertyResolverContext>(c => captured = c))
            .Returns(imageObject);

        var factory = Substitute.For<IPropertyValueResolverFactory>();
        factory.GetResolver(Arg.Any<string?>()).Returns(mediaResolver);

        var resolverConfig = JsonSerializer.Serialize(new ResolverConfigModel
        {
            Routes = new List<BlockRoute>
            {
                new()
                {
                    BlockAlias = "reviewItem",
                    NestedSchemaType = "Article",
                    PropertyMappings = new List<NestedPropertyMapping>
                    {
                        new() { SchemaProperty = "image", ContentProperty = "photo" }
                    }
                }
            }
        });

        var property = Substitute.For<IPublishedProperty>();
        property.GetValue(Arg.Any<string?>(), Arg.Any<string?>()).Returns(blockListModel);

        var context = CreateContext(property, resolverConfig: resolverConfig, resolverFactory: factory);

        var result = _sut.Resolve(context);

        // Routing reached the factory with the block editor alias.
        factory.Received().GetResolver("Umbraco.MediaPicker3");

        // The child context carried the BLOCK ELEMENT property and kept the PAGE as Content.
        captured.Should().NotBeNull();
        captured!.Property!.Alias.Should().Be("photo");
        captured.Content.Should().BeSameAs(context.Content);
        captured.ResolverFactory.Should().BeSameAs(factory);

        // The factory-resolved ImageObject flowed onto the mapped Thing (i.e. SetPropertyValue ran).
        var article = ((IEnumerable<Schema.NET.Thing>)result!).Cast<Schema.NET.Article>().Single();
        var jsonLd = article.ToString();
        jsonLd.Should().Contain("ImageObject");
        jsonLd.Should().Contain("photo.jpg");
    }

    // With no factory the resolver keeps its original behaviour: the static helper resolves a scalar
    // text block property (existing tests exercise this implicitly; this pins it explicitly).
    [Fact]
    public void ResolveBlockElementProperty_NoFactory_FallsBackToStaticHelper()
    {
        var block = CreateBlockElement("faqItem", new Dictionary<string, object?>
        {
            ["questionText"] = "Static path still runs?"
        });
        var blockListModel = CreateBlockListModel(block);

        var resolverConfig = JsonSerializer.Serialize(new ResolverConfigModel
        {
            NestedMappings = new List<NestedPropertyMapping>
            {
                new() { BlockAlias = "faqItem", SchemaProperty = "name", ContentProperty = "questionText" }
            }
        });

        var property = Substitute.For<IPublishedProperty>();
        property.GetValue(Arg.Any<string?>(), Arg.Any<string?>()).Returns(blockListModel);

        // No resolverFactory supplied — ResolverFactory is null on the context.
        var context = CreateContext(property, nestedSchemaTypeName: "Question", resolverConfig: resolverConfig);
        context.ResolverFactory.Should().BeNull();

        var result = _sut.Resolve(context);

        var question = ((IEnumerable<Schema.NET.Thing>)result!).Cast<Schema.NET.Question>().Single();
        question.Name.First().Should().Be("Static path still runs?");
    }

    // Damaged media: the factory resolver returns null for the media property. The property must be
    // omitted (not fall back to raw MediaWithCrops JSON), while a sibling scalar keeps the Thing.
    [Fact]
    public void ResolveBlockElementProperty_FactoryResolvesNull_PropertyOmitted_NoRawJson()
    {
        var block = CreateBlockElementWithEditors(
            "reviewItem",
            ("reviewTitle", "A great stay", "Umbraco.TextBox"),
            ("photo", "[{\"mediaKey\":\"abc\",\"umbracoFile\":\"/media/x.jpg\",\"MediaWithCrops\":true}]", "Umbraco.MediaPicker3"));
        var blockListModel = CreateBlockListModel(block);

        // Media resolver returns null (damaged media); text resolver returns the scalar value.
        var mediaResolver = Substitute.For<IPropertyValueResolver>();
        mediaResolver.Resolve(Arg.Any<PropertyResolverContext>()).Returns((object?)null);

        var textResolver = Substitute.For<IPropertyValueResolver>();
        textResolver.Resolve(Arg.Any<PropertyResolverContext>()).Returns("A great stay");

        var factory = Substitute.For<IPropertyValueResolverFactory>();
        factory.GetResolver("Umbraco.MediaPicker3").Returns(mediaResolver);
        factory.GetResolver("Umbraco.TextBox").Returns(textResolver);

        var resolverConfig = JsonSerializer.Serialize(new ResolverConfigModel
        {
            Routes = new List<BlockRoute>
            {
                new()
                {
                    BlockAlias = "reviewItem",
                    NestedSchemaType = "Article",
                    PropertyMappings = new List<NestedPropertyMapping>
                    {
                        new() { SchemaProperty = "name", ContentProperty = "reviewTitle" },
                        new() { SchemaProperty = "image", ContentProperty = "photo" }
                    }
                }
            }
        });

        var property = Substitute.For<IPublishedProperty>();
        property.GetValue(Arg.Any<string?>(), Arg.Any<string?>()).Returns(blockListModel);

        var context = CreateContext(property, resolverConfig: resolverConfig, resolverFactory: factory);

        var result = _sut.Resolve(context);

        var article = ((IEnumerable<Schema.NET.Thing>)result!).Cast<Schema.NET.Article>().Single();
        var jsonLd = article.ToString();

        // The Thing survived on the scalar, but the damaged media property is omitted — and crucially
        // no raw MediaWithCrops / umbracoFile JSON leaked into the output.
        jsonLd.Should().Contain("A great stay");
        jsonLd.Should().NotContain("\"image\"");
        jsonLd.Should().NotContain("umbracoFile");
        jsonLd.Should().NotContain("MediaWithCrops");
    }

    private static IPublishedElement CreateBlockElementWithNestedBlock(
        string contentTypeAlias,
        string nestedPropertyAlias,
        string editorAlias,
        object nestedValue,
        Dictionary<string, object?>? scalarProperties = null)
    {
        var element = Substitute.For<IPublishedElement>();
        var contentType = Substitute.For<IPublishedContentType>();
        contentType.Alias.Returns(contentTypeAlias);
        element.ContentType.Returns(contentType);
        element.Key.Returns(Guid.NewGuid());

        var prop = Substitute.For<IPublishedProperty>();
        var propType = Substitute.For<IPublishedPropertyType>();
        propType.EditorAlias.Returns(editorAlias);
        prop.PropertyType.Returns(propType);
        prop.Alias.Returns(nestedPropertyAlias);
        prop.GetValue(Arg.Any<string?>(), Arg.Any<string?>()).Returns(nestedValue);
        element.GetProperty(nestedPropertyAlias).Returns(prop);

        if (scalarProperties is not null)
        {
            foreach (var kvp in scalarProperties)
            {
                var scalarProp = Substitute.For<IPublishedProperty>();
                scalarProp.GetValue(Arg.Any<string?>(), Arg.Any<string?>()).Returns(kvp.Value);
                element.GetProperty(kvp.Key).Returns(scalarProp);
            }
        }

        return element;
    }

    private static IPublishedElement CreateBlockElement(string contentTypeAlias, Dictionary<string, object?> properties)
    {
        var element = Substitute.For<IPublishedElement>();
        var contentType = Substitute.For<IPublishedContentType>();
        contentType.Alias.Returns(contentTypeAlias);
        element.ContentType.Returns(contentType);
        element.Key.Returns(Guid.NewGuid());

        var stubbedProps = new List<IPublishedProperty>();
        foreach (var kvp in properties)
        {
            var key = kvp.Key;
            var prop = Substitute.For<IPublishedProperty>();
            prop.GetValue(Arg.Any<string?>(), Arg.Any<string?>()).Returns(kvp.Value);
            prop.Alias.Returns(key);
            // Umbraco property aliases are case-insensitive — mirror that so auto-map (which
            // probes by PascalCase schema property name) resolves a lower-cased block key.
            element.GetProperty(Arg.Is<string>(a => string.Equals(a, key, StringComparison.OrdinalIgnoreCase)))
                .Returns(prop);
            stubbedProps.Add(prop);
        }

        // Basic text extraction walks element.Properties — without this stub NSubstitute
        // auto-returns an empty enumerable and the walk silently sees nothing.
        element.Properties.Returns(stubbedProps);

        return element;
    }

    private static BlockListModel CreateBlockListModel(params IPublishedElement[] elements)
    {
        var items = elements.Select(e =>
            new BlockListItem(Guid.NewGuid(), e, null, null))
            .ToList();
        return new BlockListModel(items);
    }

    private static BlockGridModel CreateBlockGridModel(params IPublishedElement[] elements)
    {
        var items = elements.Select(e =>
            new BlockGridItem(Guid.NewGuid(), e, null, null))
            .ToList();
        return new BlockGridModel(items, 12);
    }

    private PropertyResolverContext CreateContext(
        IPublishedProperty? property,
        string? nestedSchemaTypeName = null,
        string? resolverConfig = null,
        int recursionDepth = 0,
        IPropertyValueResolverFactory? resolverFactory = null,
        string sourceType = SchemeWeaverConstants.SourceTypes.Property)
    {
        return new PropertyResolverContext
        {
            Content = Substitute.For<IPublishedContent>(),
            Mapping = new PropertyMapping
            {
                SchemaPropertyName = "MainEntity",
                SourceType = sourceType,
                NestedSchemaTypeName = nestedSchemaTypeName,
                ResolverConfig = resolverConfig
            },
            PropertyAlias = "faqItems",
            SchemaTypeRegistry = _registry,
            MappingRepository = _repository,
            HttpContextAccessor = _httpContextAccessor,
            ResolverFactory = resolverFactory,
            Property = property,
            RecursionDepth = recursionDepth,
            MaxRecursionDepth = 3
        };
    }

    /// <summary>
    /// Builds a block element whose properties each carry an editor alias, so
    /// <see cref="BlockContentResolver"/> can pick a per-editor resolver from the factory.
    /// </summary>
    private static IPublishedElement CreateBlockElementWithEditors(
        string contentTypeAlias,
        params (string Alias, object? Value, string? EditorAlias)[] properties)
    {
        var element = Substitute.For<IPublishedElement>();
        var contentType = Substitute.For<IPublishedContentType>();
        contentType.Alias.Returns(contentTypeAlias);
        element.ContentType.Returns(contentType);
        element.Key.Returns(Guid.NewGuid());

        var stubbedProps = new List<IPublishedProperty>();
        foreach (var (alias, value, editorAlias) in properties)
        {
            var prop = Substitute.For<IPublishedProperty>();
            prop.GetValue(Arg.Any<string?>(), Arg.Any<string?>()).Returns(value);
            prop.Alias.Returns(alias);

            var propType = Substitute.For<IPublishedPropertyType>();
            propType.EditorAlias.Returns(editorAlias);
            prop.PropertyType.Returns(propType);

            // Umbraco property aliases are case-insensitive — mirror that here.
            element.GetProperty(Arg.Is<string>(a => string.Equals(a, alias, StringComparison.OrdinalIgnoreCase)))
                .Returns(prop);
            stubbedProps.Add(prop);
        }

        // Basic text extraction walks element.Properties — without this stub NSubstitute
        // auto-returns an empty enumerable and the walk silently sees nothing.
        element.Properties.Returns(stubbedProps);

        return element;
    }
}
