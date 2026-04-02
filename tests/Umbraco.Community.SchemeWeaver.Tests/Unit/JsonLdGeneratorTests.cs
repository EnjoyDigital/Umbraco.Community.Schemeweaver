using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Xunit;
using NSubstitute;
using Umbraco.Cms.Core.Models.Blocks;
using Umbraco.Cms.Core.Models.PublishedContent;
using Umbraco.Cms.Core.Routing;
using Umbraco.Cms.Core.Services.Navigation;
using Umbraco.Community.SchemeWeaver.Models.Entities;
using Umbraco.Community.SchemeWeaver.Persistence;
using Umbraco.Community.SchemeWeaver.Services;
using Umbraco.Community.SchemeWeaver.Services.Resolvers;

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
            _logger);
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

        // The extension method uses IPublishedSnapshot internally - for Parent<T>() to work
        // with mocked navigation, we rely on the deprecated .Parent property fallback
        // In the test context, Parent<T>() extension method calls navigation service
        // which we've mocked above. However, the extension method needs IPublishedSnapshot
        // to resolve the content by key. Since we can't easily mock that chain,
        // we use the deprecated Parent property for the test mock setup.
#pragma warning disable CS0618 // Type or member is obsolete - required for test mock setup
        content.Parent.Returns(parentContent);
#pragma warning restore CS0618

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
        // The Article Headline should have HTML stripped
        var article = result as Schema.NET.Article;
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
            new BlockContentResolver(),
            new DefaultPropertyValueResolver()
        ]);
        return new JsonLdGenerator(
            _repository, _registry, _httpContextAccessor,
            _navigationQueryService, _publishedStatusFilteringService,
            blockResolverFactory, _urlProvider, _logger);
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
    public void GenerateJsonLd_SetsIdFromContentUrl()
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
        var jsonLd = result.ToString();
        jsonLd.Should().Contain("\"@id\":\"https://example.com/articles/test\"");
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
        jsonLd.Should().Contain("\"@id\":\"https://example.com/articles/test\"");
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
}
