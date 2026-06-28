using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Umbraco.Cms.Core.Models.Blocks;
using Umbraco.Cms.Core.Models.PublishedContent;
using Umbraco.Cms.Core.PublishedCache;
using Umbraco.Cms.Core.Routing;
using Umbraco.Cms.Core.Services;
using Umbraco.Cms.Core.Services.Navigation;
using Umbraco.Community.SchemeWeaver.Graph;
using Umbraco.Community.SchemeWeaver.Models.Api;
using Umbraco.Community.SchemeWeaver.Models.Entities;
using Umbraco.Community.SchemeWeaver.Persistence;
using Umbraco.Community.SchemeWeaver.Services;
using Umbraco.Community.SchemeWeaver.Services.Advisory;
using Umbraco.Community.SchemeWeaver.Services.Resolvers;
using Umbraco.Community.SchemeWeaver.Services.ValueSchemas;
using Umbraco.Community.SchemeWeaver.Services.Validation;
using Xunit;

namespace Umbraco.Community.SchemeWeaver.Tests.Unit;

/// <summary>
/// P1.2 — block-instance preview: rendering the real JSON-LD a single nested block contributes
/// to its page, via the page mapping's route for that block type.
/// </summary>
public class BlockInstancePreviewTests
{
    private readonly ISchemaMappingRepository _repository = Substitute.For<ISchemaMappingRepository>();
    private readonly ISchemaTypeRegistry _registry = new SchemaTypeRegistry();
    private readonly IHttpContextAccessor _httpContextAccessor = Substitute.For<IHttpContextAccessor>();
    private readonly IVariationContextAccessor _variationContextAccessor = Substitute.For<IVariationContextAccessor>();

    private JsonLdGenerator CreateGenerator()
    {
        var factory = new PropertyValueResolverFactory([
            new BlockContentResolver(NullLogger<BlockContentResolver>.Instance),
            new DefaultPropertyValueResolver()
        ]);
        return new JsonLdGenerator(
            _repository, _registry, _httpContextAccessor,
            Substitute.For<IDocumentNavigationQueryService>(),
            Substitute.For<IPublishedContentStatusFilteringService>(),
            factory,
            Substitute.For<IPublishedUrlProvider>(),
            _variationContextAccessor,
            NullLogger<JsonLdGenerator>.Instance,
            Options.Create(new SchemeWeaverOptions()));
    }

    // --- fixtures ---

    private static IPublishedElement CreateLeafElement(string alias, Guid key, Dictionary<string, object?> props)
    {
        var element = Substitute.For<IPublishedElement>();
        var ct = Substitute.For<IPublishedContentType>();
        ct.Alias.Returns(alias);
        element.ContentType.Returns(ct);
        element.Key.Returns(key);

        var properties = new List<IPublishedProperty>();
        foreach (var kvp in props)
        {
            var p = Substitute.For<IPublishedProperty>();
            p.Alias.Returns(kvp.Key);
            var pt = Substitute.For<IPublishedPropertyType>();
            pt.EditorAlias.Returns("Umbraco.TextBox"); // non-block, so the walk doesn't recurse into it
            p.PropertyType.Returns(pt);
            p.GetValue(Arg.Any<string?>(), Arg.Any<string?>()).Returns(kvp.Value);
            element.GetProperty(kvp.Key).Returns(p);
            properties.Add(p);
        }
        element.Properties.Returns(properties);
        return element;
    }

    /// <summary>An element whose only property is itself a Block List (a block holding nested blocks).</summary>
    private static IPublishedElement CreateContainerElement(string alias, Guid key, string blockPropAlias, IPublishedElement[] children)
    {
        var element = Substitute.For<IPublishedElement>();
        var ct = Substitute.For<IPublishedContentType>();
        ct.Alias.Returns(alias);
        element.ContentType.Returns(ct);
        element.Key.Returns(key);

        var model = new BlockListModel(children.Select(c => new BlockListItem(Guid.NewGuid(), c, null, null)).ToList());
        var prop = Substitute.For<IPublishedProperty>();
        prop.Alias.Returns(blockPropAlias);
        var pt = Substitute.For<IPublishedPropertyType>();
        pt.EditorAlias.Returns("Umbraco.BlockList");
        prop.PropertyType.Returns(pt);
        prop.GetValue(Arg.Any<string?>(), Arg.Any<string?>()).Returns(model);
        element.GetProperty(blockPropAlias).Returns(prop);
        element.Properties.Returns(new[] { prop });
        return element;
    }

