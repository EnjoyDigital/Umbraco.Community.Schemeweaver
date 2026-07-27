using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;
using NSubstitute;
using Umbraco.Cms.Core.Models;
using Umbraco.Cms.Core.Models.Blocks;
using Umbraco.Cms.Core.Models.PublishedContent;
using Umbraco.Cms.Core.PropertyEditors.ValueConverters;
using Umbraco.Cms.Core.PublishedCache;
using Umbraco.Cms.Core.Routing;
using Umbraco.Cms.Core.Services;
using Umbraco.Cms.Core.Services.Navigation;
using Umbraco.Community.SchemeWeaver.Graph;
using Umbraco.Community.SchemeWeaver.Models.Api;
using Umbraco.Community.SchemeWeaver.Models.Entities;
using Umbraco.Community.SchemeWeaver.Persistence;
using Umbraco.Community.SchemeWeaver.Services;
using Umbraco.Community.SchemeWeaver.Services.Resolvers;
using Umbraco.Community.SchemeWeaver.Services.Validation;
using Umbraco.Community.SchemeWeaver.Tests.Unit.TestSupport;

namespace Umbraco.Community.SchemeWeaver.Tests.Unit;

public class JsonLdGeneratorTests
{
    private readonly ISchemaMappingRepository _repository = Substitute.For<ISchemaMappingRepository>();
    private readonly ISchemaTypeRegistry _registry = new SchemaTypeRegistry();
    private readonly IHttpContextAccessor _httpContextAccessor = Substitute.For<IHttpContextAccessor>();
    private readonly IDocumentNavigationQueryService _navigationQueryService = Substitute.For<IDocumentNavigationQueryService>();
    private readonly IPublishedContentStatusFilteringService _publishedStatusFilteringService = Substitute.For<IPublishedContentStatusFilteringService>();
    private readonly IPropertyValueResolverFactory _resolverFactory;
    private readonly IPublishedUrlProvider _urlProvider = Substitute.For<IPublishedUrlProvider>();
    private readonly IVariationContextAccessor _variationContextAccessor = Substitute.For<IVariationContextAccessor>();
    private readonly ILogger<JsonLdGenerator> _logger = Substitute.For<ILogger<JsonLdGenerator>>();
    private readonly JsonLdGenerator _sut;

    public JsonLdGeneratorTests()
    {
        _resolverFactory = new PropertyValueResolverFactory([new DefaultPropertyValueResolver()]);
        _sut = new JsonLdGenerator(
            _repository,
            _registry,
            _httpContextAccessor,
            _navigationQueryService,
            _publishedStatusFilteringService,
            _resolverFactory,
            _urlProvider,
            _variationContextAccessor,
            _logger,
            Options.Create(new SchemeWeaverOptions()));
    }

    private static IPublishedContent CreateContent(string contentTypeAlias, Dictionary<string, object?>? properties = null)
    {
        var content = Substitute.For<IPublishedContent>();
        var contentType = Substitute.For<IPublishedContentType>();
        contentType.Alias.Returns(contentTypeAlias);
        content.ContentType.Returns(contentType);
        content.Id.Returns(1);
        content.Key.Returns(Guid.NewGuid());

        if (properties is not null)
        {
            foreach (var kvp in properties)
            {
                var property = Substitute.For<IPublishedProperty>();
                property.GetValue(Arg.Any<string?>(), Arg.Any<string?>()).Returns(kvp.Value);
                content.GetProperty(kvp.Key).Returns(property);
            }
        }

        return content;
    }

    private static SchemaMapping CreateMapping(string contentTypeAlias, string schemaTypeName, bool isEnabled = true)
        => new()
        {
            Id = 1,
            ContentTypeAlias = contentTypeAlias,
            SchemaTypeName = schemaTypeName,
            IsEnabled = isEnabled
        };

    // Production resolves Parent()/Ancestors() through Umbraco's non-deprecated
    // Parent<T>(navService, filterService) / Ancestors(...) extension methods. Those extensions
    // resolve keys internally via IPublishedStatusFilteringService.Unfiltered() on Umbraco 18
    // and, since 17.5 backported it, on 17 as well (17.0–17.4 used FilterAvailable()). To make
    // the extension return our test nodes we have to stub whatever it calls internally — there
    // is no other seam. Unfiltered carries Umbraco's own [Obsolete] "intermediate solution" note
    // (it will change the extension's internals in v19), so we suppress CS0618 here only: this
    // is a test mock mirroring Umbraco's internal call, not the package using a deprecated API.
    private void StubUnfilteredResolution(params IPublishedContent[] nodes)
    {
#pragma warning disable CS0618 // Unfiltered is Umbraco-internal-deprecated; mocked, not used for functionality
        _publishedStatusFilteringService
            .Unfiltered(Arg.Any<IEnumerable<Guid>>())
            .Returns(callInfo =>
            {
                var keys = ((IEnumerable<Guid>)callInfo[0]).ToHashSet();
                return nodes.Where(n => keys.Contains(n.Key)).ToArray();
            });
#pragma warning restore CS0618
    }

    [Fact]
    public void GenerateJsonLd_NoMappingExists_ReturnsNull()
    {
        var content = CreateContent("article");
        _repository.GetByContentTypeAlias("article").Returns((SchemaMapping?)null);

        var result = _sut.GenerateJsonLd(content);

        result.Should().BeNull();
    }

    [Fact]
    public void GenerateJsonLd_MappingDisabled_ReturnsNull()
    {
        var content = CreateContent("article");
        var mapping = CreateMapping("article", "Article", isEnabled: false);
        _repository.GetByContentTypeAlias("article").Returns(mapping);

        var result = _sut.GenerateJsonLd(content);

        result.Should().BeNull();
    }

    [Fact]
    public void GenerateJsonLd_ValidMapping_ReturnsThingInstance()
    {
        var content = CreateContent("article", new Dictionary<string, object?>
        {
            ["headline"] = "Test Article"
        });
        var mapping = CreateMapping("article", "Article");
        _repository.GetByContentTypeAlias("article").Returns(mapping);
        _repository.GetPropertyMappings(1).Returns(new List<PropertyMapping>
        {
            new() { SchemaPropertyName = "Headline", SourceType = "property", ContentTypePropertyAlias = "headline" }
        });

        var result = _sut.GenerateJsonLd(content);

        result.Should().NotBeNull();
        result.Should().BeOfType<Schema.NET.Article>();
    }

    [Fact]
    public void GenerateJsonLd_StaticSourceType_UsesStaticValue()
    {
        var content = CreateContent("article");
        var mapping = CreateMapping("article", "Article");
        _repository.GetByContentTypeAlias("article").Returns(mapping);
        _repository.GetPropertyMappings(1).Returns(new List<PropertyMapping>
        {
            new() { SchemaPropertyName = "InLanguage", SourceType = "static", StaticValue = "en-GB" }
        });

        var result = _sut.GenerateJsonLd(content);

        result.Should().NotBeNull();
    }

    [Fact]
    public void GenerateJsonLd_ParentSourceType_ReadsFromParent()
    {
        var parentContent = CreateContent("homepage", new Dictionary<string, object?>
        {
            ["siteName"] = "My Site"
        });

        var content = CreateContent("article");
        var contentKey = content.Key;
        var parentKey = parentContent.Key;

        // Set up the navigation service to return the parent key
        _navigationQueryService.TryGetParentKey(contentKey, out Arg.Any<Guid?>())
            .Returns(callInfo =>
            {
                callInfo[1] = (Guid?)parentKey;
                return true;
            });

        // Parent<T>() resolves via the navigation + filtering services (the instance
        // IPublishedContent.Parent property was removed in Umbraco 18). Mock the key→content
        // resolution so the parent is actually returned (FilterAvailable on 17, Unfiltered on 18).
        _publishedStatusFilteringService
            .FilterAvailable(Arg.Is<IEnumerable<Guid>>(keys => keys.Contains(parentKey)), Arg.Any<string?>())
            .Returns(new[] { parentContent });
        StubUnfilteredResolution(parentContent);

        var mapping = CreateMapping("article", "Article");
        _repository.GetByContentTypeAlias("article").Returns(mapping);
        _repository.GetPropertyMappings(1).Returns(new List<PropertyMapping>
        {
            new() { SchemaPropertyName = "Name", SourceType = "parent", ContentTypePropertyAlias = "siteName" }
        });

        var result = _sut.GenerateJsonLd(content);

        result.Should().NotBeNull();
    }

    [Fact]
    public void GenerateJsonLd_PublisherReference_BindsTypedOrganizationIdWithinGraph()
    {
        // Regression (17.10.3): a `reference` source type emitted a bare Thing,
        // which cannot bind to Article.publisher (range Organization) and was
        // silently dropped. The reference must resolve the org piece's @id from
        // the graph context AND be typed as Organization so it survives onto the
        // Article node.
        var content = CreateContent("article");
        var mapping = CreateMapping("article", "Article");
        _repository.GetByContentTypeAlias("article").Returns(mapping);
        _repository.GetPropertyMappings(1).Returns(new List<PropertyMapping>
        {
            new() { SchemaPropertyName = "publisher", SourceType = "reference", TargetPieceKey = "organization" }
        });

        var graphContext = new GraphPieceContext
        {
            Content = content,
            Ids = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["organization"] = "https://example.com/#organization"
            }
        };

        var result = _sut.GenerateJsonLd(content, culture: null, graphContext);

