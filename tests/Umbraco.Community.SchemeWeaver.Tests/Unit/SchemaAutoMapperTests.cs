using FluentAssertions;
using NSubstitute;
using Xunit;
using Umbraco.Cms.Core.Models;
using Umbraco.Cms.Core.Services;
using Umbraco.Community.SchemeWeaver.Services;

namespace Umbraco.Community.SchemeWeaver.Tests.Unit;

public class SchemaAutoMapperTests
{
    private readonly IContentTypeService _contentTypeService = Substitute.For<IContentTypeService>();
    private readonly ISchemaTypeRegistry _schemaTypeRegistry = Substitute.For<ISchemaTypeRegistry>();
    private readonly SchemaAutoMapper _sut;

    public SchemaAutoMapperTests()
    {
        _sut = new SchemaAutoMapper(_contentTypeService, _schemaTypeRegistry);
    }

    private IContentType CreateContentTypeWithProperties(params string[] propertyAliases)
    {
        var contentType = Substitute.For<IContentType>();
        var propertyTypes = propertyAliases.Select(alias =>
        {
            var pt = Substitute.For<IPropertyType>();
            pt.Alias.Returns(alias);
            return pt;
        }).ToList();
        contentType.PropertyTypes.Returns(propertyTypes);
        return contentType;
    }

    private IContentType CreateContentTypeWithEditors(params (string alias, string editorAlias)[] properties)
    {
        var contentType = Substitute.For<IContentType>();
        var propertyTypes = properties.Select(p =>
        {
            var pt = Substitute.For<IPropertyType>();
            pt.Alias.Returns(p.alias);
            pt.PropertyEditorAlias.Returns(p.editorAlias);
            return pt;
        }).ToList();
        contentType.PropertyTypes.Returns(propertyTypes);
        return contentType;
    }

    [Fact]
    public void SuggestMappings_ExactMatch_ReturnsConfidence100()
    {
        var contentType = CreateContentTypeWithProperties("headline");
        _contentTypeService.Get("article").Returns(contentType);
        _schemaTypeRegistry.GetProperties("Article").Returns(new[]
        {
            new SchemaPropertyInfo { Name = "Headline", PropertyType = "Text" }
        });

        var result = _sut.SuggestMappings("article", "Article").ToList();

        result.Should().ContainSingle();
        result[0].Confidence.Should().Be(100);
        result[0].SuggestedContentTypePropertyAlias.Should().Be("headline");
        result[0].IsAutoMapped.Should().BeTrue();
    }

    [Fact]
    public void SuggestMappings_SynonymMatch_ReturnsConfidence80()
    {
        var contentType = CreateContentTypeWithProperties("title");
        _contentTypeService.Get("article").Returns(contentType);
        _schemaTypeRegistry.GetProperties("Article").Returns(new[]
        {
            new SchemaPropertyInfo { Name = "headline", PropertyType = "Text" }
        });

        var result = _sut.SuggestMappings("article", "Article").ToList();

        result.Should().ContainSingle();
        result[0].Confidence.Should().Be(80);
        result[0].SuggestedContentTypePropertyAlias.Should().Be("title");
        result[0].IsAutoMapped.Should().BeTrue();
    }

    [Fact]
    public void SuggestMappings_PartialMatch_ReturnsConfidence50()
    {
        var contentType = CreateContentTypeWithProperties("blogHeadline");
        _contentTypeService.Get("article").Returns(contentType);
        _schemaTypeRegistry.GetProperties("Article").Returns(new[]
        {
            new SchemaPropertyInfo { Name = "headline", PropertyType = "Text" }
        });

        var result = _sut.SuggestMappings("article", "Article").ToList();

        result.Should().ContainSingle();
        result[0].Confidence.Should().Be(50);
        result[0].SuggestedContentTypePropertyAlias.Should().Be("blogHeadline");
        result[0].IsAutoMapped.Should().BeTrue();
    }

    [Fact]
    public void SuggestMappings_NoMatch_ReturnsConfidence0()
    {
        var contentType = CreateContentTypeWithProperties("somethingUnrelated");
        _contentTypeService.Get("article").Returns(contentType);
        _schemaTypeRegistry.GetProperties("Article").Returns(new[]
        {
            new SchemaPropertyInfo { Name = "headline", PropertyType = "Text" }
        });

        var result = _sut.SuggestMappings("article", "Article").ToList();

        result.Should().ContainSingle();
        result[0].Confidence.Should().Be(0);
        result[0].IsAutoMapped.Should().BeFalse();
        result[0].SuggestedContentTypePropertyAlias.Should().BeNull();
    }

    [Fact]
    public void SuggestMappings_CaseInsensitive_ExactMatch()
    {
        var contentType = CreateContentTypeWithProperties("HEADLINE");
        _contentTypeService.Get("article").Returns(contentType);
        _schemaTypeRegistry.GetProperties("Article").Returns(new[]
        {
            new SchemaPropertyInfo { Name = "Headline", PropertyType = "Text" }
        });

        var result = _sut.SuggestMappings("article", "Article").ToList();

        result.Should().ContainSingle();
        result[0].Confidence.Should().Be(100);
    }

    [Fact]
    public void SuggestMappings_UnknownContentType_ReturnsEmpty()
    {
        _contentTypeService.Get("unknown").Returns((IContentType?)null);

        var result = _sut.SuggestMappings("unknown", "Article");

        result.Should().BeEmpty();
    }

    [Fact]
    public void SuggestMappings_MultipleProperties_MappedSimultaneously()
    {
        var contentType = CreateContentTypeWithProperties("headline", "description", "image");
        _contentTypeService.Get("article").Returns(contentType);
        _schemaTypeRegistry.GetProperties("Article").Returns(new[]
        {
            new SchemaPropertyInfo { Name = "headline", PropertyType = "Text" },
            new SchemaPropertyInfo { Name = "description", PropertyType = "Text" },
            new SchemaPropertyInfo { Name = "image", PropertyType = "URL" }
        });

        var result = _sut.SuggestMappings("article", "Article").ToList();

        result.Should().HaveCount(3);
        result.Should().AllSatisfy(s => s.Confidence.Should().Be(100));
    }

    [Fact]
    public void SuggestMappings_SortedBySchemaProperty_NotConfidence()
    {
        // Verify suggestions are returned in schema property order (one per schema prop)
        var contentType = CreateContentTypeWithProperties("title", "unrelated");
        _contentTypeService.Get("article").Returns(contentType);
        _schemaTypeRegistry.GetProperties("Article").Returns(new[]
        {
            new SchemaPropertyInfo { Name = "headline", PropertyType = "Text" },
            new SchemaPropertyInfo { Name = "unknownProp", PropertyType = "Text" }
        });

        var result = _sut.SuggestMappings("article", "Article").ToList();

        result.Should().HaveCount(2);
        result[0].SchemaPropertyName.Should().Be("headline");
        result[0].Confidence.Should().Be(80); // synonym match: title -> headline
        result[1].SchemaPropertyName.Should().Be("unknownProp");
        result[1].Confidence.Should().Be(0);
    }