    private static IPublishedContent CreatePage(string alias, Guid key, string blockPropAlias, IPublishedElement[] blocks)
    {
        var content = Substitute.For<IPublishedContent>();
        var ct = Substitute.For<IPublishedContentType>();
        ct.Alias.Returns(alias);
        content.ContentType.Returns(ct);
        content.Key.Returns(key);
        content.Name.Returns($"{alias} node");

        var model = new BlockListModel(blocks.Select(b => new BlockListItem(Guid.NewGuid(), b, null, null)).ToList());
        var prop = Substitute.For<IPublishedProperty>();
        prop.Alias.Returns(blockPropAlias);
        var pt = Substitute.For<IPublishedPropertyType>();
        pt.EditorAlias.Returns("Umbraco.BlockList");
        prop.PropertyType.Returns(pt);
        prop.GetValue(Arg.Any<string?>(), Arg.Any<string?>()).Returns(model);
        content.GetProperty(blockPropAlias).Returns(prop);
        content.Properties.Returns(new[] { prop });
        return content;
    }

    private void SeedPageMapping(string pageAlias, string blockPropAlias, object resolverConfig)
    {
        var mapping = new SchemaMapping { Id = 1, ContentTypeAlias = pageAlias, SchemaTypeName = "WebPage", IsEnabled = true };
        _repository.GetByContentTypeAlias(pageAlias).Returns(mapping);
        _repository.GetPropertyMappings(1).Returns(new List<PropertyMapping>
        {
            new()
            {
                SchemaPropertyName = "mainEntity",
                SourceType = "blockContent",
                ContentTypePropertyAlias = blockPropAlias,
                ResolverConfig = JsonSerializer.Serialize(resolverConfig)
            }
        });
    }

    private static object FaqRoute() => new
    {
        routes = new[]
        {
            new
            {
                blockAlias = "faqItem",
                nestedSchemaType = "Question",
                propertyMappings = new object[]
                {
                    new { schemaProperty = "name", contentProperty = "question" },
                    new { schemaProperty = "acceptedAnswer", contentProperty = "answer", wrapInType = "Answer", wrapInProperty = "Text" }
                }
            }
        }
    };

    // --- FindBlockInstance ---

    [Fact]
    public void FindBlockInstance_TopLevel_LocatesByKey()
    {
        var key = Guid.NewGuid();
        var faq = CreateLeafElement("faqItem", key, new() { ["question"] = "Q1", ["answer"] = "A1" });
        var page = CreatePage("faqPage", Guid.NewGuid(), "content", [faq]);

        CreateGenerator().FindBlockInstance(page, key).Should().BeSameAs(faq);
    }

    [Fact]
    public void FindBlockInstance_Nested_LocatesByKey()
    {
        var faqKey = Guid.NewGuid();
        var faq = CreateLeafElement("faqItem", faqKey, new() { ["question"] = "Q", ["answer"] = "A" });
        var section = CreateContainerElement("faqSection", Guid.NewGuid(), "questions", [faq]);
        var page = CreatePage("faqPage", Guid.NewGuid(), "sections", [section]);

        CreateGenerator().FindBlockInstance(page, faqKey)!.ContentType.Alias.Should().Be("faqItem");
    }

    [Fact]
    public void FindBlockInstance_UnknownKey_ReturnsNull()
    {
        var faq = CreateLeafElement("faqItem", Guid.NewGuid(), new() { ["question"] = "Q" });
        var page = CreatePage("faqPage", Guid.NewGuid(), "content", [faq]);

        CreateGenerator().FindBlockInstance(page, Guid.NewGuid()).Should().BeNull();
    }

