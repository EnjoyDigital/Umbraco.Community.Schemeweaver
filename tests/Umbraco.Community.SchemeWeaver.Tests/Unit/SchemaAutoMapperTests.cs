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
        mainEntity.Confidence.Should().Be(60); // popular default, no name match
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

    #endregion
}