    #region Complex Type Intelligence

    [Fact]
    public void ComplexProperty_WithBlockListEditor_SuggestsBlockContent()
    {
        // When a content property name-matches a complex schema property AND uses a BlockList editor,
        // the auto-mapper should suggest blockContent source type at confidence 70
        var contentType = CreateContentTypeWithEditors(
            ("reviews", "Umbraco.BlockList"));
        _contentTypeService.Get("product").Returns(contentType);
        _schemaTypeRegistry.GetProperties("Product").Returns(new[]
        {
            new SchemaPropertyInfo
            {
                Name = "review",
                PropertyType = "Review",
                IsComplexType = true,
                AcceptedTypes = ["Review"]
            }
        });

        var result = _sut.SuggestMappings("product", "Product").ToList();

        result.Should().ContainSingle();
        // Synonym match "reviews" → "review" + BlockList editor → blockContent, confidence 70
        result[0].SuggestedSourceType.Should().Be("blockContent");
        result[0].SuggestedNestedSchemaTypeName.Should().Be("Review");
        result[0].SuggestedResolverConfig.Should().NotBeNullOrEmpty();
        result[0].Confidence.Should().Be(70);
        result[0].IsAutoMapped.Should().BeTrue();
    }

    [Fact]
    public void ComplexProperty_NoMatch_SuggestsComplexType()
    {
        var contentType = CreateContentTypeWithProperties("somethingUnrelated");
        _contentTypeService.Get("product").Returns(contentType);
        _schemaTypeRegistry.GetProperties("Product").Returns(new[]
        {
            new SchemaPropertyInfo
            {
                Name = "offers",
                PropertyType = "Offer",
                IsComplexType = true,
                AcceptedTypes = ["Offer"]
            }
        });

        var result = _sut.SuggestMappings("product", "Product").ToList();

        result.Should().ContainSingle();
        // Popular default: Product.offers → complexType, Offer
        result[0].SuggestedSourceType.Should().Be("complexType");
        result[0].SuggestedNestedSchemaTypeName.Should().Be("Offer");
        result[0].Confidence.Should().Be(60);
        result[0].IsAutoMapped.Should().BeTrue();
    }

    [Fact]
    public void FAQPage_MainEntity_SuggestsQuestionWithResolverConfig()
    {
        // No name match between "faqItems" and "mainEntity", but popular default kicks in
        // Should also suggest the BlockList property alias
        var contentType = CreateContentTypeWithEditors(
            ("faqItems", "Umbraco.BlockList"));
        _contentTypeService.Get("faqPage").Returns(contentType);
        _schemaTypeRegistry.GetProperties("FAQPage").Returns(new[]
        {
            new SchemaPropertyInfo
            {
                Name = "mainEntity",
                PropertyType = "Question",
                IsComplexType = true,
                AcceptedTypes = ["Question"]
            }
        });

        var result = _sut.SuggestMappings("faqPage", "FAQPage").ToList();

        var mainEntity = result.First(s => s.SchemaPropertyName == "mainEntity");
        mainEntity.SuggestedSourceType.Should().Be("blockContent");
        mainEntity.SuggestedNestedSchemaTypeName.Should().Be("Question");
        mainEntity.SuggestedResolverConfig.Should().Contain("acceptedAnswer");
        mainEntity.SuggestedResolverConfig.Should().Contain("Answer");
        mainEntity.SuggestedContentTypePropertyAlias.Should().Be("faqItems");
        mainEntity.Confidence.Should().Be(60);
    }

    [Fact]
    public void Product_Offers_SuggestsComplexTypeWithOffer()
    {
        var contentType = CreateContentTypeWithProperties("unrelated");
        _contentTypeService.Get("product").Returns(contentType);
        _schemaTypeRegistry.GetProperties("Product").Returns(new[]
        {
            new SchemaPropertyInfo
            {
                Name = "offers",
                PropertyType = "Offer",
                IsComplexType = true,
                AcceptedTypes = ["Offer"]
            }
        });

        var result = _sut.SuggestMappings("product", "Product").ToList();

        var offers = result.First(s => s.SchemaPropertyName == "offers");
        offers.SuggestedSourceType.Should().Be("complexType");
        offers.SuggestedNestedSchemaTypeName.Should().Be("Offer");
    }

    [Fact]
    public void LocalBusiness_Address_SuggestsPostalAddress()
    {
        var contentType = CreateContentTypeWithProperties("unrelated");
        _contentTypeService.Get("localBusiness").Returns(contentType);
        _schemaTypeRegistry.GetProperties("LocalBusiness").Returns(new[]
        {
            new SchemaPropertyInfo
            {
                Name = "address",
                PropertyType = "PostalAddress",
                IsComplexType = true,
                AcceptedTypes = ["PostalAddress"]
            }
        });

        var result = _sut.SuggestMappings("localBusiness", "LocalBusiness").ToList();

        var address = result.First(s => s.SchemaPropertyName == "address");
        address.SuggestedSourceType.Should().Be("complexType");
        address.SuggestedNestedSchemaTypeName.Should().Be("PostalAddress");
    }

    [Fact]
    public void NewSynonyms_RecipeProperties_Match()
    {
        var contentType = CreateContentTypeWithProperties("preparationTime", "cookingTime", "servings");
        _contentTypeService.Get("recipe").Returns(contentType);
        _schemaTypeRegistry.GetProperties("Recipe").Returns(new[]
        {
            new SchemaPropertyInfo { Name = "prepTime", PropertyType = "Duration" },
            new SchemaPropertyInfo { Name = "cookTime", PropertyType = "Duration" },
            new SchemaPropertyInfo { Name = "recipeYield", PropertyType = "Text" },
        });

        var result = _sut.SuggestMappings("recipe", "Recipe").ToList();

        result.Should().HaveCount(3);
        result[0].SuggestedContentTypePropertyAlias.Should().Be("preparationTime");
        result[0].Confidence.Should().Be(80);
        result[1].SuggestedContentTypePropertyAlias.Should().Be("cookingTime");
        result[1].Confidence.Should().Be(80);
        result[2].SuggestedContentTypePropertyAlias.Should().Be("servings");
        result[2].Confidence.Should().Be(80);
    }

