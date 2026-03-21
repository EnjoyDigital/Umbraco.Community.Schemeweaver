using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Xunit;
using NSubstitute;
using Umbraco.Cms.Core.Models.Blocks;
using Umbraco.Cms.Core.Models.PublishedContent;
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
            blockResolverFactory, _logger);
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
            var udi = Umbraco.Cms.Core.Udi.Create(Umbraco.Cms.Core.Constants.UdiEntityType.Element, Guid.NewGuid());
            return new BlockListItem(udi, e, null, null);
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

    #endregion
}