    // --- GenerateBlockInstanceJsonLd ---

    [Fact]
    public void GenerateBlockInstanceJsonLd_RealFaqItem_RendersQuestionWithAnswer()
    {
        var key = Guid.NewGuid();
        var faq = CreateLeafElement("faqItem", key, new() { ["question"] = "What is your returns policy?", ["answer"] = "Within 30 days." });
        var page = CreatePage("faqPage", Guid.NewGuid(), "content", [faq]);
        SeedPageMapping("faqPage", "content", FaqRoute());

        var result = CreateGenerator().GenerateBlockInstanceJsonLd(page, key);

        result.Status.Should().Be(BlockInstanceResolutionStatus.Rendered);
        result.SchemaType.Should().Be("Question");
        result.BlockAlias.Should().Be("faqItem");
        result.ResolvedFromNodeName.Should().Be("faqPage node");
        result.JsonLd.Should().Contain("What is your returns policy?");
        result.JsonLd.Should().Contain("Within 30 days.");
        result.JsonLd.Should().Contain("\"Question\"");
    }

    [Fact]
    public void GenerateBlockInstanceJsonLd_NestedFaqItem_RendersViaNestedRoute()
    {
        var faqKey = Guid.NewGuid();
        var faq = CreateLeafElement("faqItem", faqKey, new() { ["question"] = "Nested Q?", ["answer"] = "Nested A." });
        var section = CreateContainerElement("faqSection", Guid.NewGuid(), "questions", [faq]);
        var page = CreatePage("faqPage", Guid.NewGuid(), "sections", [section]);

        SeedPageMapping("faqPage", "sections", new
        {
            routes = new[]
            {
                new
                {
                    blockAlias = "faqSection",
                    nestedSchemaType = "ItemList",
                    propertyMappings = new object[]
                    {
                        new
                        {
                            schemaProperty = "itemListElement",
                            contentProperty = "questions",
                            routes = new[]
                            {
                                new
                                {
                                    blockAlias = "faqItem",
                                    nestedSchemaType = "Question",
                                    propertyMappings = new object[]
                                    {
                                        new { schemaProperty = "name", contentProperty = "question" }
                                    }
                                }
                            }
                        }
                    }
                }
            }
        });

        var result = CreateGenerator().GenerateBlockInstanceJsonLd(page, faqKey);

        result.Status.Should().Be(BlockInstanceResolutionStatus.Rendered, "the nested faqItem route is discovered by walking nested routes");
        result.SchemaType.Should().Be("Question");
        result.JsonLd.Should().Contain("Nested Q?");
    }

    [Fact]
    public void GenerateBlockInstanceJsonLd_MissingKey_ReturnsBlockNotFound()
    {
        var faq = CreateLeafElement("faqItem", Guid.NewGuid(), new() { ["question"] = "Q" });
        var page = CreatePage("faqPage", Guid.NewGuid(), "content", [faq]);
        SeedPageMapping("faqPage", "content", FaqRoute());

        CreateGenerator().GenerateBlockInstanceJsonLd(page, Guid.NewGuid())
            .Status.Should().Be(BlockInstanceResolutionStatus.BlockNotFound);
    }

    [Fact]
    public void GenerateBlockInstanceJsonLd_BlockTypeNotRouted_ReturnsNoRouteForBlock()
    {
        var key = Guid.NewGuid();
        var unmapped = CreateLeafElement("imageBlock", key, new() { ["caption"] = "hi" });
        var page = CreatePage("faqPage", Guid.NewGuid(), "content", [unmapped]);
        SeedPageMapping("faqPage", "content", FaqRoute()); // only routes faqItem

        var result = CreateGenerator().GenerateBlockInstanceJsonLd(page, key);

        result.Status.Should().Be(BlockInstanceResolutionStatus.NoRouteForBlock);
        result.BlockAlias.Should().Be("imageBlock");
    }