    [Fact]
    public void NewSynonyms_EventProperties_Match()
    {
        var contentType = CreateContentTypeWithProperties("eventDate", "venue", "organiser");
        _contentTypeService.Get("event").Returns(contentType);
        _schemaTypeRegistry.GetProperties("Event").Returns(new[]
        {
            new SchemaPropertyInfo { Name = "startDate", PropertyType = "DateTime" },
            new SchemaPropertyInfo { Name = "location", PropertyType = "Text" },
            new SchemaPropertyInfo { Name = "organizer", PropertyType = "Text" },
        });

        var result = _sut.SuggestMappings("event", "Event").ToList();

        result.Should().HaveCount(3);
        result[0].SuggestedContentTypePropertyAlias.Should().Be("eventDate");
        result[0].Confidence.Should().Be(80);
        result[1].SuggestedContentTypePropertyAlias.Should().Be("venue");
        result[1].Confidence.Should().Be(80);
        result[2].SuggestedContentTypePropertyAlias.Should().Be("organiser");
        result[2].Confidence.Should().Be(80);
    }

    [Fact]
    public void NewSynonyms_ProductProperties_Match()
    {
        var contentType = CreateContentTypeWithProperties("productCode", "manufacturer", "cost");
        _contentTypeService.Get("product").Returns(contentType);
        _schemaTypeRegistry.GetProperties("Product").Returns(new[]
        {
            new SchemaPropertyInfo { Name = "sku", PropertyType = "Text" },
            new SchemaPropertyInfo { Name = "brand", PropertyType = "Text" },
            new SchemaPropertyInfo { Name = "price", PropertyType = "Number" },
        });

        var result = _sut.SuggestMappings("product", "Product").ToList();

        result.Should().HaveCount(3);
        result[0].SuggestedContentTypePropertyAlias.Should().Be("productCode");
        result[0].Confidence.Should().Be(80);
        result[1].SuggestedContentTypePropertyAlias.Should().Be("manufacturer");
        result[1].Confidence.Should().Be(80);
        result[2].SuggestedContentTypePropertyAlias.Should().Be("cost");
        result[2].Confidence.Should().Be(80);
    }

    [Fact]
    public void SuggestedNestedSchemaTypeName_PopulatedFromAcceptedTypes()
    {
        // Complex type with no popular default — should use first non-primitive from AcceptedTypes
        var contentType = CreateContentTypeWithProperties("unrelated");
        _contentTypeService.Get("custom").Returns(contentType);
        _schemaTypeRegistry.GetProperties("CustomType").Returns(new[]
        {
            new SchemaPropertyInfo
            {
                Name = "someComplexProp",
                PropertyType = "SomeType",
                IsComplexType = true,
                AcceptedTypes = ["Text", "SomeType", "Number"]
            }
        });

        var result = _sut.SuggestMappings("custom", "CustomType").ToList();

        result.Should().ContainSingle();
        result[0].SuggestedSourceType.Should().Be("complexType");
        result[0].SuggestedNestedSchemaTypeName.Should().Be("SomeType");
        result[0].Confidence.Should().Be(60);
    }

    [Fact]
    public void ComplexProperty_WithBlockGridEditor_SuggestsBlockContent()
    {
        var contentType = CreateContentTypeWithEditors(
            ("items", "Umbraco.BlockGrid"));
        _contentTypeService.Get("page").Returns(contentType);
        _schemaTypeRegistry.GetProperties("SomeType").Returns(new[]
        {
            new SchemaPropertyInfo
            {
                Name = "items",
                PropertyType = "Thing",
                IsComplexType = true,
                AcceptedTypes = ["Thing"]
            }
        });

        var result = _sut.SuggestMappings("page", "SomeType").ToList();

        result.Should().ContainSingle();
        result[0].SuggestedSourceType.Should().Be("blockContent");
        result[0].SuggestedNestedSchemaTypeName.Should().Be("Thing");
        result[0].Confidence.Should().Be(70);
    }

    [Fact]
    public void ComplexProperty_WithContentPicker_KeepsPropertySourceType()
    {
        var contentType = CreateContentTypeWithEditors(
            ("author", "Umbraco.ContentPicker"));
        _contentTypeService.Get("article").Returns(contentType);
        _schemaTypeRegistry.GetProperties("Article").Returns(new[]
        {
            new SchemaPropertyInfo
            {
                Name = "author",
                PropertyType = "Person",
                IsComplexType = true,
                AcceptedTypes = ["Person"]
            }
        });

        var result = _sut.SuggestMappings("article", "Article").ToList();

        result.Should().ContainSingle();
        // Content picker keeps "property" source type
        result[0].SuggestedSourceType.Should().Be("property");
        result[0].SuggestedNestedSchemaTypeName.Should().Be("Person");
        result[0].Confidence.Should().Be(100); // exact match
    }

    [Fact]
    public void Recipe_Instructions_SuggestsHowToStepWithConfig()
    {
        var contentType = CreateContentTypeWithEditors(
            ("instructions", "Umbraco.BlockList"));
        _contentTypeService.Get("recipe").Returns(contentType);
        _schemaTypeRegistry.GetProperties("Recipe").Returns(new[]
        {
            new SchemaPropertyInfo
            {
                Name = "recipeInstructions",
                PropertyType = "HowToStep",
                IsComplexType = true,
                AcceptedTypes = ["HowToStep"]
            }
        });

        var result = _sut.SuggestMappings("recipe", "Recipe").ToList();

        var instructions = result.First(s => s.SchemaPropertyName == "recipeInstructions");
        instructions.SuggestedSourceType.Should().Be("blockContent");
        instructions.SuggestedNestedSchemaTypeName.Should().Be("HowToStep");
        instructions.SuggestedResolverConfig.Should().Contain("stepName");
        instructions.SuggestedResolverConfig.Should().Contain("stepText");
    }

    #region End-to-End Auto-Mapping (TestHost Content Types)

    /// <summary>
    /// Helper that creates a content type with a mix of simple properties (default editor alias)
    /// and properties with specific editor aliases (e.g. BlockList).
    /// Pass null for editorAlias to get a simple textbox-style property.
    /// </summary>
    private IContentType CreateContentTypeWithMixedEditors(params (string alias, string? editorAlias)[] properties)
    {
        var contentType = Substitute.For<IContentType>();
        var propertyTypes = properties.Select(p =>
        {
            var pt = Substitute.For<IPropertyType>();
            pt.Alias.Returns(p.alias);
            pt.PropertyEditorAlias.Returns(p.editorAlias ?? "Umbraco.TextBox");
            return pt;
        }).ToList();
        contentType.PropertyTypes.Returns(propertyTypes);
        return contentType;
    }