        result.Should().BeOfType<Schema.NET.Article>();
        ((Schema.NET.Article)result!).Publisher.Count.Should().Be(1);
        var json = result.ToString();
        json.Should().Contain("publisher");
        json.Should().Contain("https://example.com/#organization");
    }

    [Fact]
    public void GenerateJsonLd_PublisherReference_WithoutGraphContext_SkipsProperty()
    {
        // Outside the graph pipeline there is nothing to point at — the reference
        // must be skipped cleanly, never throw or emit a dangling publisher.
        var content = CreateContent("article");
        var mapping = CreateMapping("article", "Article");
        _repository.GetByContentTypeAlias("article").Returns(mapping);
        _repository.GetPropertyMappings(1).Returns(new List<PropertyMapping>
        {
            new() { SchemaPropertyName = "publisher", SourceType = "reference", TargetPieceKey = "organization" }
        });

        var result = _sut.GenerateJsonLd(content); // no graph context

        result.Should().BeOfType<Schema.NET.Article>();
        ((Schema.NET.Article)result!).Publisher.Count.Should().Be(0);
    }

    [Fact]
    public void GenerateJsonLd_StripHtmlTransform_RemovesTags()
    {
        var content = CreateContent("article", new Dictionary<string, object?>
        {
            ["bodyText"] = "<p>Hello <strong>World</strong></p>"
        });
        var mapping = CreateMapping("article", "Article");
        _repository.GetByContentTypeAlias("article").Returns(mapping);
        _repository.GetPropertyMappings(1).Returns(new List<PropertyMapping>
        {
            new()
            {
                SchemaPropertyName = "Headline",
                SourceType = "property",
                ContentTypePropertyAlias = "bodyText",
                TransformType = "stripHtml"
            }
        });

        var result = _sut.GenerateJsonLd(content);

        result.Should().NotBeNull();
        result.Should().BeOfType<Schema.NET.Article>();
        // The Article Headline should have HTML stripped
        var article = (Schema.NET.Article)result!;
        article.Should().NotBeNull();
    }

    [Fact]
    public void GenerateJsonLdString_ValidMapping_ReturnsJsonString()
    {
        var content = CreateContent("article", new Dictionary<string, object?>
        {
            ["headline"] = "Test Headline"
        });
        var mapping = CreateMapping("article", "Article");
        _repository.GetByContentTypeAlias("article").Returns(mapping);
        _repository.GetPropertyMappings(1).Returns(new List<PropertyMapping>
        {
            new() { SchemaPropertyName = "Headline", SourceType = "property", ContentTypePropertyAlias = "headline" }
        });

        var result = _sut.GenerateJsonLdString(content);

        result.Should().NotBeNullOrEmpty();
        result.Should().Contain("schema.org");
    }

    [Fact]
    public void GenerateJsonLdString_NoMapping_ReturnsNull()
    {
        var content = CreateContent("article");
        _repository.GetByContentTypeAlias("article").Returns((SchemaMapping?)null);

        var result = _sut.GenerateJsonLdString(content);

        result.Should().BeNull();
    }

    [Fact]
    public void GenerateJsonLd_ComplexType_CreatesNestedPerson()
    {
        var content = CreateContent("article", new Dictionary<string, object?>
        {
            ["authorName"] = "Jane Smith",
            ["authorEmail"] = "jane@example.com"
        });
        var mapping = CreateMapping("article", "Article");
        _repository.GetByContentTypeAlias("article").Returns(mapping);

        var config = System.Text.Json.JsonSerializer.Serialize(new
        {
            complexTypeMappings = new[]
            {
                new { schemaProperty = "Name", sourceType = "property", contentTypePropertyAlias = (string?)"authorName", staticValue = (string?)null },
                new { schemaProperty = "Email", sourceType = "static", contentTypePropertyAlias = (string?)null, staticValue = (string?)"jane@example.com" }
            }
        });

        _repository.GetPropertyMappings(1).Returns(new List<PropertyMapping>
        {
            new()
            {
                SchemaPropertyName = "Author",
                SourceType = "complexType",
                NestedSchemaTypeName = "Person",
                ResolverConfig = config
            }
        });

        var result = _sut.GenerateJsonLd(content);

        result.Should().NotBeNull();
        result.Should().BeOfType<Schema.NET.Article>();
        // The JSON-LD output should contain the nested Person
        var jsonLd = result!.ToString();
        jsonLd.Should().Contain("Person");
        jsonLd.Should().Contain("Jane Smith");
    }

    [Fact]
    public void GenerateJsonLd_UrlProperty_SetsUriFromString()
    {
        var content = CreateContent("event", new Dictionary<string, object?>
        {
            ["ticketUrl"] = "https://tickets.example.com/event/123"
        });
        var mapping = CreateMapping("event", "Event");
        _repository.GetByContentTypeAlias("event").Returns(mapping);
        _repository.GetPropertyMappings(1).Returns(new List<PropertyMapping>
        {
            new() { SchemaPropertyName = "Url", SourceType = "property", ContentTypePropertyAlias = "ticketUrl" }
        });

        var result = _sut.GenerateJsonLd(content);

        result.Should().NotBeNull();
        var jsonLd = result!.ToString();
        jsonLd.Should().Contain("https://tickets.example.com/event/123");
    }

    [Fact]
    public void GenerateJsonLd_ImageProperty_SetsUriFromString()
    {
        // When the resolver returns a URL string for an image property,
        // SetPropertyValue should handle OneOrMany<Values<IImageObject, Uri>> conversion
        var content = CreateContent("article", new Dictionary<string, object?>
        {
            ["heroImage"] = "https://example.com/images/hero.jpg"
        });
        var mapping = CreateMapping("article", "Article");
        _repository.GetByContentTypeAlias("article").Returns(mapping);
        _repository.GetPropertyMappings(1).Returns(new List<PropertyMapping>
        {
            new() { SchemaPropertyName = "Image", SourceType = "property", ContentTypePropertyAlias = "heroImage" }
        });

        var result = _sut.GenerateJsonLd(content);

        result.Should().NotBeNull();
        var jsonLd = result!.ToString();
        jsonLd.Should().Contain("https://example.com/images/hero.jpg");
    }

    [Fact]
    public void GenerateJsonLd_BlockContentSourceType_ResolvesTargetNode()
    {
        // blockContent source type should resolve to the content node (same as property)
        // so that the resolver factory can route to BlockContentResolver based on editor alias
        var content = CreateContent("faqPage", new Dictionary<string, object?>
        {
            ["faqItems"] = "some block content"
        });
        var mapping = CreateMapping("faqPage", "FAQPage");
        _repository.GetByContentTypeAlias("faqPage").Returns(mapping);
        _repository.GetPropertyMappings(1).Returns(new List<PropertyMapping>
        {
            new()
            {
                SchemaPropertyName = "Name",
                SourceType = "blockContent",
                ContentTypePropertyAlias = "faqItems",
                NestedSchemaTypeName = "Question"
            }
        });

        var result = _sut.GenerateJsonLd(content);

        // The key assertion: result is not null, meaning ResolveTargetNode returned
        // a valid node for blockContent (previously it returned null via the _ => null fallback)
        result.Should().NotBeNull();
        result.Should().BeOfType<Schema.NET.FAQPage>();
    }

    [Fact]
    public void GenerateJsonLd_ComplexType_ResolvesPropertyViaResolverFactory()
    {
        // When a complex type sub-mapping has a property source, it should use the resolver factory
        // instead of just calling GetValue()?.ToString()
        var content = CreateContent("product", new Dictionary<string, object?>
        {
            ["brandName"] = "Acme Corp",
            ["brandUrl"] = "https://acme.example.com"
        });
        var mapping = CreateMapping("product", "Product");
        _repository.GetByContentTypeAlias("product").Returns(mapping);

        var config = System.Text.Json.JsonSerializer.Serialize(new
        {
            complexTypeMappings = new[]
            {
                new { schemaProperty = "Name", sourceType = "property", contentTypePropertyAlias = (string?)"brandName", staticValue = (string?)null },
                new { schemaProperty = "Url", sourceType = "property", contentTypePropertyAlias = (string?)"brandUrl", staticValue = (string?)null }
            }
        });

        _repository.GetPropertyMappings(1).Returns(new List<PropertyMapping>
        {
            new()
            {
                SchemaPropertyName = "Brand",
                SourceType = "complexType",
                NestedSchemaTypeName = "Brand",
                ResolverConfig = config
            }
        });

        var result = _sut.GenerateJsonLd(content);

        result.Should().NotBeNull();
        var jsonLd = result!.ToString();
        jsonLd.Should().Contain("Brand");
        jsonLd.Should().Contain("Acme Corp");
        jsonLd.Should().Contain("https://acme.example.com");
    }

    #region Block Content Tests

    private JsonLdGenerator CreateBlockAwareGenerator()
    {
        var blockResolverFactory = new PropertyValueResolverFactory([
            new BlockContentResolver(NullLogger<BlockContentResolver>.Instance),
            new DefaultPropertyValueResolver()
        ]);
        return new JsonLdGenerator(
            _repository, _registry, _httpContextAccessor,
            _navigationQueryService, _publishedStatusFilteringService,
            blockResolverFactory, _urlProvider, _variationContextAccessor,
            _logger, Options.Create(new SchemeWeaverOptions()));
    }

    private static IPublishedContent CreateContentWithBlockList(
        string contentTypeAlias,
        string blockPropertyAlias,
        IPublishedElement[] blockElements,
        Dictionary<string, object?>? extraProperties = null)
    {
        var content = Substitute.For<IPublishedContent>();
        var contentType = Substitute.For<IPublishedContentType>();
        contentType.Alias.Returns(contentTypeAlias);
        content.ContentType.Returns(contentType);
        content.Id.Returns(1);
        content.Key.Returns(Guid.NewGuid());

        // Create block list items using Udi-based constructor
        var blockListItems = blockElements.Select(e =>
        {
            return new BlockListItem(Guid.NewGuid(), e, null, null);
        }).ToList();

        var blockListModel = new BlockListModel(blockListItems);

        // Set up the block list property with the correct editor alias
        var blockProperty = Substitute.For<IPublishedProperty>();
        blockProperty.GetValue(Arg.Any<string?>(), Arg.Any<string?>()).Returns(blockListModel);
        var blockPropertyType = Substitute.For<IPublishedPropertyType>();
        blockPropertyType.EditorAlias.Returns("Umbraco.BlockList");
        blockProperty.PropertyType.Returns(blockPropertyType);
        content.GetProperty(blockPropertyAlias).Returns(blockProperty);

        // Add extra properties
        if (extraProperties is not null)
        {
            foreach (var kvp in extraProperties)
            {
                var property = Substitute.For<IPublishedProperty>();
                property.GetValue(Arg.Any<string?>(), Arg.Any<string?>()).Returns(kvp.Value);
                content.GetProperty(kvp.Key).Returns(property);
            }
        }

        return content;
    }

    private static IPublishedElement CreateBlockElement(string alias, Dictionary<string, object?> properties)
    {
        var element = Substitute.For<IPublishedElement>();
        var elementType = Substitute.For<IPublishedContentType>();
        elementType.Alias.Returns(alias);
        element.ContentType.Returns(elementType);

        foreach (var kvp in properties)
        {
            var prop = Substitute.For<IPublishedProperty>();
            prop.Alias.Returns(kvp.Key);
            prop.GetValue(Arg.Any<string?>(), Arg.Any<string?>()).Returns(kvp.Value);
            element.GetProperty(kvp.Key).Returns(prop);
        }

        return element;
    }

    [Fact]
    public void GenerateJsonLd_FAQPage_ProducesQuestionsWithAnswers()
    {
        var sut = CreateBlockAwareGenerator();

        var faqItems = new[]
        {
            CreateBlockElement("faqItem", new Dictionary<string, object?>
            {
                ["question"] = "What is your returns policy?",
                ["answer"] = "You can return within 30 days"
            }),
            CreateBlockElement("faqItem", new Dictionary<string, object?>
            {
                ["question"] = "How long does delivery take?",
                ["answer"] = "3-5 working days"
            })
        };

        var content = CreateContentWithBlockList("faqPage", "faqItems", faqItems,
            new Dictionary<string, object?> { ["title"] = "FAQ" });

        var mapping = CreateMapping("faqPage", "FAQPage");
        _repository.GetByContentTypeAlias("faqPage").Returns(mapping);
        _repository.GetPropertyMappings(1).Returns(new List<PropertyMapping>
        {
            new()
            {
                SchemaPropertyName = "Name",
                SourceType = "property",
                ContentTypePropertyAlias = "title"
            },
            new()
            {
                SchemaPropertyName = "MainEntity",
                SourceType = "blockContent",
                ContentTypePropertyAlias = "faqItems",
                NestedSchemaTypeName = "Question",
                ResolverConfig = """{"nestedMappings":[{"schemaProperty":"name","contentProperty":"question"},{"schemaProperty":"acceptedAnswer","contentProperty":"answer","wrapInType":"Answer","wrapInProperty":"Text"}]}"""
            }
        });

        var result = sut.GenerateJsonLd(content);

        result.Should().NotBeNull();
        var jsonLd = result!.ToString();
        jsonLd.Should().Contain("FAQPage");
        jsonLd.Should().Contain("Question");
        jsonLd.Should().Contain("What is your returns policy?");
        jsonLd.Should().Contain("How long does delivery take?");
        jsonLd.Should().Contain("Answer");
        jsonLd.Should().Contain("You can return within 30 days");
        jsonLd.Should().Contain("3-5 working days");
    }

    [Fact]
    public void GenerateJsonLd_Recipe_StringListExtractsIngredients()
    {
        var sut = CreateBlockAwareGenerator();

        var ingredients = new[]
        {
            CreateBlockElement("recipeIngredient", new Dictionary<string, object?> { ["ingredient"] = "200g flour" }),
            CreateBlockElement("recipeIngredient", new Dictionary<string, object?> { ["ingredient"] = "100g sugar" }),
            CreateBlockElement("recipeIngredient", new Dictionary<string, object?> { ["ingredient"] = "2 eggs" })
        };

        var content = CreateContentWithBlockList("recipePage", "ingredients", ingredients,
            new Dictionary<string, object?> { ["title"] = "Chocolate Cake" });

        var mapping = CreateMapping("recipePage", "Recipe");
        _repository.GetByContentTypeAlias("recipePage").Returns(mapping);
        _repository.GetPropertyMappings(1).Returns(new List<PropertyMapping>
        {
            new()
            {
                SchemaPropertyName = "Name",
                SourceType = "property",
                ContentTypePropertyAlias = "title"
            },
            new()
            {
                SchemaPropertyName = "RecipeIngredient",
                SourceType = "blockContent",
                ContentTypePropertyAlias = "ingredients",
                ResolverConfig = """{"extractAs":"stringList","contentProperty":"ingredient"}"""
            }
        });

        var result = sut.GenerateJsonLd(content);

        result.Should().NotBeNull();
        var jsonLd = result!.ToString();
        jsonLd.Should().Contain("Recipe");
        jsonLd.Should().Contain("200g flour");
        jsonLd.Should().Contain("100g sugar");
        jsonLd.Should().Contain("2 eggs");
    }

    [Fact]
    public void GenerateJsonLd_Recipe_HowToStepInstructions()
    {
        var sut = CreateBlockAwareGenerator();

        var steps = new[]
        {
            CreateBlockElement("recipeStep", new Dictionary<string, object?>
            {
                ["stepName"] = "Preheat",
                ["stepText"] = "Preheat oven to 180C"
            }),
            CreateBlockElement("recipeStep", new Dictionary<string, object?>
            {
                ["stepName"] = "Mix",
                ["stepText"] = "Mix all dry ingredients"
            })
        };

        var content = CreateContentWithBlockList("recipePage", "instructions", steps,
            new Dictionary<string, object?> { ["title"] = "Chocolate Cake" });

        var mapping = CreateMapping("recipePage", "Recipe");
        _repository.GetByContentTypeAlias("recipePage").Returns(mapping);
        _repository.GetPropertyMappings(1).Returns(new List<PropertyMapping>
        {
            new()
            {
                SchemaPropertyName = "Name",
                SourceType = "property",
                ContentTypePropertyAlias = "title"
            },
            new()
            {
                SchemaPropertyName = "RecipeInstructions",
                SourceType = "blockContent",
                ContentTypePropertyAlias = "instructions",
                NestedSchemaTypeName = "HowToStep",
                ResolverConfig = """{"nestedMappings":[{"schemaProperty":"name","contentProperty":"stepName"},{"schemaProperty":"text","contentProperty":"stepText"}]}"""
            }
        });

        var result = sut.GenerateJsonLd(content);

        result.Should().NotBeNull();
        var jsonLd = result!.ToString();
        jsonLd.Should().Contain("HowToStep");
        jsonLd.Should().Contain("Preheat oven to 180C");
        jsonLd.Should().Contain("Mix all dry ingredients");
    }

    [Fact]
    public void GenerateJsonLd_Event_WithComplexTypeLocation()
    {
        var content = CreateContent("eventPage", new Dictionary<string, object?>
        {
            ["title"] = "Tech Conference",
            ["locationName"] = "Convention Centre",
            ["locationAddress"] = "123 Main St"
        });
        var mapping = CreateMapping("eventPage", "Event");
        _repository.GetByContentTypeAlias("eventPage").Returns(mapping);

        var locationConfig = System.Text.Json.JsonSerializer.Serialize(new
        {
            complexTypeMappings = new[]
            {
                new { schemaProperty = "Name", sourceType = "property", contentTypePropertyAlias = (string?)"locationName", staticValue = (string?)null },
                new { schemaProperty = "Address", sourceType = "property", contentTypePropertyAlias = (string?)"locationAddress", staticValue = (string?)null }
            }
        });

        _repository.GetPropertyMappings(1).Returns(new List<PropertyMapping>
        {
            new() { SchemaPropertyName = "Name", SourceType = "property", ContentTypePropertyAlias = "title" },
            new()
            {
                SchemaPropertyName = "Location",
                SourceType = "complexType",
                NestedSchemaTypeName = "Place",
                ResolverConfig = locationConfig
            }
        });

        var result = _sut.GenerateJsonLd(content);

        result.Should().NotBeNull();
        var jsonLd = result!.ToString();
        jsonLd.Should().Contain("Event");
        jsonLd.Should().Contain("Place");
        jsonLd.Should().Contain("Convention Centre");
        jsonLd.Should().Contain("123 Main St");
    }

    [Fact]
    public void GenerateJsonLd_Product_WithReviewBlocks_ProducesReviewArray()
    {
        var sut = CreateBlockAwareGenerator();

        // Review.Author is OneOrMany<Values<IOrganization, IPerson>> — a plain string
        // cannot be implicitly converted. Use wrapInType to nest the author name inside
        // a Person object, which mirrors how a real mapping configuration would work.
        var reviewBlocks = new[]
        {
            CreateBlockElement("reviewItem", new Dictionary<string, object?>
            {
                ["reviewAuthor"] = "Alice Johnson",
                ["reviewBody"] = "Excellent product, highly recommend!"
            }),
            CreateBlockElement("reviewItem", new Dictionary<string, object?>
            {
                ["reviewAuthor"] = "Bob Smith",
                ["reviewBody"] = "Good quality but a bit pricey."
            })
        };

        var content = CreateContentWithBlockList("productPage", "reviews", reviewBlocks,
            new Dictionary<string, object?>
            {
                ["productName"] = "Widget Pro",
                ["sku"] = "WGT-PRO-001"
            });

        var mapping = CreateMapping("productPage", "Product");
        _repository.GetByContentTypeAlias("productPage").Returns(mapping);
        _repository.GetPropertyMappings(1).Returns(new List<PropertyMapping>
        {
            new() { SchemaPropertyName = "Name", SourceType = "property", ContentTypePropertyAlias = "productName" },
            new() { SchemaPropertyName = "Sku", SourceType = "property", ContentTypePropertyAlias = "sku" },
            new()
            {
                SchemaPropertyName = "Review",
                SourceType = "blockContent",
                ContentTypePropertyAlias = "reviews",
                NestedSchemaTypeName = "Review",
                ResolverConfig = """{"nestedMappings":[{"schemaProperty":"author","contentProperty":"reviewAuthor","wrapInType":"Person","wrapInProperty":"Name"},{"schemaProperty":"reviewBody","contentProperty":"reviewBody"}]}"""
            }
        });

        var result = sut.GenerateJsonLd(content);

        result.Should().NotBeNull();
        result.Should().BeOfType<Schema.NET.Product>();
        var jsonLd = result!.ToString();
        jsonLd.Should().Contain("Product");
        jsonLd.Should().Contain("Review");
        jsonLd.Should().Contain("Person");
        jsonLd.Should().Contain("Alice Johnson");
        jsonLd.Should().Contain("Bob Smith");
        jsonLd.Should().Contain("Excellent product, highly recommend!");
        jsonLd.Should().Contain("Good quality but a bit pricey.");
        jsonLd.Should().Contain("Widget Pro");
        jsonLd.Should().Contain("WGT-PRO-001");
    }

    [Fact]
    public void GenerateJsonLd_BlogPosting_WithAuthorComplexType_ProducesNestedPerson()
    {
        var content = CreateContent("blogArticle", new Dictionary<string, object?>
        {
            ["headline"] = "Understanding Structured Data",
            ["articleBody"] = "Structured data helps search engines understand your content.",
            ["datePublished"] = "2026-01-15",
            ["authorName"] = "Dr. Emily Carter"
        });

        var mapping = CreateMapping("blogArticle", "BlogPosting");
        _repository.GetByContentTypeAlias("blogArticle").Returns(mapping);

        var authorConfig = System.Text.Json.JsonSerializer.Serialize(new
        {
            complexTypeMappings = new[]
            {
                new { schemaProperty = "Name", sourceType = "property", contentTypePropertyAlias = (string?)"authorName", staticValue = (string?)null },
                new { schemaProperty = "Email", sourceType = "static", contentTypePropertyAlias = (string?)null, staticValue = (string?)"editor@example.com" }
            }
        });

        _repository.GetPropertyMappings(1).Returns(new List<PropertyMapping>
        {
            new() { SchemaPropertyName = "Headline", SourceType = "property", ContentTypePropertyAlias = "headline" },
            new() { SchemaPropertyName = "ArticleBody", SourceType = "property", ContentTypePropertyAlias = "articleBody" },
            new() { SchemaPropertyName = "DatePublished", SourceType = "property", ContentTypePropertyAlias = "datePublished" },
            new()
            {
                SchemaPropertyName = "Author",
                SourceType = "complexType",
                NestedSchemaTypeName = "Person",
                ResolverConfig = authorConfig
            }
        });

        var result = _sut.GenerateJsonLd(content);

        result.Should().NotBeNull();
        result.Should().BeOfType<Schema.NET.BlogPosting>();
        var jsonLd = result!.ToString();
        jsonLd.Should().Contain("BlogPosting");
        jsonLd.Should().Contain("Person");
        jsonLd.Should().Contain("Dr. Emily Carter");
        jsonLd.Should().Contain("editor@example.com");
        jsonLd.Should().Contain("Understanding Structured Data");
        jsonLd.Should().Contain("Structured data helps search engines understand your content.");
    }

    [Fact]
    public void GenerateJsonLd_FAQPage_ValidatesFullStructure()
    {
        var sut = CreateBlockAwareGenerator();

        var faqItems = new[]
        {
            CreateBlockElement("faqItem", new Dictionary<string, object?>
            {
                ["question"] = "What payment methods do you accept?",
                ["answer"] = "We accept Visa, Mastercard, and PayPal."
            }),
            CreateBlockElement("faqItem", new Dictionary<string, object?>
            {
                ["question"] = "Do you ship internationally?",
                ["answer"] = "Yes, we ship to over 50 countries worldwide."
            }),
            CreateBlockElement("faqItem", new Dictionary<string, object?>
            {
                ["question"] = "What is your warranty policy?",
                ["answer"] = "All products come with a 2-year manufacturer warranty."
            })
        };

        var content = CreateContentWithBlockList("faqPage", "faqItems", faqItems,
            new Dictionary<string, object?> { ["pageTitle"] = "Frequently Asked Questions" });

        var mapping = CreateMapping("faqPage", "FAQPage");
        _repository.GetByContentTypeAlias("faqPage").Returns(mapping);
        _repository.GetPropertyMappings(1).Returns(new List<PropertyMapping>
        {
            new() { SchemaPropertyName = "Name", SourceType = "property", ContentTypePropertyAlias = "pageTitle" },
            new()
            {
                SchemaPropertyName = "MainEntity",
                SourceType = "blockContent",
                ContentTypePropertyAlias = "faqItems",
                NestedSchemaTypeName = "Question",
                ResolverConfig = """{"nestedMappings":[{"schemaProperty":"name","contentProperty":"question"},{"schemaProperty":"acceptedAnswer","contentProperty":"answer","wrapInType":"Answer","wrapInProperty":"Text"}]}"""
            }
        });

        var result = sut.GenerateJsonLd(content);

        result.Should().NotBeNull();
        var jsonLd = result!.ToString();
        jsonLd.Should().NotBeNullOrEmpty();

        // Parse the JSON-LD and validate the full structure
        var doc = System.Text.Json.JsonDocument.Parse(jsonLd);
        var root = doc.RootElement;

        // Validate top-level structure
        root.GetProperty("@context").GetString().Should().Contain("schema.org");
        root.GetProperty("@type").GetString().Should().Be("FAQPage");

        // Validate mainEntity array exists with 3 Question items
        root.TryGetProperty("mainEntity", out var mainEntity).Should().BeTrue();
        mainEntity.ValueKind.Should().Be(System.Text.Json.JsonValueKind.Array);
        mainEntity.GetArrayLength().Should().Be(3);

        // Validate each Question has the correct structure
        var firstQuestion = mainEntity[0];
        firstQuestion.GetProperty("@type").GetString().Should().Be("Question");
        firstQuestion.GetProperty("name").GetString().Should().Be("What payment methods do you accept?");
        firstQuestion.TryGetProperty("acceptedAnswer", out var firstAnswer).Should().BeTrue();
        firstAnswer.GetProperty("@type").GetString().Should().Be("Answer");
        firstAnswer.GetProperty("text").GetString().Should().Be("We accept Visa, Mastercard, and PayPal.");

        var secondQuestion = mainEntity[1];
        secondQuestion.GetProperty("@type").GetString().Should().Be("Question");
        secondQuestion.GetProperty("name").GetString().Should().Be("Do you ship internationally?");
        secondQuestion.TryGetProperty("acceptedAnswer", out var secondAnswer).Should().BeTrue();
        secondAnswer.GetProperty("@type").GetString().Should().Be("Answer");
        secondAnswer.GetProperty("text").GetString().Should().Be("Yes, we ship to over 50 countries worldwide.");

        var thirdQuestion = mainEntity[2];
        thirdQuestion.GetProperty("@type").GetString().Should().Be("Question");
        thirdQuestion.GetProperty("name").GetString().Should().Be("What is your warranty policy?");
        thirdQuestion.TryGetProperty("acceptedAnswer", out var thirdAnswer).Should().BeTrue();
        thirdAnswer.GetProperty("@type").GetString().Should().Be("Answer");
        thirdAnswer.GetProperty("text").GetString().Should().Be("All products come with a 2-year manufacturer warranty.");
    }

    [Fact]
    public void GenerateJsonLd_Recipe_WithIngredientsAndInstructions_ProducesFullOutput()
    {
        var sut = CreateBlockAwareGenerator();

        var ingredients = new[]
        {
            CreateBlockElement("recipeIngredient", new Dictionary<string, object?> { ["ingredient"] = "500g chicken breast" }),
            CreateBlockElement("recipeIngredient", new Dictionary<string, object?> { ["ingredient"] = "2 tablespoons olive oil" }),
            CreateBlockElement("recipeIngredient", new Dictionary<string, object?> { ["ingredient"] = "1 teaspoon paprika" }),
            CreateBlockElement("recipeIngredient", new Dictionary<string, object?> { ["ingredient"] = "Salt and pepper to taste" })
        };

        var instructions = new[]
        {
            CreateBlockElement("recipeStep", new Dictionary<string, object?>
            {
                ["stepName"] = "Prepare",
                ["stepText"] = "Season the chicken with paprika, salt and pepper."
            }),
            CreateBlockElement("recipeStep", new Dictionary<string, object?>
            {
                ["stepName"] = "Cook",
                ["stepText"] = "Heat olive oil in a pan and cook chicken for 6 minutes each side."
            }),
            CreateBlockElement("recipeStep", new Dictionary<string, object?>
            {
                ["stepName"] = "Rest",
                ["stepText"] = "Let the chicken rest for 5 minutes before serving."
            })
        };

        // Create content with two block list properties manually
        var content = Substitute.For<IPublishedContent>();
        var contentType = Substitute.For<IPublishedContentType>();
        contentType.Alias.Returns("recipePage");
        content.ContentType.Returns(contentType);
        content.Id.Returns(1);
        content.Key.Returns(Guid.NewGuid());

        // Simple properties
        var nameProperty = Substitute.For<IPublishedProperty>();
        nameProperty.GetValue(Arg.Any<string?>(), Arg.Any<string?>()).Returns("Paprika Chicken");
        content.GetProperty("recipeName").Returns(nameProperty);

        var descriptionProperty = Substitute.For<IPublishedProperty>();
        descriptionProperty.GetValue(Arg.Any<string?>(), Arg.Any<string?>()).Returns("A quick and flavourful paprika chicken recipe.");
        content.GetProperty("recipeDescription").Returns(descriptionProperty);

        var yieldProperty = Substitute.For<IPublishedProperty>();
        yieldProperty.GetValue(Arg.Any<string?>(), Arg.Any<string?>()).Returns("4 servings");
        content.GetProperty("recipeYield").Returns(yieldProperty);

        var categoryProperty = Substitute.For<IPublishedProperty>();
        categoryProperty.GetValue(Arg.Any<string?>(), Arg.Any<string?>()).Returns("Main Course");
        content.GetProperty("recipeCategory").Returns(categoryProperty);

        // First block list: ingredients
        var ingredientBlockListItems = ingredients.Select(e =>
        {
            return new BlockListItem(Guid.NewGuid(), e, null, null);
        }).ToList();
        var ingredientBlockListModel = new BlockListModel(ingredientBlockListItems);

        var ingredientsProperty = Substitute.For<IPublishedProperty>();
        ingredientsProperty.GetValue(Arg.Any<string?>(), Arg.Any<string?>()).Returns(ingredientBlockListModel);
        var ingredientsPropertyType = Substitute.For<IPublishedPropertyType>();
        ingredientsPropertyType.EditorAlias.Returns("Umbraco.BlockList");
        ingredientsProperty.PropertyType.Returns(ingredientsPropertyType);
        content.GetProperty("ingredients").Returns(ingredientsProperty);

        // Second block list: instructions
        var instructionBlockListItems = instructions.Select(e =>
        {
            return new BlockListItem(Guid.NewGuid(), e, null, null);
        }).ToList();
        var instructionBlockListModel = new BlockListModel(instructionBlockListItems);

        var instructionsProperty = Substitute.For<IPublishedProperty>();
        instructionsProperty.GetValue(Arg.Any<string?>(), Arg.Any<string?>()).Returns(instructionBlockListModel);
        var instructionsPropertyType = Substitute.For<IPublishedPropertyType>();
        instructionsPropertyType.EditorAlias.Returns("Umbraco.BlockList");
        instructionsProperty.PropertyType.Returns(instructionsPropertyType);
        content.GetProperty("instructions").Returns(instructionsProperty);

        var mapping = CreateMapping("recipePage", "Recipe");
        _repository.GetByContentTypeAlias("recipePage").Returns(mapping);
        _repository.GetPropertyMappings(1).Returns(new List<PropertyMapping>
        {
            new() { SchemaPropertyName = "Name", SourceType = "property", ContentTypePropertyAlias = "recipeName" },
            new() { SchemaPropertyName = "Description", SourceType = "property", ContentTypePropertyAlias = "recipeDescription" },
            new() { SchemaPropertyName = "RecipeYield", SourceType = "property", ContentTypePropertyAlias = "recipeYield" },
            new() { SchemaPropertyName = "RecipeCategory", SourceType = "property", ContentTypePropertyAlias = "recipeCategory" },
            new()
            {
                SchemaPropertyName = "RecipeIngredient",
                SourceType = "blockContent",
                ContentTypePropertyAlias = "ingredients",
                ResolverConfig = """{"extractAs":"stringList","contentProperty":"ingredient"}"""
            },
            new()
            {
                SchemaPropertyName = "RecipeInstructions",
                SourceType = "blockContent",
                ContentTypePropertyAlias = "instructions",
                NestedSchemaTypeName = "HowToStep",
                ResolverConfig = """{"nestedMappings":[{"schemaProperty":"name","contentProperty":"stepName"},{"schemaProperty":"text","contentProperty":"stepText"}]}"""
            }
        });

        var result = sut.GenerateJsonLd(content);

        result.Should().NotBeNull();
        result.Should().BeOfType<Schema.NET.Recipe>();
        var jsonLd = result!.ToString();

        // Validate recipe metadata
        jsonLd.Should().Contain("Recipe");
        jsonLd.Should().Contain("Paprika Chicken");
        jsonLd.Should().Contain("A quick and flavourful paprika chicken recipe.");
        jsonLd.Should().Contain("4 servings");
        jsonLd.Should().Contain("Main Course");

        // Validate string ingredients array
        jsonLd.Should().Contain("500g chicken breast");
        jsonLd.Should().Contain("2 tablespoons olive oil");
        jsonLd.Should().Contain("1 teaspoon paprika");
        jsonLd.Should().Contain("Salt and pepper to taste");

        // Validate HowToStep instructions
        jsonLd.Should().Contain("HowToStep");
        jsonLd.Should().Contain("Season the chicken with paprika, salt and pepper.");
        jsonLd.Should().Contain("Heat olive oil in a pan and cook chicken for 6 minutes each side.");
        jsonLd.Should().Contain("Let the chicken rest for 5 minutes before serving.");

        // Parse and validate the overall JSON-LD structure
        var doc = System.Text.Json.JsonDocument.Parse(jsonLd);
        var root = doc.RootElement;
        root.GetProperty("@type").GetString().Should().Be("Recipe");

        // Verify recipeIngredient is an array of strings
        root.TryGetProperty("recipeIngredient", out var ingredientsArray).Should().BeTrue();
        ingredientsArray.GetArrayLength().Should().Be(4);

        // Verify recipeInstructions is an array of HowToStep objects
        root.TryGetProperty("recipeInstructions", out var instructionsArray).Should().BeTrue();
        instructionsArray.GetArrayLength().Should().Be(3);
        instructionsArray[0].GetProperty("@type").GetString().Should().Be("HowToStep");
    }

    [Fact]
    public void GenerateJsonLd_Event_WithLocationAndOffers_ProducesNestedStructure()
    {
        var content = CreateContent("eventPage", new Dictionary<string, object?>
        {
            ["eventName"] = "Summer Music Festival",
            ["eventDescription"] = "An outdoor music festival featuring local and international artists.",
            ["eventUrl"] = "https://summerfest.example.com",
            ["locationName"] = "Hyde Park",
            ["locationAddress"] = "London, W2 2UH",
            ["ticketPrice"] = "45.00",
            ["ticketUrl"] = "https://tickets.example.com/summer-fest"
        });

        var mapping = CreateMapping("eventPage", "Event");
        _repository.GetByContentTypeAlias("eventPage").Returns(mapping);

        var locationConfig = System.Text.Json.JsonSerializer.Serialize(new
        {
            complexTypeMappings = new[]
            {
                new { schemaProperty = "Name", sourceType = "property", contentTypePropertyAlias = (string?)"locationName", staticValue = (string?)null },
                new { schemaProperty = "Address", sourceType = "property", contentTypePropertyAlias = (string?)"locationAddress", staticValue = (string?)null }
            }
        });

        var offersConfig = System.Text.Json.JsonSerializer.Serialize(new
        {
            complexTypeMappings = new[]
            {
                new { schemaProperty = "Price", sourceType = "property", contentTypePropertyAlias = (string?)"ticketPrice", staticValue = (string?)null },
                new { schemaProperty = "Url", sourceType = "property", contentTypePropertyAlias = (string?)"ticketUrl", staticValue = (string?)null }
            }
        });

        _repository.GetPropertyMappings(1).Returns(new List<PropertyMapping>
        {
            new() { SchemaPropertyName = "Name", SourceType = "property", ContentTypePropertyAlias = "eventName" },
            new() { SchemaPropertyName = "Description", SourceType = "property", ContentTypePropertyAlias = "eventDescription" },
            new() { SchemaPropertyName = "Url", SourceType = "property", ContentTypePropertyAlias = "eventUrl" },
            new()
            {
                SchemaPropertyName = "Location",
                SourceType = "complexType",
                NestedSchemaTypeName = "Place",
                ResolverConfig = locationConfig
            },
            new()
            {
                SchemaPropertyName = "Offers",
                SourceType = "complexType",
                NestedSchemaTypeName = "Offer",
                ResolverConfig = offersConfig
            }
        });

        var result = _sut.GenerateJsonLd(content);

        result.Should().NotBeNull();
        result.Should().BeOfType<Schema.NET.Event>();
        var jsonLd = result!.ToString();

        // Validate top-level Event properties
        jsonLd.Should().Contain("Event");
        jsonLd.Should().Contain("Summer Music Festival");
        jsonLd.Should().Contain("An outdoor music festival featuring local and international artists.");
        jsonLd.Should().Contain("https://summerfest.example.com");

        // Validate nested Place location
        jsonLd.Should().Contain("Place");
        jsonLd.Should().Contain("Hyde Park");
        jsonLd.Should().Contain("London, W2 2UH");

        // Validate nested Offer
        jsonLd.Should().Contain("Offer");
        jsonLd.Should().Contain("45.00");
        jsonLd.Should().Contain("https://tickets.example.com/summer-fest");

        // Parse and validate the JSON-LD structure
        var doc = System.Text.Json.JsonDocument.Parse(jsonLd);
        var root = doc.RootElement;
        root.GetProperty("@type").GetString().Should().Be("Event");
        root.TryGetProperty("location", out var location).Should().BeTrue();
        location.GetProperty("@type").GetString().Should().Be("Place");
        root.TryGetProperty("offers", out var offers).Should().BeTrue();
        offers.GetProperty("@type").GetString().Should().Be("Offer");
    }

    #endregion

    #region @id and BreadcrumbList Tests

    [Fact]
    public void GenerateJsonLd_DefaultId_IsContentUrlWithSchemaTypeFragment()
    {
        var content = CreateContent("article", new Dictionary<string, object?>
        {
            ["headline"] = "Test Article"
        });
        var mapping = CreateMapping("article", "Article");
        _repository.GetByContentTypeAlias("article").Returns(mapping);
        _repository.GetPropertyMappings(1).Returns(new List<PropertyMapping>
        {
            new() { SchemaPropertyName = "Headline", SourceType = "property", ContentTypePropertyAlias = "headline" }
        });
        _urlProvider.GetUrl(content, UrlMode.Absolute).Returns("https://example.com/articles/test");

        var result = _sut.GenerateJsonLd(content);

        result.Should().NotBeNull();
        result!.Id.Should().NotBeNull();
        // The fragment disambiguates the page (WebPage) from the entity it describes
        // (Article, Organization, …) so two entities on the same URL get distinct @ids.
        var jsonLd = result.ToString();
        jsonLd.Should().Contain("\"@id\":\"https://example.com/articles/test#article\"");
    }

    [Fact]
    public void GenerateJsonLd_ExplicitIdMappingWithStaticValue_OverridesDefault()
    {
        var content = CreateContent("organization", new Dictionary<string, object?>
        {
            ["orgName"] = "Acme Ltd"
        });
        var mapping = CreateMapping("organization", "Organization");
        _repository.GetByContentTypeAlias("organization").Returns(mapping);
        _repository.GetPropertyMappings(1).Returns(new List<PropertyMapping>
        {
            new() { SchemaPropertyName = "Name", SourceType = "property", ContentTypePropertyAlias = "orgName" },
            new() { SchemaPropertyName = "Id", SourceType = "static", StaticValue = "https://acme.example/#org" },
        });
        _urlProvider.GetUrl(content, UrlMode.Absolute).Returns("https://example.com/about");

        var result = _sut.GenerateJsonLd(content);

        result.Should().NotBeNull();
        var jsonLd = result!.ToString();
        jsonLd.Should().Contain("\"@id\":\"https://acme.example/#org\"");
        jsonLd.Should().NotContain("\"@id\":\"https://example.com/about#organization\"");
    }

    [Fact]
    public void GenerateJsonLd_ExplicitIdMappingWithPropertyValue_OverridesDefault()
    {
        var content = CreateContent("organization", new Dictionary<string, object?>
        {
            ["orgName"] = "Acme Ltd",
            ["canonicalId"] = "https://acme.example/#org"
        });
        var mapping = CreateMapping("organization", "Organization");
        _repository.GetByContentTypeAlias("organization").Returns(mapping);
        _repository.GetPropertyMappings(1).Returns(new List<PropertyMapping>
        {
            new() { SchemaPropertyName = "Name", SourceType = "property", ContentTypePropertyAlias = "orgName" },
            new() { SchemaPropertyName = "Id", SourceType = "property", ContentTypePropertyAlias = "canonicalId" },
        });
        _urlProvider.GetUrl(content, UrlMode.Absolute).Returns("https://example.com/about");

        var result = _sut.GenerateJsonLd(content);

        result.Should().NotBeNull();
        var jsonLd = result!.ToString();
        jsonLd.Should().Contain("\"@id\":\"https://acme.example/#org\"");
    }

    [Fact]
    public void GenerateJsonLd_NoUrl_DoesNotSetId()
    {
        var content = CreateContent("article", new Dictionary<string, object?>
        {
            ["headline"] = "Test Article"
        });
        var mapping = CreateMapping("article", "Article");
        _repository.GetByContentTypeAlias("article").Returns(mapping);
        _repository.GetPropertyMappings(1).Returns(new List<PropertyMapping>
        {
            new() { SchemaPropertyName = "Headline", SourceType = "property", ContentTypePropertyAlias = "headline" }
        });
        _urlProvider.GetUrl(content, UrlMode.Absolute).Returns("#");
        _urlProvider.GetUrl(content, UrlMode.Relative).Returns("#");

        var result = _sut.GenerateJsonLd(content);

        result.Should().NotBeNull();
        var jsonLd = result!.ToString();
        jsonLd.Should().NotContain("@id");
    }

    [Fact]
    public void GenerateJsonLd_RelativeUrl_BuildsAbsoluteIdFromRequest()
    {
        var content = CreateContent("article", new Dictionary<string, object?>
        {
            ["headline"] = "Test Article"
        });
        var mapping = CreateMapping("article", "Article");
        _repository.GetByContentTypeAlias("article").Returns(mapping);
        _repository.GetPropertyMappings(1).Returns(new List<PropertyMapping>
        {
            new() { SchemaPropertyName = "Headline", SourceType = "property", ContentTypePropertyAlias = "headline" }
        });

        // Absolute returns "#", relative returns a path
        _urlProvider.GetUrl(content, UrlMode.Absolute).Returns("#");
        _urlProvider.GetUrl(content, UrlMode.Relative).Returns("/articles/test");

        var httpContext = new DefaultHttpContext();
        httpContext.Request.Scheme = "https";
        httpContext.Request.Host = new HostString("example.com");
        _httpContextAccessor.HttpContext.Returns(httpContext);

        var result = _sut.GenerateJsonLd(content);

        result.Should().NotBeNull();
        var jsonLd = result!.ToString();
        jsonLd.Should().Contain("\"@id\":\"https://example.com/articles/test#article\"");
    }

    [Fact]
    public void GenerateBreadcrumbJsonLd_RootContent_ReturnsNull()
    {
        var root = CreateContent("homepage");
        var rootKey = root.Key;

        // Parent<T> calls GetParent which calls TryGetParentKey
        _navigationQueryService.TryGetParentKey(rootKey, out Arg.Any<Guid?>())
            .Returns(callInfo =>
            {
                callInfo[1] = (Guid?)null;
                return true;
            });

        var result = _sut.GenerateBreadcrumbJsonLd(root);

        result.Should().BeNull();
    }

    [Fact]
    public void BuildBreadcrumbJsonLd_NestedContent_HasCorrectPositions()
    {
        // Test the breadcrumb assembly logic directly (bypassing navigation service)
        var root = CreateContent("homepage");
        root.Name.Returns("Home");

        var section = CreateContent("section");
        section.Name.Returns("Articles");

        var article = CreateContent("article");
        article.Name.Returns("Test Article");

        _urlProvider.GetUrl(root, UrlMode.Absolute).Returns("https://example.com/");
        _urlProvider.GetUrl(section, UrlMode.Absolute).Returns("https://example.com/articles/");
        _urlProvider.GetUrl(article, UrlMode.Absolute).Returns("https://example.com/articles/test/");

        // Pass the ancestor chain directly (root-first order)
        var ancestors = new List<IPublishedContent> { root, section, article };
        var result = _sut.BuildBreadcrumbJsonLd(ancestors);

        result.Should().NotBeNull();
        result.Should().Contain("BreadcrumbList");
        result.Should().Contain("Home");
        result.Should().Contain("Articles");
        result.Should().Contain("Test Article");
        result.Should().Contain("https://example.com/");
        result.Should().Contain("https://example.com/articles/");
        result.Should().Contain("https://example.com/articles/test/");

        // Verify it's valid JSON with correct structure
        var doc = System.Text.Json.JsonDocument.Parse(result!);
        var rootElement = doc.RootElement;
        rootElement.GetProperty("@type").GetString().Should().Be("BreadcrumbList");
    }

    [Fact]
    public void BuildBreadcrumbJsonLd_SingleItem_ReturnsNull()
    {
        var root = CreateContent("homepage");
        root.Name.Returns("Home");

        var result = _sut.BuildBreadcrumbJsonLd([root]);

        result.Should().BeNull();
    }

    [Fact]
    public void BuildBreadcrumbJsonLd_EachListItem_HasItemFieldWithUrlAsId()
    {
        // Google's BreadcrumbList spec requires an `item` field on every ListItem
        // (a URL string or a Thing with @id). The Rich Results Test flags
        // "Missing field 'item'" otherwise. Regression guard against emitting
        // url/@id directly on the ListItem without an item pointer.
        var root = CreateContent("homepage");
        root.Name.Returns("Home");

        var about = CreateContent("aboutPage");
        about.Name.Returns("About");

        _urlProvider.GetUrl(root, UrlMode.Absolute).Returns("https://example.com/");
        _urlProvider.GetUrl(about, UrlMode.Absolute).Returns("https://example.com/about/");

        var result = _sut.BuildBreadcrumbJsonLd([root, about]);

        var doc = System.Text.Json.JsonDocument.Parse(result!);
        var items = doc.RootElement.GetProperty("itemListElement").EnumerateArray().ToList();
        items.Should().HaveCount(2);

        items[0].GetProperty("item").GetProperty("@id").GetString()
            .Should().Be("https://example.com/");
        items[1].GetProperty("item").GetProperty("@id").GetString()
            .Should().Be("https://example.com/about/");
    }

    #endregion

    #region Nested Complex Type Tests

    [Fact]
    public void GenerateJsonLd_NestedComplexType_TwoLevelsDeep()
    {
        var content = CreateContent("productPage", new Dictionary<string, object?>
        {
            ["reviewText"] = "Great product",
            ["authorName"] = "Jane Doe"
        });
        var mapping = CreateMapping("productPage", "Product");
        _repository.GetByContentTypeAlias("productPage").Returns(mapping);

        var reviewConfig = System.Text.Json.JsonSerializer.Serialize(new
        {
            selectedSubType = "Review",
            complexTypeMappings = new object[]
            {
                new { schemaProperty = "ReviewBody", sourceType = "property", contentTypePropertyAlias = "reviewText" },
                new
                {
                    schemaProperty = "Author",
                    sourceType = "complexType",
                    resolverConfig = "{\"selectedSubType\":\"Person\",\"complexTypeMappings\":[{\"schemaProperty\":\"Name\",\"sourceType\":\"property\",\"contentTypePropertyAlias\":\"authorName\"}]}"
                }
            }
        });

        _repository.GetPropertyMappings(1).Returns(new List<PropertyMapping>
        {
            new()
            {
                SchemaPropertyName = "Review",
                SourceType = "complexType",
                NestedSchemaTypeName = "Review",
                ResolverConfig = reviewConfig
            }
        });

        var result = _sut.GenerateJsonLd(content);

        result.Should().NotBeNull();
        result.Should().BeOfType<Schema.NET.Product>();
        var jsonLd = result!.ToString();

        // Validate the nested structure: Product > Review > Person
        jsonLd.Should().Contain("Product");
        jsonLd.Should().Contain("Review");
        jsonLd.Should().Contain("Great product");
        jsonLd.Should().Contain("Person");
        jsonLd.Should().Contain("Jane Doe");

        // Parse and validate the JSON-LD structure at depth
        var doc = System.Text.Json.JsonDocument.Parse(jsonLd);
        var root = doc.RootElement;
        root.GetProperty("@type").GetString().Should().Be("Product");

        root.TryGetProperty("review", out var review).Should().BeTrue();
        review.GetProperty("@type").GetString().Should().Be("Review");
        review.GetProperty("reviewBody").GetString().Should().Be("Great product");

        review.TryGetProperty("author", out var author).Should().BeTrue();
        author.GetProperty("@type").GetString().Should().Be("Person");
        author.GetProperty("name").GetString().Should().Be("Jane Doe");
    }

    [Fact]
    public void GenerateJsonLd_NestedComplexType_ResolvesArbitraryDepth()
    {
        // Build a 3-level deep resolverConfig using proper JSON serialisation.
        // Organization.Member → Organization.Member → Person.Name = "DeepLeaf"
        var innerPersonConfig = JsonSerializer.Serialize(new
        {
            selectedSubType = "Person",
            complexTypeMappings = new[]
            {
                new { schemaProperty = "Name", sourceType = "static", staticValue = "DeepLeaf" }
            }
        });

        var middleOrgConfig = JsonSerializer.Serialize(new
        {
            selectedSubType = "Organization",
            complexTypeMappings = new[]
            {
                new { schemaProperty = "Member", sourceType = "complexType", resolverConfig = innerPersonConfig }
            }
        });

        var outerOrgConfig = JsonSerializer.Serialize(new
        {
            selectedSubType = "Organization",
            complexTypeMappings = new[]
            {
                new { schemaProperty = "Member", sourceType = "complexType", resolverConfig = middleOrgConfig }
            }
        });

        var content = CreateContent("orgPage");
        var mapping = CreateMapping("orgPage", "Organization");
        _repository.GetByContentTypeAlias("orgPage").Returns(mapping);

        _repository.GetPropertyMappings(1).Returns(new List<PropertyMapping>
        {
            new()
            {
                SchemaPropertyName = "Member",
                SourceType = "complexType",
                NestedSchemaTypeName = "Organization",
                ResolverConfig = outerOrgConfig
            }
        });

        var result = _sut.GenerateJsonLd(content);

        result.Should().NotBeNull();
        result.Should().BeOfType<Schema.NET.Organization>();
        var jsonLd = result!.ToString();
        jsonLd.Should().Contain("Organization");

        // With no depth limit, the deepest "DeepLeaf" Person should be fully resolved
        jsonLd.Should().Contain("DeepLeaf");
    }

    #endregion

    #region Source Type Resolution Tests

    [Fact]
    public void GenerateJsonLd_ParentSourceType_ResolvesParentPropertyValue()
    {
        // Verify the parent's property value actually flows through to JSON-LD output
        var parentContent = CreateContent("homepage", new Dictionary<string, object?>
        {
            ["siteName"] = "My Site"
        });

        var content = CreateContent("article");
        var contentKey = content.Key;
        var parentKey = parentContent.Key;

        // Mock Parent<T> resolution: TryGetParentKey → FilterAvailable
        _navigationQueryService.TryGetParentKey(contentKey, out Arg.Any<Guid?>())
            .Returns(callInfo =>
            {
                callInfo[1] = (Guid?)parentKey;
                return true;
            });
        _publishedStatusFilteringService
            .FilterAvailable(Arg.Is<IEnumerable<Guid>>(keys => keys.Contains(parentKey)), Arg.Any<string?>())
            .Returns(new[] { parentContent });
        StubUnfilteredResolution(parentContent);

        var mapping = CreateMapping("article", "Article");
        _repository.GetByContentTypeAlias("article").Returns(mapping);
        _repository.GetPropertyMappings(1).Returns(new List<PropertyMapping>
        {
            new() { SchemaPropertyName = "Name", SourceType = "parent", ContentTypePropertyAlias = "siteName" }
        });

        var result = _sut.GenerateJsonLdString(content);

        result.Should().NotBeNullOrEmpty();
        result.Should().Contain("My Site");
    }

    [Fact]
    public void GenerateJsonLd_AncestorSourceType_ResolvesFromMatchingAncestor()
    {
        // Build a 3-level hierarchy: grandparent (settings) → parent (section) → child (article)
        var grandparent = CreateContent("settings", new Dictionary<string, object?>
        {
            ["siteName"] = "Corporate Site"
        });
        var parent = CreateContent("section");
        var child = CreateContent("article");

        var childKey = child.Key;
        var parentKey = parent.Key;
        var grandparentKey = grandparent.Key;

        // Mock Ancestors() resolution: TryGetAncestorsKeys → FilterAvailable
        _navigationQueryService.TryGetAncestorsKeys(childKey, out Arg.Any<IEnumerable<Guid>>())
            .Returns(callInfo =>
            {
                callInfo[1] = new[] { parentKey, grandparentKey };
                return true;
            });
        _publishedStatusFilteringService
            .FilterAvailable(Arg.Is<IEnumerable<Guid>>(keys => keys.Contains(grandparentKey) && keys.Contains(parentKey)), Arg.Any<string?>())
            .Returns(new[] { parent, grandparent });
        StubUnfilteredResolution(parent, grandparent);

        var mapping = CreateMapping("article", "Article");
        _repository.GetByContentTypeAlias("article").Returns(mapping);
        _repository.GetPropertyMappings(1).Returns(new List<PropertyMapping>
        {
            new()
            {
                SchemaPropertyName = "Name",
                SourceType = "ancestor",
                SourceContentTypeAlias = "settings",
                ContentTypePropertyAlias = "siteName"
            }
        });

        var result = _sut.GenerateJsonLdString(child);

        result.Should().NotBeNullOrEmpty();
        result.Should().Contain("Corporate Site");
    }

    [Fact]
    public void GenerateJsonLd_SiblingSourceType_ResolvesFromSiblingNode()
    {
        // Build parent with two children: content (article) and sibling (sidebarContent)
        var parentNode = CreateContent("homepage");
        var parentNodeKey = parentNode.Key;

        var content = CreateContent("article");
        var contentKey = content.Key;
        content.Id.Returns(100);

        var sibling = CreateContent("sidebarContent", new Dictionary<string, object?>
        {
            ["promoText"] = "Special Offer"
        });
        var siblingKey = sibling.Key;
        sibling.Id.Returns(200);

        // Mock Parent<T> resolution for the content node
        _navigationQueryService.TryGetParentKey(contentKey, out Arg.Any<Guid?>())
            .Returns(callInfo =>
            {
                callInfo[1] = (Guid?)parentNodeKey;
                return true;
            });
        _publishedStatusFilteringService
            .FilterAvailable(Arg.Is<IEnumerable<Guid>>(keys => keys.Contains(parentNodeKey)), Arg.Any<string?>())
            .Returns(new[] { parentNode });
        // Parent resolves via Unfiltered on 18; siblings (the parent's Children) still use FilterAvailable.
        StubUnfilteredResolution(parentNode);

        // Mock Children() on the parent: TryGetChildrenKeys → FilterAvailable
        _navigationQueryService.TryGetChildrenKeys(parentNodeKey, out Arg.Any<IEnumerable<Guid>>())
            .Returns(callInfo =>
            {
                callInfo[1] = new[] { contentKey, siblingKey };
                return true;
            });
        _publishedStatusFilteringService
            .FilterAvailable(Arg.Is<IEnumerable<Guid>>(keys => keys.Contains(contentKey) && keys.Contains(siblingKey)), Arg.Any<string?>())
            .Returns(new[] { content, sibling });

        var mapping = CreateMapping("article", "Article");
        _repository.GetByContentTypeAlias("article").Returns(mapping);
        _repository.GetPropertyMappings(1).Returns(new List<PropertyMapping>
        {
            new()
            {
                SchemaPropertyName = "Name",
                SourceType = "sibling",
                SourceContentTypeAlias = "sidebarContent",
                ContentTypePropertyAlias = "promoText"
            }
        });

        var result = _sut.GenerateJsonLdString(content);

        result.Should().NotBeNullOrEmpty();
        result.Should().Contain("Special Offer");
    }

    [Fact]
    public void GenerateJsonLd_NullPropertyValue_HandledGracefully()
    {
        // Content has a property that returns null — should not throw
        var content = CreateContent("article", new Dictionary<string, object?>
        {
            ["headline"] = null
        });
        var mapping = CreateMapping("article", "Article");
        _repository.GetByContentTypeAlias("article").Returns(mapping);
        _repository.GetPropertyMappings(1).Returns(new List<PropertyMapping>
        {
            new() { SchemaPropertyName = "Headline", SourceType = "property", ContentTypePropertyAlias = "headline" }
        });

        var act = () => _sut.GenerateJsonLd(content);

        act.Should().NotThrow();
        var result = act();
        result.Should().NotBeNull();
        result.Should().BeOfType<Schema.NET.Article>();
    }

    [Fact]
    public void GenerateJsonLd_NoPropertyMappings_ReturnsThingWithTypeOnly()
    {
        var content = CreateContent("article");
        var mapping = CreateMapping("article", "Article");
        _repository.GetByContentTypeAlias("article").Returns(mapping);
        _repository.GetPropertyMappings(1).Returns(new List<PropertyMapping>());

        var result = _sut.GenerateJsonLd(content);

        result.Should().NotBeNull();
        result.Should().BeOfType<Schema.NET.Article>();

        var jsonLd = result!.ToString();
        jsonLd.Should().Contain("Article");
    }

    #endregion

    #region Culture / Variant Tests

    [Fact]
    public void GenerateJsonLd_WithCultureNull_PreservesExistingBehaviour()
    {
        var content = CreateContent("article", new Dictionary<string, object?>
        {
            ["headline"] = "Invariant Headline"
        });
        var mapping = CreateMapping("article", "Article");
        _repository.GetByContentTypeAlias("article").Returns(mapping);
        _repository.GetPropertyMappings(1).Returns(new List<PropertyMapping>
        {
            new() { SchemaPropertyName = "Headline", SourceType = "property", ContentTypePropertyAlias = "headline" }
        });

        var result = _sut.GenerateJsonLd(content, culture: null);

        result.Should().NotBeNull();
        result.Should().BeOfType<Schema.NET.Article>();
        var jsonLd = result!.ToString();
        jsonLd.Should().Contain("Invariant Headline");
        jsonLd.Should().NotContain("inLanguage");
    }

    [Fact]
    public void GenerateJsonLd_WithCulture_ReturnsVariantValues()
    {
        var content = Substitute.For<IPublishedContent>();
        var contentType = Substitute.For<IPublishedContentType>();
        contentType.Alias.Returns("article");
        content.ContentType.Returns(contentType);
        content.Id.Returns(1);
        content.Key.Returns(Guid.NewGuid());

        var property = Substitute.For<IPublishedProperty>();
        // Return different values per culture
        property.GetValue("de-DE", null).Returns("Deutscher Titel");
        property.GetValue(null, null).Returns("English Title");
        content.GetProperty("headline").Returns(property);

        var mapping = CreateMapping("article", "Article");
        _repository.GetByContentTypeAlias("article").Returns(mapping);
        _repository.GetPropertyMappings(1).Returns(new List<PropertyMapping>
        {
            new() { SchemaPropertyName = "Headline", SourceType = "property", ContentTypePropertyAlias = "headline" }
        });

        var result = _sut.GenerateJsonLd(content, culture: "de-DE");

        result.Should().NotBeNull();
        var jsonLd = result!.ToString();
        jsonLd.Should().Contain("Deutscher Titel");
    }

    [Fact]
    public void GenerateJsonLd_WithCulture_AutoFillsInLanguage()
    {
        var content = CreateContent("article", new Dictionary<string, object?>
        {
            ["headline"] = "Test"
        });
        var mapping = CreateMapping("article", "Article");
        _repository.GetByContentTypeAlias("article").Returns(mapping);
        _repository.GetPropertyMappings(1).Returns(new List<PropertyMapping>
        {
            new() { SchemaPropertyName = "Headline", SourceType = "property", ContentTypePropertyAlias = "headline" }
        });

        var result = _sut.GenerateJsonLd(content, culture: "de-DE");

        result.Should().NotBeNull();
        var jsonLd = result!.ToString();
        jsonLd.Should().Contain("de-DE");
        jsonLd.Should().Contain("inLanguage");
    }

    [Fact]
    public void GenerateJsonLd_WithCulture_ExplicitInLanguageMappingWins()
    {
        var content = CreateContent("article", new Dictionary<string, object?>
        {
            ["headline"] = "Test"
        });
        var mapping = CreateMapping("article", "Article");
        _repository.GetByContentTypeAlias("article").Returns(mapping);
        _repository.GetPropertyMappings(1).Returns(new List<PropertyMapping>
        {
            new() { SchemaPropertyName = "Headline", SourceType = "property", ContentTypePropertyAlias = "headline" },
            new() { SchemaPropertyName = "InLanguage", SourceType = "static", StaticValue = "fr-FR" }
        });

        var result = _sut.GenerateJsonLd(content, culture: "de-DE");

        result.Should().NotBeNull();
        var jsonLd = result!.ToString();
        jsonLd.Should().Contain("fr-FR");
        jsonLd.Should().NotContain("de-DE");
    }

    [Fact]
    public void GenerateJsonLd_WithCulture_SetsAndRestoresVariationContext()
    {
        var content = CreateContent("article");
        _repository.GetByContentTypeAlias("article").Returns((SchemaMapping?)null);

        var originalContext = new VariationContext("en-US");
        _variationContextAccessor.VariationContext = originalContext;

        _sut.GenerateJsonLd(content, culture: "de-DE");

        _variationContextAccessor.VariationContext.Should().BeSameAs(originalContext);
    }

    [Fact]
    public void GenerateJsonLd_WithNullCulture_DoesNotSetVariationContext()
    {
        var content = CreateContent("article");
        _repository.GetByContentTypeAlias("article").Returns((SchemaMapping?)null);

        VariationContext? originalContext = null;
        _variationContextAccessor.VariationContext = originalContext;

        _sut.GenerateJsonLd(content, culture: null);

        _variationContextAccessor.VariationContext.Should().BeNull();
    }

    #endregion

    #region @id override

    [Fact]
    public void ExpandIdTokens_ExpandsAllKnownTokens()
    {
        var key = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");

        var result = JsonLdGenerator.ExpandIdTokens(
            "{siteUrl}/{culture}/entity/{key}#{type}",
            contentUrl: "https://example.com/about/",
            siteUrl: "https://example.com",
            schemaTypeName: "RealEstateAgent",
            contentKey: key,
            culture: "en-gb");

        result.Should().Be("https://example.com/en-gb/entity/aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee#realestateagent");
    }

    [Fact]
    public void ExpandIdTokens_MissingContextValues_ExpandToEmptyString()
    {
        var result = JsonLdGenerator.ExpandIdTokens(
            "{siteUrl}#{type}",
            contentUrl: null,
            siteUrl: null,
            schemaTypeName: "Organization",
            contentKey: Guid.Empty,
            culture: null);

        result.Should().Be("#organization");
    }

    [Fact]
    public void GenerateJsonLd_WithIdOverride_UsesExpandedOverride()
    {
        var content = CreateContent("siteSettings");
        content.Key.Returns(Guid.Parse("11111111-2222-3333-4444-555555555555"));
        _urlProvider.GetUrl(content, UrlMode.Absolute).Returns("https://example.com/about/");

        var httpContext = new DefaultHttpContext();
        httpContext.Request.Scheme = "https";
        httpContext.Request.Host = new HostString("example.com");
        _httpContextAccessor.HttpContext.Returns(httpContext);

        var mapping = CreateMapping("siteSettings", "Organization");
        mapping.IdOverride = "{siteUrl}#organization";
        _repository.GetByContentTypeAlias("siteSettings").Returns(mapping);
        _repository.GetPropertyMappings(1).Returns(new List<PropertyMapping>());

        var result = _sut.GenerateJsonLd(content);

        result.Should().NotBeNull();
        result!.Id.Should().NotBeNull();
        result.Id!.ToString().Should().Be("https://example.com/#organization");
    }

    [Fact]
    public void GenerateJsonLd_WithoutIdOverride_FallsBackToDefault()
    {
        var content = CreateContent("article");
        _urlProvider.GetUrl(content, UrlMode.Absolute).Returns("https://example.com/blog/post/");

        var mapping = CreateMapping("article", "Article");
        mapping.IdOverride = null;
        _repository.GetByContentTypeAlias("article").Returns(mapping);
        _repository.GetPropertyMappings(1).Returns(new List<PropertyMapping>());

        var result = _sut.GenerateJsonLd(content);

        result.Should().NotBeNull();
        result!.Id!.ToString().Should().Be("https://example.com/blog/post/#article");
    }

    [Fact]
    public void GenerateJsonLd_ExplicitIdPropertyMapping_BeatsIdOverride()
    {
        var content = CreateContent("article", new Dictionary<string, object?>
        {
            ["customId"] = "https://example.com/explicit/#thing"
        });
        _urlProvider.GetUrl(content, UrlMode.Absolute).Returns("https://example.com/blog/post/");

        var mapping = CreateMapping("article", "Article");
        mapping.IdOverride = "{url}#override-wins-over-default-but-not-over-explicit";
        _repository.GetByContentTypeAlias("article").Returns(mapping);
        _repository.GetPropertyMappings(1).Returns(new List<PropertyMapping>
        {
            new() { SchemaPropertyName = "Id", SourceType = "property", ContentTypePropertyAlias = "customId" }
        });

        var result = _sut.GenerateJsonLd(content);

        result.Should().NotBeNull();
        result!.Id!.ToString().Should().Be("https://example.com/explicit/#thing");
    }

    #endregion

    #region Media logo complexType regression (broken persisted shape + empty shells)

    /// <summary>
    /// Generator wired with the real <see cref="MediaPickerResolver"/> so complexType
    /// sub-mappings that point at a MediaPicker3 property resolve through the factory
    /// exactly as production does.
    /// </summary>
    private JsonLdGenerator CreateMediaAwareGenerator()
    {
        var factory = new PropertyValueResolverFactory([
            new MediaPickerResolver(NullLogger<MediaPickerResolver>.Instance, _urlProvider),
            new DefaultPropertyValueResolver()
        ]);
        return new JsonLdGenerator(
            _repository, _registry, _httpContextAccessor,
            _navigationQueryService, _publishedStatusFilteringService,
            factory, _urlProvider, _variationContextAccessor,
            _logger, Options.Create(new SchemeWeaverOptions()));
    }

    /// <summary>
    /// Content node carrying one MediaPicker3 property whose media resolves to
    /// <paramref name="mediaUrl"/> via the shared <see cref="_urlProvider"/> stub
    /// (MediaPickerResolverTests fixture style: MediaWithCrops + url provider stub).
    /// </summary>
    private IPublishedContent CreateContentWithMediaProperty(
        string contentTypeAlias, string mediaPropertyAlias, string mediaUrl)
    {
        var content = Substitute.For<IPublishedContent>();
        var contentType = Substitute.For<IPublishedContentType>();
        contentType.Alias.Returns(contentTypeAlias);
        content.ContentType.Returns(contentType);
        content.Id.Returns(1);
        content.Key.Returns(Guid.NewGuid());

        var media = Substitute.For<IPublishedContent>();
        _urlProvider
            .GetMediaUrl(media, UrlMode.Absolute, Arg.Any<string?>(), Arg.Any<string>(), Arg.Any<Uri?>())
            .Returns(mediaUrl);
        var mediaWithCrops = new MediaWithCrops(
            media, Substitute.For<IPublishedValueFallback>(), new ImageCropperValue());

        var property = Substitute.For<IPublishedProperty>();
        var propertyType = Substitute.For<IPublishedPropertyType>();
        propertyType.EditorAlias.Returns("Umbraco.MediaPicker3");
        property.PropertyType.Returns(propertyType);
        property.GetValue(Arg.Any<string?>(), Arg.Any<string?>()).Returns(mediaWithCrops);
        content.GetProperty(mediaPropertyAlias).Returns(property);

        return content;
    }

    [Fact]
    public void GenerateJsonLd_PersistedBrokenLogoShape_EmitsImageObjectWithUrl()
    {
        HermeticStaticServiceProvider.EnsureInstalled();

        // The shape SchemaAutoMapper + StructuralEnricher persisted for MediaPicker logos:
        // complexType/ImageObject binding ImageObject.Name <- the media property alias.
        // At render time the media resolves to a full ImageObject, which cannot be set into
        // the string-only ImageObject.Name — the resolved media must repair into the emitted
        // logo (url populated), not be silently dropped leaving an empty shell.
        var content = CreateContentWithMediaProperty(
            "siteSettings", "logo", "https://example.com/media/brand/logo.png");
        var mapping = CreateMapping("siteSettings", "Organization");
        _repository.GetByContentTypeAlias("siteSettings").Returns(mapping);
        _repository.GetPropertyMappings(1).Returns(new List<PropertyMapping>
        {
            new()
            {
                SchemaPropertyName = "Logo",
                SourceType = "complexType",
                NestedSchemaTypeName = "ImageObject",
                ResolverConfig =
                    """{"complexTypeMappings":[{"schemaProperty":"Name","sourceType":"property","contentTypePropertyAlias":"logo"}]}"""
            }
        });

        var sut = CreateMediaAwareGenerator();
        var result = sut.GenerateJsonLd(content);

        result.Should().NotBeNull();
        using var doc = JsonDocument.Parse(result!.ToString());
        doc.RootElement.TryGetProperty("logo", out var logo)
            .Should().BeTrue("the mapped logo must be emitted");
        logo.GetProperty("@type").GetString().Should().Be("ImageObject");
        logo.TryGetProperty("url", out var url)
            .Should().BeTrue("the logo must carry the resolved media URL, not be an empty ImageObject shell");
        url.GetString().Should().Be("https://example.com/media/brand/logo.png");
    }

    /// <summary>
    /// Content node carrying one MULTI-select MediaPicker3 property whose value is an
    /// <see cref="IEnumerable{MediaWithCrops}"/> — the resolver then returns a
    /// <c>List&lt;ImageObject&gt;</c>, exercising <c>FirstImageObject</c>'s <c>many</c> branch.
    /// </summary>
    private IPublishedContent CreateContentWithMultipleMediaProperty(
        string contentTypeAlias, string mediaPropertyAlias, params string[] mediaUrls)
    {
        var content = Substitute.For<IPublishedContent>();
        var contentType = Substitute.For<IPublishedContentType>();
        contentType.Alias.Returns(contentTypeAlias);
        content.ContentType.Returns(contentType);
        content.Id.Returns(1);
        content.Key.Returns(Guid.NewGuid());

        var items = new List<MediaWithCrops>();
        foreach (var mediaUrl in mediaUrls)
        {
            var media = Substitute.For<IPublishedContent>();
            _urlProvider
                .GetMediaUrl(media, UrlMode.Absolute, Arg.Any<string?>(), Arg.Any<string>(), Arg.Any<Uri?>())
                .Returns(mediaUrl);
            items.Add(new MediaWithCrops(
                media, Substitute.For<IPublishedValueFallback>(), new ImageCropperValue()));
        }

        var property = Substitute.For<IPublishedProperty>();
        var propertyType = Substitute.For<IPublishedPropertyType>();
        propertyType.EditorAlias.Returns("Umbraco.MediaPicker3");
        property.PropertyType.Returns(propertyType);
        property.GetValue(Arg.Any<string?>(), Arg.Any<string?>()).Returns(items);
        content.GetProperty(mediaPropertyAlias).Returns(property);

        return content;
    }

    [Fact]
    public void GenerateJsonLd_PersistedBrokenLogoShape_MultiSelectMedia_EmitsImageObjectWithUrl()
    {
        HermeticStaticServiceProvider.EnsureInstalled();

        // Same persisted broken shape, but the bound MediaPicker3 is MULTI-select so the
        // resolver returns a List<ImageObject> rather than a single ImageObject. The adoption
        // repair must still fire via FirstImageObject's `many` branch — adopt the first resolved
        // ImageObject as the nested instance and emit a populated logo (single-image adoption
        // picking the first is acceptable behaviour).
        var content = CreateContentWithMultipleMediaProperty(
            "siteSettings", "logos",
            "https://example.com/media/brand/logo-a.png",
            "https://example.com/media/brand/logo-b.png");
        var mapping = CreateMapping("siteSettings", "Organization");
        _repository.GetByContentTypeAlias("siteSettings").Returns(mapping);
        _repository.GetPropertyMappings(1).Returns(new List<PropertyMapping>
        {
            new()
            {
                SchemaPropertyName = "Logo",
                SourceType = "complexType",
                NestedSchemaTypeName = "ImageObject",
                ResolverConfig =
                    """{"complexTypeMappings":[{"schemaProperty":"Name","sourceType":"property","contentTypePropertyAlias":"logos"}]}"""
            }
        });

        var sut = CreateMediaAwareGenerator();
        var result = sut.GenerateJsonLd(content);

        result.Should().NotBeNull();
        using var doc = JsonDocument.Parse(result!.ToString());
        doc.RootElement.TryGetProperty("logo", out var logo)
            .Should().BeTrue("the mapped logo must be emitted");
        logo.GetProperty("@type").GetString().Should().Be("ImageObject");
        logo.TryGetProperty("url", out var url)
            .Should().BeTrue("the adopted first image must carry a resolved media URL, not be an empty shell");
        url.GetString().Should().Be("https://example.com/media/brand/logo-a.png");
    }

    [Fact]
    public void MediaLogoAdoption_ValidatorAndRenderAgree()
    {
        HermeticStaticServiceProvider.EnsureInstalled();

        // Pins the G2 (render adopt) and G3 (validator warn) seams to AGREE on the exact
        // logo -> complexType/ImageObject with Name <- media shape: the validator must report
        // NO issue AND the render must emit a populated ImageObject. A disagreement here — a
        // warning telling users to "fix" a mapping the render renders correctly — is the bug.
        const string resolverConfig =
            """{"complexTypeMappings":[{"schemaProperty":"Name","sourceType":"property","contentTypePropertyAlias":"logo"}]}""";

        // Validator leg: real registry + checker + a content-type service exposing the media picker.
        var registry = new SchemaTypeRegistry();
        registry.EnsureInitialised();
        var logoProp = Substitute.For<IPropertyType>();
        logoProp.Alias.Returns("logo");
        logoProp.PropertyEditorAlias.Returns("Umbraco.MediaPicker3");
        var contentType = Substitute.For<IContentType>();
        contentType.CompositionPropertyTypes.Returns(new[] { logoProp });
        var contentTypeService = Substitute.For<IContentTypeService>();
        contentTypeService.Get("siteSettings").Returns(contentType);
        var validator = new SchemaRangeValidator(
            registry, new SchemaRangeChecker(registry), contentTypeService);

        var dto = new SchemaMappingDto
        {
            ContentTypeAlias = "siteSettings",
            SchemaTypeName = "Organization",
            IsEnabled = true,
            PropertyMappings =
            [
                new PropertyMappingDto
                {
                    SchemaPropertyName = "Logo",
                    SourceType = "complexType",
                    NestedSchemaTypeName = "ImageObject",
                    ResolverConfig = resolverConfig
                }
            ]
        };

        validator.Validate(dto).Should().BeEmpty(
            "the render adopts the media as the ImageObject, so the validator must not warn");

        // Render leg: the SAME shape must emit a populated ImageObject carrying the media URL.
        var content = CreateContentWithMediaProperty(
            "siteSettings", "logo", "https://example.com/media/brand/logo.png");
        _repository.GetByContentTypeAlias("siteSettings")
            .Returns(CreateMapping("siteSettings", "Organization"));
        _repository.GetPropertyMappings(1).Returns(new List<PropertyMapping>
        {
            new()
            {
                SchemaPropertyName = "Logo",
                SourceType = "complexType",
                NestedSchemaTypeName = "ImageObject",
                ResolverConfig = resolverConfig
            }
        });

        var result = CreateMediaAwareGenerator().GenerateJsonLd(content);

        result.Should().NotBeNull();
        using var doc = JsonDocument.Parse(result!.ToString());
        doc.RootElement.TryGetProperty("logo", out var logo)
            .Should().BeTrue("the mapped logo must be emitted");
        logo.GetProperty("@type").GetString().Should().Be("ImageObject");
        logo.GetProperty("url").GetString().Should().Be("https://example.com/media/brand/logo.png");
    }

    [Fact]
    public void GenerateJsonLd_ComplexTypeImage_MediaSubPropertyInRange_IsNotAdopted()
    {
        HermeticStaticServiceProvider.EnsureInstalled();

        // A VALID persisted shape: complexType/ImageObject whose media sub-mapping targets
        // ImageObject.Thumbnail — a sub-property whose range ACCEPTS an ImageObject — plus a
        // static contentUrl. The broken-shape adoption repair must NOT hijack it: the media
        // stays nested under 'thumbnail' and the static contentUrl lands on the OUTER image,
        // exactly as configured (and exactly as the range validator blesses).
        var content = CreateContentWithMediaProperty(
            "article", "thumbMedia", "https://example.com/media/thumb.png");
        var mapping = CreateMapping("article", "Article");
        _repository.GetByContentTypeAlias("article").Returns(mapping);
        _repository.GetPropertyMappings(1).Returns(new List<PropertyMapping>
        {
            new()
            {
                SchemaPropertyName = "Image",
                SourceType = "complexType",
                NestedSchemaTypeName = "ImageObject",
                ResolverConfig =
                    """{"complexTypeMappings":[{"schemaProperty":"Thumbnail","sourceType":"property","contentTypePropertyAlias":"thumbMedia"},{"schemaProperty":"ContentUrl","sourceType":"static","staticValue":"https://example.com/media/full.png"}]}"""
            }
        });

        var sut = CreateMediaAwareGenerator();
        var result = sut.GenerateJsonLd(content);

        result.Should().NotBeNull();
        using var doc = JsonDocument.Parse(result!.ToString());
        doc.RootElement.TryGetProperty("image", out var image)
            .Should().BeTrue("the mapped image must be emitted");
        image.GetProperty("@type").GetString().Should().Be("ImageObject");
        image.GetProperty("contentUrl").GetString().Should().Be(
            "https://example.com/media/full.png",
            "the static contentUrl belongs on the OUTER image, not stamped onto an adopted thumbnail");
        image.TryGetProperty("thumbnail", out var thumbnail).Should().BeTrue(
            "the media sub-mapping targets 'thumbnail', which accepts an ImageObject — it must bind there");
        thumbnail.GetProperty("url").GetString().Should().Be("https://example.com/media/thumb.png");
        image.TryGetProperty("url", out _).Should().BeFalse(
            "no sub-mapping sets the outer image url — its presence would mean the thumbnail media was adopted");
    }

    [Fact]
    public void GenerateJsonLd_ComplexTypeAllSubValuesNull_OmitsEmptyShell()
    {
        // A complexType whose configured sub-values ALL resolve to null must be omitted
        // from the output entirely — emitting {"@type":"Person"} (or ImageObject) shells
        // is invalid structured data and is exactly the visible symptom of the logo trap.
        var content = CreateContent("article", new Dictionary<string, object?>
        {
            ["headline"] = "Test Article"
        });
        var mapping = CreateMapping("article", "Article");
        _repository.GetByContentTypeAlias("article").Returns(mapping);
        _repository.GetPropertyMappings(1).Returns(new List<PropertyMapping>
        {
            new() { SchemaPropertyName = "Headline", SourceType = "property", ContentTypePropertyAlias = "headline" },
            new()
            {
                SchemaPropertyName = "Author",
                SourceType = "complexType",
                NestedSchemaTypeName = "Person",
                ResolverConfig =
                    """{"complexTypeMappings":[{"schemaProperty":"Name","sourceType":"property","contentTypePropertyAlias":"authorName"}]}"""
            }
        });

        var result = _sut.GenerateJsonLd(content);

        result.Should().NotBeNull();
        using var doc = JsonDocument.Parse(result!.ToString());
        doc.RootElement.TryGetProperty("author", out _).Should().BeFalse(
            "a complexType whose sub-values all resolve null must be omitted, " +
            "not emitted as an empty {\"@type\":\"Person\"} shell");
    }

    #endregion

    #region Related-node complexType sub-rows (issue #32's literal shape)

    private JsonLdGenerator CreateSutWithFullResolverFactory()
    {
        var factory = new PropertyValueResolverFactory(new IPropertyValueResolver[]
        {
            new DefaultPropertyValueResolver(),
            new BuiltInPropertyResolver(_urlProvider),
            new ContentPickerResolver(),
            new MultiNodeTreePickerResolver()
        });
        return new JsonLdGenerator(
            _repository, _registry, _httpContextAccessor, _navigationQueryService,
            _publishedStatusFilteringService, factory, _urlProvider,
            _variationContextAccessor, _logger, Options.Create(new SchemeWeaverOptions()));
    }

    /// <summary>
    /// Mocks the ancestor chain child → parent → grandparent for Ancestors() resolution.
    /// </summary>
    private void StubAncestors(IPublishedContent child, params IPublishedContent[] ancestorsNearestFirst)
    {
        var keys = ancestorsNearestFirst.Select(a => a.Key).ToArray();
        _navigationQueryService.TryGetAncestorsKeys(child.Key, out Arg.Any<IEnumerable<Guid>>())
            .Returns(callInfo =>
            {
                callInfo[1] = keys;
                return true;
            });
        _publishedStatusFilteringService
            .FilterAvailable(Arg.Any<IEnumerable<Guid>>(), Arg.Any<string?>())
            .Returns(callInfo =>
            {
                var requested = ((IEnumerable<Guid>)callInfo[0]).ToHashSet();
                return ancestorsNearestFirst.Where(n => requested.Contains(n.Key)).ToArray();
            });
        StubUnfilteredResolution(ancestorsNearestFirst);
    }

    [Fact]
    public void ComplexType_AncestorSubRow_ResolvesFromAncestor()
    {
        // The literal #32 ask: article author → Organization whose nested name reads the site root.
        var root = CreateContent("homePage", new Dictionary<string, object?>
        {
            ["organisationName"] = "Enjoy Digital"
        });
        var article = CreateContent("article");
        StubAncestors(article, root);

        var mapping = CreateMapping("article", "Article");
        _repository.GetByContentTypeAlias("article").Returns(mapping);
        _repository.GetPropertyMappings(1).Returns(new List<PropertyMapping>
        {
            new()
            {
                SchemaPropertyName = "Author",
                SourceType = "complexType",
                NestedSchemaTypeName = "Organization",
                ResolverConfig =
                    """{"complexTypeMappings":[{"schemaProperty":"Name","sourceType":"ancestor","sourceContentTypeAlias":"homePage","contentTypePropertyAlias":"organisationName"}]}"""
            }
        });

        var result = _sut.GenerateJsonLd(article);

        result.Should().NotBeNull();
        using var doc = JsonDocument.Parse(result!.ToString());
        var author = doc.RootElement.GetProperty("author");
        author.GetProperty("@type").GetString().Should().Be("Organization");
        author.GetProperty("name").GetString().Should().Be("Enjoy Digital");
    }

    [Fact]
    public void ComplexType_MixedAncestorAndPropertySubRows_BothResolve()
    {
        var root = CreateContent("homePage", new Dictionary<string, object?>
        {
            ["organisationName"] = "Enjoy Digital"
        });
        var article = CreateContent("article", new Dictionary<string, object?>
        {
            ["authorRole"] = "Publisher"
        });
        StubAncestors(article, root);

        var mapping = CreateMapping("article", "Article");
        _repository.GetByContentTypeAlias("article").Returns(mapping);
        _repository.GetPropertyMappings(1).Returns(new List<PropertyMapping>
        {
            new()
            {
                SchemaPropertyName = "Author",
                SourceType = "complexType",
                NestedSchemaTypeName = "Organization",
                ResolverConfig =
                    """{"complexTypeMappings":[{"schemaProperty":"Name","sourceType":"ancestor","sourceContentTypeAlias":"homePage","contentTypePropertyAlias":"organisationName"},{"schemaProperty":"Description","sourceType":"property","contentTypePropertyAlias":"authorRole"}]}"""
            }
        });

        var result = _sut.GenerateJsonLd(article);

        using var doc = JsonDocument.Parse(result!.ToString());
        var author = doc.RootElement.GetProperty("author");
        author.GetProperty("name").GetString().Should().Be("Enjoy Digital");
        author.GetProperty("description").GetString().Should().Be("Publisher");
    }

    [Fact]
    public void ComplexType_AncestorSubRow_NoMatchingAncestor_OmitsNestedThing()
    {
        var unrelatedAncestor = CreateContent("sectionPage");
        var article = CreateContent("article");
        StubAncestors(article, unrelatedAncestor);

        var mapping = CreateMapping("article", "Article");
        _repository.GetByContentTypeAlias("article").Returns(mapping);
        _repository.GetPropertyMappings(1).Returns(new List<PropertyMapping>
        {
            new()
            {
                SchemaPropertyName = "Author",
                SourceType = "complexType",
                NestedSchemaTypeName = "Organization",
                ResolverConfig =
                    """{"complexTypeMappings":[{"schemaProperty":"Name","sourceType":"ancestor","sourceContentTypeAlias":"homePage","contentTypePropertyAlias":"organisationName"}]}"""
            }
        });

        var result = _sut.GenerateJsonLd(article);

        result.Should().NotBeNull();
        using var doc = JsonDocument.Parse(result!.ToString());
        doc.RootElement.TryGetProperty("author", out _).Should().BeFalse(
            "when the ancestor filter matches nothing the sub-row resolves null and " +
            "the empty-shell guard must omit the nested Thing entirely");
    }

    [Fact]
    public void ComplexType_ParentSubRow_ResolvesFromParent()
    {
        var parent = CreateContent("blogListing", new Dictionary<string, object?>
        {
            ["sectionTitle"] = "Engineering Blog"
        });
        var article = CreateContent("article");

        _navigationQueryService.TryGetParentKey(article.Key, out Arg.Any<Guid?>())
            .Returns(callInfo =>
            {
                callInfo[1] = (Guid?)parent.Key;
                return true;
            });
        _publishedStatusFilteringService
            .FilterAvailable(Arg.Is<IEnumerable<Guid>>(keys => keys.Contains(parent.Key)), Arg.Any<string?>())
            .Returns(new[] { parent });
        StubUnfilteredResolution(parent);

        var mapping = CreateMapping("article", "Article");
        _repository.GetByContentTypeAlias("article").Returns(mapping);
        _repository.GetPropertyMappings(1).Returns(new List<PropertyMapping>
        {
            new()
            {
                SchemaPropertyName = "Publisher",
                SourceType = "complexType",
                NestedSchemaTypeName = "Organization",
                ResolverConfig =
                    """{"complexTypeMappings":[{"schemaProperty":"Name","sourceType":"parent","contentTypePropertyAlias":"sectionTitle"}]}"""
            }
        });

        var result = _sut.GenerateJsonLd(article);

        using var doc = JsonDocument.Parse(result!.ToString());
        doc.RootElement.GetProperty("publisher").GetProperty("name").GetString()
            .Should().Be("Engineering Blog");
    }

    [Fact]
    public void ComplexType_AncestorSubRow_BuiltInName_ResolvesAgainstAncestor()
    {
        var root = CreateContent("homePage", new Dictionary<string, object?>
        {
            // has to HAVE the probe property? Built-ins short-circuit the probe, so no.
        });
        root.Name.Returns("Enjoy Digital Site");
        var article = CreateContent("article");
        StubAncestors(article, root);

        var mapping = CreateMapping("article", "Article");
        _repository.GetByContentTypeAlias("article").Returns(mapping);
        _repository.GetPropertyMappings(1).Returns(new List<PropertyMapping>
        {
            new()
            {
                SchemaPropertyName = "Author",
                SourceType = "complexType",
                NestedSchemaTypeName = "Organization",
                ResolverConfig =
                    """{"complexTypeMappings":[{"schemaProperty":"Name","sourceType":"ancestor","sourceContentTypeAlias":"homePage","contentTypePropertyAlias":"__name"}]}"""
            }
        });

        var sut = CreateSutWithFullResolverFactory();
        var result = sut.GenerateJsonLd(article);

        using var doc = JsonDocument.Parse(result!.ToString());
        doc.RootElement.GetProperty("author").GetProperty("name").GetString()
            .Should().Be("Enjoy Digital Site");
    }

    [Fact]
    public void ComplexType_PropertySubRow_BuiltInName_ResolvesAgainstPage()
    {
        // Regression: __name in a plain property sub-row used to silently resolve null.
        var article = CreateContent("article");
        article.Name.Returns("The Article Title");

        var mapping = CreateMapping("article", "Article");
        _repository.GetByContentTypeAlias("article").Returns(mapping);
        _repository.GetPropertyMappings(1).Returns(new List<PropertyMapping>
        {
            new()
            {
                SchemaPropertyName = "Author",
                SourceType = "complexType",
                NestedSchemaTypeName = "Person",
                ResolverConfig =
                    """{"complexTypeMappings":[{"schemaProperty":"Name","sourceType":"property","contentTypePropertyAlias":"__name"}]}"""
            }
        });

        var sut = CreateSutWithFullResolverFactory();
        var result = sut.GenerateJsonLd(article);

        using var doc = JsonDocument.Parse(result!.ToString());
        doc.RootElement.GetProperty("author").GetProperty("name").GetString()
            .Should().Be("The Article Title");
    }

    [Fact]
    public void ComplexType_AncestorSubRow_StripHtmlTransform_Applies()
    {
        var root = CreateContent("homePage", new Dictionary<string, object?>
        {
            ["strapline"] = "<p>Weaving <strong>schemas</strong></p>"
        });
        var article = CreateContent("article");
        StubAncestors(article, root);

        var mapping = CreateMapping("article", "Article");
        _repository.GetByContentTypeAlias("article").Returns(mapping);
        _repository.GetPropertyMappings(1).Returns(new List<PropertyMapping>
        {
            new()
            {
                SchemaPropertyName = "Author",
                SourceType = "complexType",
                NestedSchemaTypeName = "Organization",
                ResolverConfig =
                    """{"complexTypeMappings":[{"schemaProperty":"Name","sourceType":"ancestor","sourceContentTypeAlias":"homePage","contentTypePropertyAlias":"strapline","transformType":"stripHtml"}]}"""
            }
        });

        var result = _sut.GenerateJsonLd(article);

        using var doc = JsonDocument.Parse(result!.ToString());
        doc.RootElement.GetProperty("author").GetProperty("name").GetString()
            .Should().Be("Weaving schemas");
    }

    [Fact]
    public void ComplexType_NestedComplexType_AncestorSubRow_StillResolvesRelativeToPage()
    {
        // Related-node sub-rows one nesting level down must still walk from the PAGE.
        var root = CreateContent("homePage", new Dictionary<string, object?>
        {
            ["organisationName"] = "Enjoy Digital"
        });
        var article = CreateContent("article", new Dictionary<string, object?>
        {
            ["authorName"] = "Jane Doe"
        });
        StubAncestors(article, root);

        var mapping = CreateMapping("article", "Article");
        _repository.GetByContentTypeAlias("article").Returns(mapping);
        _repository.GetPropertyMappings(1).Returns(new List<PropertyMapping>
        {
            new()
            {
                SchemaPropertyName = "Author",
                SourceType = "complexType",
                NestedSchemaTypeName = "Person",
                ResolverConfig =
                    """{"complexTypeMappings":[{"schemaProperty":"Name","sourceType":"property","contentTypePropertyAlias":"authorName"},{"schemaProperty":"WorksFor","sourceType":"complexType","resolverConfig":"{\"selectedSubType\":\"Organization\",\"complexTypeMappings\":[{\"schemaProperty\":\"Name\",\"sourceType\":\"ancestor\",\"sourceContentTypeAlias\":\"homePage\",\"contentTypePropertyAlias\":\"organisationName\"}]}"}]}"""
            }
        });

        var result = _sut.GenerateJsonLd(article);

        using var doc = JsonDocument.Parse(result!.ToString());
        var author = doc.RootElement.GetProperty("author");
        author.GetProperty("name").GetString().Should().Be("Jane Doe");
        author.GetProperty("worksFor").GetProperty("name").GetString().Should().Be("Enjoy Digital");
    }

    [Fact]
    public void GenerateJsonLd_PickerDrillDown_EmitsDrilledScalar()
    {
        // Feature A end-to-end through the generator: author ← picked node's jobTitle.
        var pickedType = Substitute.For<IPublishedContentType>();
        pickedType.Alias.Returns("author");

        var jobTitleType = Substitute.For<IPublishedPropertyType>();
        jobTitleType.EditorAlias.Returns("Umbraco.TextBox");
        var jobTitle = Substitute.For<IPublishedProperty>();
        jobTitle.GetValue(Arg.Any<string?>(), Arg.Any<string?>()).Returns("Principal Developer");
        jobTitle.PropertyType.Returns(jobTitleType);

        var picked = Substitute.For<IPublishedContent>();
        picked.Name.Returns("Jane Doe");
        picked.Key.Returns(Guid.NewGuid());
        picked.ContentType.Returns(pickedType);
        picked.GetProperty("jobTitle").Returns(jobTitle);

        var pickerType = Substitute.For<IPublishedPropertyType>();
        pickerType.EditorAlias.Returns("Umbraco.ContentPicker");
        var pickerProperty = Substitute.For<IPublishedProperty>();
        pickerProperty.GetValue(Arg.Any<string?>(), Arg.Any<string?>()).Returns(picked);
        pickerProperty.PropertyType.Returns(pickerType);

        var article = CreateContent("article");
        article.GetProperty("authorNode").Returns(pickerProperty);

        var mapping = CreateMapping("article", "Article");
        _repository.GetByContentTypeAlias("article").Returns(mapping);
        _repository.GetPropertyMappings(1).Returns(new List<PropertyMapping>
        {
            new()
            {
                SchemaPropertyName = "Author",
                SourceType = "property",
                ContentTypePropertyAlias = "authorNode",
                NestedSchemaTypeName = "Person",
                ResolverConfig = """{"pickedPropertyAlias":"jobTitle","pickedContentTypeAlias":"author"}"""
            }
        });

        var sut = CreateSutWithFullResolverFactory();
        var result = sut.GenerateJsonLd(article);

        using var doc = JsonDocument.Parse(result!.ToString());
        var author = doc.RootElement.GetProperty("author");
        // The drilled scalar may be emitted verbatim or range-adopted into a typed
        // object ({"@type":"Person","name":…}) by the setter — both carry the value.
        var emitted = author.ValueKind == JsonValueKind.String
            ? author.GetString()
            : author.GetProperty("name").GetString();
        emitted.Should().Be("Principal Developer");
        result.ToString().Should().NotContain("Jane Doe",
            "drill-down must emit the drilled property, not the picked node's name");
    }

    #endregion
}