    [Fact]
    public void GenerateBlockInstanceJsonLd_WildcardRoute_MatchesAnyBlock()
    {
        var key = Guid.NewGuid();
        var block = CreateLeafElement("anyBlock", key, new() { ["heading"] = "Hello" });
        var page = CreatePage("page", Guid.NewGuid(), "content", [block]);
        SeedPageMapping("page", "content", new
        {
            routes = new[]
            {
                new
                {
                    blockAlias = "", // wildcard
                    nestedSchemaType = "Thing",
                    propertyMappings = new object[] { new { schemaProperty = "name", contentProperty = "heading" } }
                }
            }
        });

        var result = CreateGenerator().GenerateBlockInstanceJsonLd(page, key);

        result.Status.Should().Be(BlockInstanceResolutionStatus.Rendered);
        result.JsonLd.Should().Contain("Hello");
    }

    // --- Service mapping ---

    private SchemeWeaverService CreateService(IJsonLdGenerator generator)
    {
        var validator = Substitute.For<ISchemaValidator>();
        validator.Validate(Arg.Any<string>()).Returns(ValidationResult.Empty);
        var rangeValidator = Substitute.For<ISchemaRangeValidator>();
        rangeValidator.Validate(Arg.Any<SchemaMappingDto>()).Returns(Array.Empty<ValidationIssue>());
        var advisor = Substitute.For<IMappingAdvisor>();
        advisor.AdviseEntry(Arg.Any<MappingEntryInput>()).Returns(Array.Empty<MappingAdvice>());
        var valueSchema = Substitute.For<IPropertyValueSchemaService>();
        var reach = Substitute.For<IMappingReachabilityClassifier>();
        var drift = Substitute.For<IMappingDriftReporter>();
        drift.GetStatus(Arg.Any<string>()).Returns(MappingDriftStatus.USyncUnavailable);

        return new SchemeWeaverService(
            _registry, Substitute.For<ISchemaAutoMapper>(), generator,
            Substitute.For<IGraphGenerator>(), _repository,
            Substitute.For<IContentTypeService>(), Substitute.For<IDataTypeService>(),
            validator, Substitute.For<IBlockSchemaSuggester>(), rangeValidator, advisor, valueSchema, reach, drift,
            Substitute.For<Umbraco.Cms.Core.Events.IEventAggregator>(),
            Options.Create(new SchemeWeaverOptions()),
            NullLogger<SchemeWeaverService>.Instance);
    }

    [Fact]
    public void Service_Rendered_SetsJsonLdAndInfoNote()
    {
        var page = CreatePage("faqPage", Guid.NewGuid(), "content", []);
        var key = Guid.NewGuid();
        var pageKey = page.Key; // capture before configuring the substitute (avoid nested substitute calls in Returns)
        var generator = Substitute.For<IJsonLdGenerator>();
        generator.GetResolvedBaseUrl().Returns("https://example.com");
        generator.GenerateBlockInstanceJsonLd(page, key, null).Returns(
            new BlockInstancePreviewResult(BlockInstanceResolutionStatus.Rendered, "{\"@type\":\"Question\"}",
                "faqPage node", pageKey, "faqItem", "Question"));

        var response = CreateService(generator).GenerateBlockInstancePreview(page, key);

        response.JsonLd.Should().Contain("Question");
        response.ResolvedBaseUrl.Should().Be("https://example.com");
        response.Issues.Should().Contain(i => i.Severity == "info" && i.Message!.Contains("faqPage node"));
        response.Errors.Should().BeEmpty();
    }

    [Fact]
    public void Service_BlockNotFound_AddsError()
    {
        var page = CreatePage("faqPage", Guid.NewGuid(), "content", []);
        var key = Guid.NewGuid();
        var pageKey = page.Key;
        var generator = Substitute.For<IJsonLdGenerator>();
        generator.GenerateBlockInstanceJsonLd(page, key, null).Returns(
            new BlockInstancePreviewResult(BlockInstanceResolutionStatus.BlockNotFound, null, "faqPage node", pageKey, null, null));

        var response = CreateService(generator).GenerateBlockInstancePreview(page, key);

        response.JsonLd.Should().BeEmpty();
        response.Errors.Should().ContainSingle(e => e.Contains("was not found"));
    }
}