    [Fact]
    public void Recipe_FullAutoMap_MapsAllProperties()
    {
        // Simulates the TestHost recipePage content type with all its properties
        var contentType = CreateContentTypeWithMixedEditors(
            ("title", null),
            ("description", null),
            ("authorName", null),
            ("prepTime", null),
            ("cookTime", null),
            ("totalTime", null),
            ("recipeYield", null),
            ("calories", null),
            ("recipeCategory", null),
            ("recipeCuisine", null),
            ("recipeImage", null),
            ("instructions", "Umbraco.BlockList"),
            ("ingredients", "Umbraco.BlockList"));
        _contentTypeService.Get("recipePage").Returns(contentType);
        _schemaTypeRegistry.GetProperties("Recipe").Returns(new[]
        {
            new SchemaPropertyInfo { Name = "name", PropertyType = "Text" },
            new SchemaPropertyInfo { Name = "description", PropertyType = "Text" },
            new SchemaPropertyInfo { Name = "author", PropertyType = "Person", IsComplexType = true, AcceptedTypes = ["Person"] },
            new SchemaPropertyInfo { Name = "image", PropertyType = "URL" },
            new SchemaPropertyInfo { Name = "prepTime", PropertyType = "Duration" },
            new SchemaPropertyInfo { Name = "cookTime", PropertyType = "Duration" },
            new SchemaPropertyInfo { Name = "totalTime", PropertyType = "Duration" },
            new SchemaPropertyInfo { Name = "recipeYield", PropertyType = "Text" },
            new SchemaPropertyInfo { Name = "calories", PropertyType = "Text" },
            new SchemaPropertyInfo { Name = "recipeCategory", PropertyType = "Text" },
            new SchemaPropertyInfo { Name = "recipeCuisine", PropertyType = "Text" },
            new SchemaPropertyInfo
            {
                Name = "recipeIngredient", PropertyType = "Text",
                IsComplexType = true, AcceptedTypes = ["Text"]
            },
            new SchemaPropertyInfo
            {
                Name = "recipeInstructions", PropertyType = "HowToStep",
                IsComplexType = true, AcceptedTypes = ["HowToStep"]
            },
        });

        var result = _sut.SuggestMappings("recipePage", "Recipe").ToList();

        // name → title (synonym, 80)
        var name = result.First(s => s.SchemaPropertyName == "name");
        name.SuggestedContentTypePropertyAlias.Should().Be("title");
        name.Confidence.Should().Be(80);
        name.IsAutoMapped.Should().BeTrue();

        // description → description (exact, 100)
        var description = result.First(s => s.SchemaPropertyName == "description");
        description.SuggestedContentTypePropertyAlias.Should().Be("description");
        description.Confidence.Should().Be(100);

        // author → authorName (synonym, 80) — complex type with popular default Person
        var author = result.First(s => s.SchemaPropertyName == "author");
        author.SuggestedContentTypePropertyAlias.Should().Be("authorName");
        author.Confidence.Should().BeOneOf(80); // synonym match, non-block editor applies popular default
        author.SuggestedSourceType.Should().Be("complexType");
        author.SuggestedNestedSchemaTypeName.Should().Be("Person");

        // prepTime → prepTime (exact, 100)
        var prepTime = result.First(s => s.SchemaPropertyName == "prepTime");
        prepTime.SuggestedContentTypePropertyAlias.Should().Be("prepTime");
        prepTime.Confidence.Should().Be(100);

        // cookTime → cookTime (exact, 100)
        var cookTime = result.First(s => s.SchemaPropertyName == "cookTime");
        cookTime.SuggestedContentTypePropertyAlias.Should().Be("cookTime");
        cookTime.Confidence.Should().Be(100);

        // totalTime → totalTime (exact, 100)
        var totalTime = result.First(s => s.SchemaPropertyName == "totalTime");
        totalTime.SuggestedContentTypePropertyAlias.Should().Be("totalTime");
        totalTime.Confidence.Should().Be(100);

        // recipeYield → recipeYield (exact, 100)
        var recipeYield = result.First(s => s.SchemaPropertyName == "recipeYield");
        recipeYield.SuggestedContentTypePropertyAlias.Should().Be("recipeYield");
        recipeYield.Confidence.Should().Be(100);

        // calories → calories (exact, 100)
        var calories = result.First(s => s.SchemaPropertyName == "calories");
        calories.SuggestedContentTypePropertyAlias.Should().Be("calories");
        calories.Confidence.Should().Be(100);

        // recipeCategory → recipeCategory (exact, 100)
        var recipeCategory = result.First(s => s.SchemaPropertyName == "recipeCategory");
        recipeCategory.SuggestedContentTypePropertyAlias.Should().Be("recipeCategory");
        recipeCategory.Confidence.Should().Be(100);

        // recipeCuisine → recipeCuisine (exact, 100)
        var recipeCuisine = result.First(s => s.SchemaPropertyName == "recipeCuisine");
        recipeCuisine.SuggestedContentTypePropertyAlias.Should().Be("recipeCuisine");
        recipeCuisine.Confidence.Should().Be(100);

        // recipeInstructions → instructions (synonym + BlockList → blockContent, 70)
        var instructions = result.First(s => s.SchemaPropertyName == "recipeInstructions");
        instructions.SuggestedContentTypePropertyAlias.Should().Be("instructions");
        instructions.SuggestedSourceType.Should().Be("blockContent");
        instructions.SuggestedNestedSchemaTypeName.Should().Be("HowToStep");
        instructions.SuggestedResolverConfig.Should().NotBeNullOrEmpty();
        instructions.SuggestedResolverConfig.Should().Contain("stepName");
        instructions.SuggestedResolverConfig.Should().Contain("stepText");
        instructions.Confidence.Should().Be(70);

        // recipeIngredient → ingredients (synonym + BlockList → blockContent, 70)
        var ingredients = result.First(s => s.SchemaPropertyName == "recipeIngredient");
        ingredients.SuggestedContentTypePropertyAlias.Should().Be("ingredients");
        ingredients.SuggestedSourceType.Should().Be("blockContent");
        ingredients.SuggestedResolverConfig.Should().NotBeNullOrEmpty();
        ingredients.SuggestedResolverConfig.Should().Contain("ingredient");
        ingredients.Confidence.Should().Be(70);

        // All suggestions should be auto-mapped
        result.Should().OnlyContain(s => s.IsAutoMapped);
    }

    [Fact]
    public void Product_FullAutoMap_MapsAllProperties()
    {
        // Simulates the TestHost productPage content type
        var contentType = CreateContentTypeWithMixedEditors(
            ("title", null),
            ("description", null),
            ("bodyText", null),
            ("price", null),
            ("sku", null),
            ("brand", null),
            ("availability", null),
            ("currency", null),
            ("productImage", null),
            ("reviews", "Umbraco.BlockList"));
        _contentTypeService.Get("productPage").Returns(contentType);
        _schemaTypeRegistry.GetProperties("Product").Returns(new[]
        {
            new SchemaPropertyInfo { Name = "name", PropertyType = "Text" },
            new SchemaPropertyInfo { Name = "description", PropertyType = "Text" },
            new SchemaPropertyInfo { Name = "sku", PropertyType = "Text" },
            new SchemaPropertyInfo
            {
                Name = "brand", PropertyType = "Brand",
                IsComplexType = true, AcceptedTypes = ["Brand"]
            },
            new SchemaPropertyInfo
            {
                Name = "offers", PropertyType = "Offer",
                IsComplexType = true, AcceptedTypes = ["Offer"]
            },
            new SchemaPropertyInfo
            {
                Name = "review", PropertyType = "Review",
                IsComplexType = true, AcceptedTypes = ["Review"]
            },
        });

        var result = _sut.SuggestMappings("productPage", "Product").ToList();

        // name → title (synonym, 80)
        var name = result.First(s => s.SchemaPropertyName == "name");
        name.SuggestedContentTypePropertyAlias.Should().Be("title");
        name.Confidence.Should().Be(80);

        // description → description (exact, 100)
        var description = result.First(s => s.SchemaPropertyName == "description");
        description.SuggestedContentTypePropertyAlias.Should().Be("description");
        description.Confidence.Should().Be(100);

        // sku → sku (exact, 100)
        var sku = result.First(s => s.SchemaPropertyName == "sku");
        sku.SuggestedContentTypePropertyAlias.Should().Be("sku");
        sku.Confidence.Should().Be(100);

        // brand → brand (exact match, complex type with popular default Brand)
        var brand = result.First(s => s.SchemaPropertyName == "brand");
        brand.SuggestedContentTypePropertyAlias.Should().Be("brand");
        brand.SuggestedSourceType.Should().Be("complexType");
        brand.SuggestedNestedSchemaTypeName.Should().Be("Brand");

        // review → reviews (synonym + BlockList → blockContent with Review config, 70)
        var review = result.First(s => s.SchemaPropertyName == "review");
        review.SuggestedContentTypePropertyAlias.Should().Be("reviews");
        review.SuggestedSourceType.Should().Be("blockContent");
        review.SuggestedNestedSchemaTypeName.Should().Be("Review");
        review.SuggestedResolverConfig.Should().NotBeNullOrEmpty();
        review.SuggestedResolverConfig.Should().Contain("reviewAuthor");
        review.SuggestedResolverConfig.Should().Contain("reviewBody");
        review.SuggestedResolverConfig.Should().Contain("ratingValue");
        review.Confidence.Should().Be(70);

        // offers — no content property match, popular default kicks in
        var offers = result.First(s => s.SchemaPropertyName == "offers");
        offers.SuggestedSourceType.Should().Be("complexType");
        offers.SuggestedNestedSchemaTypeName.Should().Be("Offer");
        offers.Confidence.Should().Be(60);
        offers.SuggestedContentTypePropertyAlias.Should().BeNull();
    }

    [Fact]
    public void Event_FullAutoMap_MapsAllProperties()
    {
        // Simulates the TestHost eventPage content type
        var contentType = CreateContentTypeWithMixedEditors(
            ("title", null),
            ("description", null),
            ("startDate", null),
            ("endDate", null),
            ("locationName", null),
            ("locationAddress", null),
            ("organiserName", null),
            ("ticketPrice", null),
            ("ticketUrl", null),
            ("eventImage", null));
        _contentTypeService.Get("eventPage").Returns(contentType);
        _schemaTypeRegistry.GetProperties("Event").Returns(new[]
        {
            new SchemaPropertyInfo { Name = "name", PropertyType = "Text" },
            new SchemaPropertyInfo { Name = "description", PropertyType = "Text" },
            new SchemaPropertyInfo { Name = "startDate", PropertyType = "DateTime" },
            new SchemaPropertyInfo { Name = "endDate", PropertyType = "DateTime" },
            new SchemaPropertyInfo
            {
                Name = "location", PropertyType = "Place",
                IsComplexType = true, AcceptedTypes = ["Place"]
            },
            new SchemaPropertyInfo
            {
                Name = "organizer", PropertyType = "Organization",
                IsComplexType = true, AcceptedTypes = ["Organization"]
            },
            new SchemaPropertyInfo { Name = "url", PropertyType = "URL" },
            new SchemaPropertyInfo
            {
                Name = "offers", PropertyType = "Offer",
                IsComplexType = true, AcceptedTypes = ["Offer"]
            },
        });

        var result = _sut.SuggestMappings("eventPage", "Event").ToList();

        // name → title (synonym, 80)
        var name = result.First(s => s.SchemaPropertyName == "name");
        name.SuggestedContentTypePropertyAlias.Should().Be("title");
        name.Confidence.Should().Be(80);

        // description → description (exact, 100)
        var description = result.First(s => s.SchemaPropertyName == "description");
        description.SuggestedContentTypePropertyAlias.Should().Be("description");
        description.Confidence.Should().Be(100);

        // startDate → startDate (exact, 100)
        var startDate = result.First(s => s.SchemaPropertyName == "startDate");
        startDate.SuggestedContentTypePropertyAlias.Should().Be("startDate");
        startDate.Confidence.Should().Be(100);

        // endDate → endDate (exact, 100)
        var endDate = result.First(s => s.SchemaPropertyName == "endDate");
        endDate.SuggestedContentTypePropertyAlias.Should().Be("endDate");
        endDate.Confidence.Should().Be(100);

        // url → ticketUrl (partial match: "ticketUrl" contains "url", 50)
        var url = result.First(s => s.SchemaPropertyName == "url");
        url.SuggestedContentTypePropertyAlias.Should().Be("ticketUrl");
        url.Confidence.Should().Be(50);

        // location — locationName matches via synonym, but location is a complex type
        // "locationName" is a synonym for "location", so synonym match applies
        var location = result.First(s => s.SchemaPropertyName == "location");
        location.SuggestedContentTypePropertyAlias.Should().Be("locationName");
        location.Confidence.Should().Be(80);
        // Complex type with popular default: Place
        location.SuggestedSourceType.Should().Be("complexType");
        location.SuggestedNestedSchemaTypeName.Should().Be("Place");

        // organizer → organiserName (synonym, 80) — complex type with popular default: Organization
        var organizer = result.First(s => s.SchemaPropertyName == "organizer");
        organizer.SuggestedContentTypePropertyAlias.Should().Be("organiserName");
        organizer.Confidence.Should().Be(80);
        organizer.SuggestedSourceType.Should().Be("complexType");
        organizer.SuggestedNestedSchemaTypeName.Should().Be("Organization");

        // offers — no content property match, popular default kicks in
        var offers = result.First(s => s.SchemaPropertyName == "offers");
        offers.SuggestedSourceType.Should().Be("complexType");
        offers.SuggestedNestedSchemaTypeName.Should().Be("Offer");
        offers.Confidence.Should().Be(60);
    }

    [Fact]
    public void BlogPosting_FullAutoMap_MapsAllProperties()
    {
        // Simulates the TestHost blogArticle content type
        var contentType = CreateContentTypeWithMixedEditors(
            ("title", null),
            ("description", null),
            ("bodyText", null),
            ("authorName", null),
            ("publishDate", null),
            ("featuredImage", null),
            ("keywords", null),
            ("category", null));
        _contentTypeService.Get("blogArticle").Returns(contentType);
        _schemaTypeRegistry.GetProperties("BlogPosting").Returns(new[]
        {
            new SchemaPropertyInfo { Name = "headline", PropertyType = "Text" },
            new SchemaPropertyInfo { Name = "articleBody", PropertyType = "Text" },
            new SchemaPropertyInfo { Name = "description", PropertyType = "Text" },
            new SchemaPropertyInfo
            {
                Name = "author", PropertyType = "Person",
                IsComplexType = true, AcceptedTypes = ["Person"]
            },
            new SchemaPropertyInfo { Name = "datePublished", PropertyType = "DateTime" },
            new SchemaPropertyInfo { Name = "image", PropertyType = "URL" },
            new SchemaPropertyInfo { Name = "keywords", PropertyType = "Text" },
        });

        var result = _sut.SuggestMappings("blogArticle", "BlogPosting").ToList();

        // headline → title (synonym, 80)
        var headline = result.First(s => s.SchemaPropertyName == "headline");
        headline.SuggestedContentTypePropertyAlias.Should().Be("title");
        headline.Confidence.Should().Be(80);

        // articleBody → bodyText (synonym, 80)
        var articleBody = result.First(s => s.SchemaPropertyName == "articleBody");
        articleBody.SuggestedContentTypePropertyAlias.Should().Be("bodyText");
        articleBody.Confidence.Should().Be(80);

        // description → description (exact, 100)
        var description = result.First(s => s.SchemaPropertyName == "description");
        description.SuggestedContentTypePropertyAlias.Should().Be("description");
        description.Confidence.Should().Be(100);

        // author → authorName (synonym, 80) — complex type with popular default Person
        var author = result.First(s => s.SchemaPropertyName == "author");
        author.SuggestedContentTypePropertyAlias.Should().Be("authorName");
        author.Confidence.Should().Be(80);
        author.SuggestedSourceType.Should().Be("complexType");
        author.SuggestedNestedSchemaTypeName.Should().Be("Person");

        // datePublished → publishDate (synonym, 80)
        var datePublished = result.First(s => s.SchemaPropertyName == "datePublished");
        datePublished.SuggestedContentTypePropertyAlias.Should().Be("publishDate");
        datePublished.Confidence.Should().Be(80);

        // image → featuredImage (synonym, 80)
        var image = result.First(s => s.SchemaPropertyName == "image");
        image.SuggestedContentTypePropertyAlias.Should().Be("featuredImage");
        image.Confidence.Should().Be(80);

        // keywords → keywords (exact, 100)
        var keywords = result.First(s => s.SchemaPropertyName == "keywords");
        keywords.SuggestedContentTypePropertyAlias.Should().Be("keywords");
        keywords.Confidence.Should().Be(100);

        // All should be auto-mapped
        result.Should().OnlyContain(s => s.IsAutoMapped);
    }

    [Fact]
    public void AutoMap_SuggestionToPropertyMapping_ProducesValidConfig()
    {
        // Simulates the TestHost faqPage and verifies suggestions can construct
        // a PropertyMapping entity with correct fields for JsonLdGenerator
        var contentType = CreateContentTypeWithMixedEditors(
            ("title", null),
            ("description", null),
            ("faqItems", "Umbraco.BlockList"));
        _contentTypeService.Get("faqPage").Returns(contentType);
        _schemaTypeRegistry.GetProperties("FAQPage").Returns(new[]
        {
            new SchemaPropertyInfo { Name = "name", PropertyType = "Text" },
            new SchemaPropertyInfo { Name = "description", PropertyType = "Text" },
            new SchemaPropertyInfo
            {
                Name = "mainEntity", PropertyType = "Question",
                IsComplexType = true, AcceptedTypes = ["Question"]
            },
        });

        var suggestions = _sut.SuggestMappings("faqPage", "FAQPage").ToList();

        // Verify we can construct valid PropertyMapping entities from each suggestion
        foreach (var suggestion in suggestions)
        {
            var mapping = new Models.Entities.PropertyMapping
            {
                SchemaMappingId = 1,
                SchemaPropertyName = suggestion.SchemaPropertyName,
                SourceType = suggestion.SuggestedSourceType,
                ContentTypePropertyAlias = suggestion.SuggestedContentTypePropertyAlias,
                IsAutoMapped = suggestion.IsAutoMapped,
                NestedSchemaTypeName = suggestion.SuggestedNestedSchemaTypeName,
                ResolverConfig = suggestion.SuggestedResolverConfig,
            };

            mapping.SchemaPropertyName.Should().NotBeNullOrEmpty();
            mapping.SourceType.Should().NotBeNullOrEmpty();
            mapping.SchemaMappingId.Should().Be(1);
        }

        // Verify specific FAQ mainEntity mapping produces a complete config for JsonLdGenerator
        var mainEntity = suggestions.First(s => s.SchemaPropertyName == "mainEntity");
        var mainEntityMapping = new Models.Entities.PropertyMapping
        {
            SchemaMappingId = 1,
            SchemaPropertyName = mainEntity.SchemaPropertyName,
            SourceType = mainEntity.SuggestedSourceType,
            ContentTypePropertyAlias = mainEntity.SuggestedContentTypePropertyAlias,
            IsAutoMapped = mainEntity.IsAutoMapped,
            NestedSchemaTypeName = mainEntity.SuggestedNestedSchemaTypeName,
            ResolverConfig = mainEntity.SuggestedResolverConfig,
        };

        mainEntityMapping.SchemaPropertyName.Should().Be("mainEntity");
        mainEntityMapping.SourceType.Should().Be("blockContent");
        mainEntityMapping.ContentTypePropertyAlias.Should().Be("faqItems");
        mainEntityMapping.NestedSchemaTypeName.Should().Be("Question");
        mainEntityMapping.ResolverConfig.Should().Contain("acceptedAnswer");
        mainEntityMapping.ResolverConfig.Should().Contain("Answer");
        mainEntityMapping.ResolverConfig.Should().Contain("question");
        mainEntityMapping.IsAutoMapped.Should().BeTrue();

        // Verify the name mapping (synonym: title → name)
        var nameSuggestion = suggestions.First(s => s.SchemaPropertyName == "name");
        var nameMapping = new Models.Entities.PropertyMapping
        {
            SchemaMappingId = 1,
            SchemaPropertyName = nameSuggestion.SchemaPropertyName,
            SourceType = nameSuggestion.SuggestedSourceType,
            ContentTypePropertyAlias = nameSuggestion.SuggestedContentTypePropertyAlias,
            IsAutoMapped = nameSuggestion.IsAutoMapped,
        };

        nameMapping.SchemaPropertyName.Should().Be("name");
        nameMapping.SourceType.Should().Be("property");
        nameMapping.ContentTypePropertyAlias.Should().Be("title");
        nameMapping.IsAutoMapped.Should().BeTrue();
        nameMapping.NestedSchemaTypeName.Should().BeNull();
        nameMapping.ResolverConfig.Should().BeNull();
    }

    #endregion

    #region Expanded Schema Type Coverage

    [Fact]
    public void SuggestMappings_VideoThumbnail_SynonymMatch()
    {
        var contentType = CreateContentTypeWithProperties("thumbnail");
        _contentTypeService.Get("video").Returns(contentType);
        _schemaTypeRegistry.GetProperties("VideoObject").Returns(new[]
        {
            new SchemaPropertyInfo { Name = "thumbnailUrl", PropertyType = "URL" }
        });

        var result = _sut.SuggestMappings("video", "VideoObject").ToList();

        result.Should().ContainSingle();
        result[0].SuggestedContentTypePropertyAlias.Should().Be("thumbnail");
        result[0].Confidence.Should().Be(80);
        result[0].IsAutoMapped.Should().BeTrue();
    }

    [Fact]
    public void SuggestMappings_JobEmploymentType_SynonymMatch()
    {
        var contentType = CreateContentTypeWithProperties("jobType");
        _contentTypeService.Get("jobPosting").Returns(contentType);
        _schemaTypeRegistry.GetProperties("JobPosting").Returns(new[]
        {
            new SchemaPropertyInfo { Name = "employmentType", PropertyType = "Text" }
        });

        var result = _sut.SuggestMappings("jobPosting", "JobPosting").ToList();

        result.Should().ContainSingle();
        result[0].SuggestedContentTypePropertyAlias.Should().Be("jobType");
        result[0].Confidence.Should().Be(80);
        result[0].IsAutoMapped.Should().BeTrue();
    }

    [Fact]
    public void SuggestMappings_CourseCode_SynonymMatch()
    {
        var contentType = CreateContentTypeWithProperties("code");
        _contentTypeService.Get("course").Returns(contentType);
        _schemaTypeRegistry.GetProperties("Course").Returns(new[]
        {
            new SchemaPropertyInfo { Name = "courseCode", PropertyType = "Text" }
        });

        var result = _sut.SuggestMappings("course", "Course").ToList();

        result.Should().ContainSingle();
        result[0].SuggestedContentTypePropertyAlias.Should().Be("code");
        result[0].Confidence.Should().Be(80);
        result[0].IsAutoMapped.Should().BeTrue();
    }

    [Fact]
    public void SuggestMappings_BookIsbn_SynonymMatch()
    {
        var contentType = CreateContentTypeWithProperties("isbnNumber");
        _contentTypeService.Get("book").Returns(contentType);
        _schemaTypeRegistry.GetProperties("Book").Returns(new[]
        {
            new SchemaPropertyInfo { Name = "isbn", PropertyType = "Text" }
        });

        var result = _sut.SuggestMappings("book", "Book").ToList();

        result.Should().ContainSingle();
        result[0].SuggestedContentTypePropertyAlias.Should().Be("isbnNumber");
        result[0].Confidence.Should().Be(80);
        result[0].IsAutoMapped.Should().BeTrue();
    }

    [Fact]
    public void SuggestMappings_RestaurantCuisine_SynonymMatch()
    {
        var contentType = CreateContentTypeWithProperties("cuisineType");
        _contentTypeService.Get("restaurant").Returns(contentType);
        _schemaTypeRegistry.GetProperties("Restaurant").Returns(new[]
        {
            new SchemaPropertyInfo { Name = "servesCuisine", PropertyType = "Text" }
        });

        var result = _sut.SuggestMappings("restaurant", "Restaurant").ToList();

        result.Should().ContainSingle();
        result[0].SuggestedContentTypePropertyAlias.Should().Be("cuisineType");
        result[0].Confidence.Should().Be(80);
        result[0].IsAutoMapped.Should().BeTrue();
    }

    [Fact]
    public void JobPosting_HiringOrganization_SuggestsOrganizationDefault()
    {
        var contentType = CreateContentTypeWithProperties("unrelated");
        _contentTypeService.Get("jobPosting").Returns(contentType);
        _schemaTypeRegistry.GetProperties("JobPosting").Returns(new[]
        {
            new SchemaPropertyInfo
            {
                Name = "hiringOrganization",
                PropertyType = "Organization",
                IsComplexType = true,
                AcceptedTypes = ["Organization"]
            }
        });

        var result = _sut.SuggestMappings("jobPosting", "JobPosting").ToList();

        result.Should().ContainSingle();
        result[0].SuggestedSourceType.Should().Be("complexType");
        result[0].SuggestedNestedSchemaTypeName.Should().Be("Organization");
        result[0].Confidence.Should().Be(60);
        result[0].IsAutoMapped.Should().BeTrue();
    }

    [Fact]
    public void HowTo_Step_SuggestsBlockContentWithResolverConfig()
    {
        var contentType = CreateContentTypeWithEditors(
            ("howToSteps", "Umbraco.BlockList"));
        _contentTypeService.Get("howTo").Returns(contentType);
        _schemaTypeRegistry.GetProperties("HowTo").Returns(new[]
        {
            new SchemaPropertyInfo
            {
                Name = "step",
                PropertyType = "HowToStep",
                IsComplexType = true,
                AcceptedTypes = ["HowToStep"]
            }
        });

        var result = _sut.SuggestMappings("howTo", "HowTo").ToList();

        var step = result.First(s => s.SchemaPropertyName == "step");
        step.SuggestedSourceType.Should().Be("blockContent");
        step.SuggestedNestedSchemaTypeName.Should().Be("HowToStep");
        step.SuggestedResolverConfig.Should().Contain("stepName");
        step.SuggestedResolverConfig.Should().Contain("stepText");
        step.SuggestedContentTypePropertyAlias.Should().Be("howToSteps");
        step.IsAutoMapped.Should().BeTrue();
    }

    [Fact]
    public void Book_Author_SuggestsPersonDefault()
    {
        var contentType = CreateContentTypeWithProperties("unrelated");
        _contentTypeService.Get("book").Returns(contentType);
        _schemaTypeRegistry.GetProperties("Book").Returns(new[]
        {
            new SchemaPropertyInfo
            {
                Name = "author",
                PropertyType = "Person",
                IsComplexType = true,
                AcceptedTypes = ["Person"]
            }
        });

        var result = _sut.SuggestMappings("book", "Book").ToList();

        result.Should().ContainSingle();
        result[0].SuggestedSourceType.Should().Be("complexType");
        result[0].SuggestedNestedSchemaTypeName.Should().Be("Person");
        result[0].Confidence.Should().Be(60);
        result[0].IsAutoMapped.Should().BeTrue();
    }

    [Fact]
    public void SoftwareApplication_Offers_SuggestsOfferDefault()
    {
        var contentType = CreateContentTypeWithProperties("unrelated");
        _contentTypeService.Get("softwareApp").Returns(contentType);
        _schemaTypeRegistry.GetProperties("SoftwareApplication").Returns(new[]
        {
            new SchemaPropertyInfo
            {
                Name = "offers",
                PropertyType = "Offer",
                IsComplexType = true,
                AcceptedTypes = ["Offer"]
            }
        });

        var result = _sut.SuggestMappings("softwareApp", "SoftwareApplication").ToList();

        result.Should().ContainSingle();
        result[0].SuggestedSourceType.Should().Be("complexType");
        result[0].SuggestedNestedSchemaTypeName.Should().Be("Offer");
        result[0].Confidence.Should().Be(60);
        result[0].IsAutoMapped.Should().BeTrue();
    }

    [Fact]
    public void Course_Provider_SuggestsOrganizationDefault()
    {
        var contentType = CreateContentTypeWithProperties("unrelated");
        _contentTypeService.Get("course").Returns(contentType);
        _schemaTypeRegistry.GetProperties("Course").Returns(new[]
        {
            new SchemaPropertyInfo
            {
                Name = "provider",
                PropertyType = "Organization",
                IsComplexType = true,
                AcceptedTypes = ["Organization"]
            }
        });

        var result = _sut.SuggestMappings("course", "Course").ToList();

        result.Should().ContainSingle();
        result[0].SuggestedSourceType.Should().Be("complexType");
        result[0].SuggestedNestedSchemaTypeName.Should().Be("Organization");
        result[0].Confidence.Should().Be(60);
        result[0].IsAutoMapped.Should().BeTrue();
    }

    #endregion

    #region Built-in Property Auto-Mapping

    [Fact]
    public void SuggestMappings_UrlSchemaProperty_NoCustomMatch_SuggestsBuiltInUrl()
    {
        var contentType = CreateContentTypeWithProperties("title", "description");
        _contentTypeService.Get("page").Returns(contentType);
        _schemaTypeRegistry.GetProperties("WebPage").Returns(new[]
        {
            new SchemaPropertyInfo { Name = "url", PropertyType = "URL", AcceptedTypes = ["URL"] }
        });

        var result = _sut.SuggestMappings("page", "WebPage").ToList();

        result.Should().ContainSingle();
        result[0].SuggestedContentTypePropertyAlias.Should().Be("__url");
        result[0].EditorAlias.Should().Be(SchemeWeaverConstants.BuiltInProperties.EditorAlias);
        result[0].Confidence.Should().Be(70);
        result[0].IsAutoMapped.Should().BeTrue();
    }

    [Fact]
    public void SuggestMappings_CustomUrlProperty_PrefersCustomOverBuiltIn()
    {
        var contentType = CreateContentTypeWithProperties("url", "title");
        _contentTypeService.Get("page").Returns(contentType);
        _schemaTypeRegistry.GetProperties("WebPage").Returns(new[]
        {
            new SchemaPropertyInfo { Name = "url", PropertyType = "URL", AcceptedTypes = ["URL"] }
        });

        var result = _sut.SuggestMappings("page", "WebPage").ToList();

        result.Should().ContainSingle();
        result[0].SuggestedContentTypePropertyAlias.Should().Be("url");
        result[0].Confidence.Should().Be(100);
    }

    [Fact]
    public void SuggestMappings_NameProperty_NoCustomMatch_SuggestsBuiltInName()
    {
        var contentType = CreateContentTypeWithProperties("bodyText");
        _contentTypeService.Get("page").Returns(contentType);
        _schemaTypeRegistry.GetProperties("Thing").Returns(new[]
        {
            new SchemaPropertyInfo { Name = "name", PropertyType = "Text", AcceptedTypes = ["Text"] }
        });

        var result = _sut.SuggestMappings("page", "Thing").ToList();

        result.Should().ContainSingle();
        result[0].SuggestedContentTypePropertyAlias.Should().Be("__name");
        result[0].Confidence.Should().Be(70);
        result[0].IsAutoMapped.Should().BeTrue();
    }

    [Fact]
    public void SuggestMappings_DateModified_NoCustomMatch_SuggestsBuiltInUpdateDate()
    {
        var contentType = CreateContentTypeWithProperties("title");
        _contentTypeService.Get("page").Returns(contentType);
        _schemaTypeRegistry.GetProperties("Article").Returns(new[]
        {
            new SchemaPropertyInfo { Name = "dateModified", PropertyType = "Date", AcceptedTypes = ["Date"] }
        });

        var result = _sut.SuggestMappings("page", "Article").ToList();

        result.Should().ContainSingle();
        result[0].SuggestedContentTypePropertyAlias.Should().Be("__updateDate");
        result[0].Confidence.Should().Be(70);
    }

    [Fact]
    public void SuggestMappings_DatePublished_NoCustomMatch_SuggestsBuiltInCreateDate()
    {
        var contentType = CreateContentTypeWithProperties("title");
        _contentTypeService.Get("page").Returns(contentType);
        _schemaTypeRegistry.GetProperties("Article").Returns(new[]
        {
            new SchemaPropertyInfo { Name = "datePublished", PropertyType = "Date", AcceptedTypes = ["Date"] }
        });

        var result = _sut.SuggestMappings("page", "Article").ToList();

        result.Should().ContainSingle();
        result[0].SuggestedContentTypePropertyAlias.Should().Be("__createDate");
        result[0].Confidence.Should().Be(70);
    }

    #endregion

    #endregion
}
