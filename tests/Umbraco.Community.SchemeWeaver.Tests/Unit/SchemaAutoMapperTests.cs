using FluentAssertions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;
using Umbraco.Cms.Core.Models;
using Umbraco.Cms.Core.Services;
using Umbraco.Community.SchemeWeaver.Models.Api;
using Umbraco.Community.SchemeWeaver.Services;
using Umbraco.Community.SchemeWeaver.Services.Mapping;

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
        contentType.CompositionPropertyTypes.Returns(propertyTypes);
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
        contentType.CompositionPropertyTypes.Returns(propertyTypes);
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
    public void SuggestMappings_IncludesCompositionInheritedProperties()
    {
        // A property that lives only on a composition (e.g. a shared "Hero" tab) is exposed
        // through CompositionPropertyTypes, not the content type's local PropertyTypes. The
        // auto-mapper must still be able to map it.
        var contentType = Substitute.For<IContentType>();
        var composedProp = Substitute.For<IPropertyType>();
        composedProp.Alias.Returns("headline");
        contentType.PropertyTypes.Returns(Array.Empty<IPropertyType>());
        contentType.CompositionPropertyTypes.Returns(new[] { composedProp });
        _contentTypeService.Get("article").Returns(contentType);
        _schemaTypeRegistry.GetProperties("Article").Returns(new[]
        {
            new SchemaPropertyInfo { Name = "Headline", PropertyType = "Text" }
        });

        var result = _sut.SuggestMappings("article", "Article").ToList();

        result.Should().ContainSingle();
        result[0].SuggestedContentTypePropertyAlias.Should().Be("headline");
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
    public void SuggestMappings_PartialMatch_IsDroppedAsJunk()
    {
        // A partial-name match scores 50 internally — below the show threshold (60),
        // so it is dropped entirely rather than offered as a (usually wrong) suggestion.
        var contentType = CreateContentTypeWithProperties("blogHeadline");
        _contentTypeService.Get("article").Returns(contentType);
        _schemaTypeRegistry.GetProperties("Article").Returns(new[]
        {
            new SchemaPropertyInfo { Name = "headline", PropertyType = "Text" }
        });

        var result = _sut.SuggestMappings("article", "Article").ToList();

        result.Should().BeEmpty("partial-name matches (confidence 50) are below the show threshold");
    }

    [Fact]
    public void SuggestMappings_NoMatch_IsDropped()
    {
        // No match at all scores 0 — dropped, never surfaced to the UI.
        var contentType = CreateContentTypeWithProperties("somethingUnrelated");
        _contentTypeService.Get("article").Returns(contentType);
        _schemaTypeRegistry.GetProperties("Article").Returns(new[]
        {
            new SchemaPropertyInfo { Name = "headline", PropertyType = "Text" }
        });

        var result = _sut.SuggestMappings("article", "Article").ToList();

        result.Should().BeEmpty("no-match rows (confidence 0) are below the show threshold");
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
        // Verify suggestions are returned in schema property order (one per schema prop).
        // The no-match "unknownProp" row (confidence 0) is filtered out as junk.
        var contentType = CreateContentTypeWithProperties("title", "unrelated");
        _contentTypeService.Get("article").Returns(contentType);
        _schemaTypeRegistry.GetProperties("Article").Returns(new[]
        {
            new SchemaPropertyInfo { Name = "headline", PropertyType = "Text" },
            new SchemaPropertyInfo { Name = "unknownProp", PropertyType = "Text" }
        });

        var result = _sut.SuggestMappings("article", "Article").ToList();

        result.Should().ContainSingle();
        result[0].SchemaPropertyName.Should().Be("headline");
        result[0].Confidence.Should().Be(80); // synonym match: title -> headline
        result.Should().NotContain(s => s.SchemaPropertyName == "unknownProp");
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
        // Synonym match "reviews" → "review" + BlockList editor → blockContent.
        // Confidence stays at the synonym score (80) and auto-applies.
        result[0].SuggestedSourceType.Should().Be("blockContent");
        result[0].SuggestedNestedSchemaTypeName.Should().Be("Review");
        result[0].SuggestedResolverConfig.Should().NotBeNullOrEmpty();
        result[0].Confidence.Should().Be(80);
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
        // complexType popular default with no content-property match — shown but not auto-applied.
        result[0].IsAutoMapped.Should().BeFalse();
    }

    [Fact]
    public void ComplexProperty_BlockContentDefault_NoBlockProperty_IsDropped()
    {
        // blockContent popular default but NO BlockList/BlockGrid property on content type
        // → confidence 0 → dropped (there is nothing actionable to offer).
        var contentType = CreateContentTypeWithEditors(
            ("someTextField", "Umbraco.TextBox"));
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

        result.Should().NotContain(s => s.SchemaPropertyName == "mainEntity");
    }

    [Fact]
    public void ComplexProperty_NoPopularDefault_NoMatch_IsDropped()
    {
        // Complex type without popular default and no matching property
        // → confidence 0 → dropped.
        var contentType = CreateContentTypeWithProperties("unrelated");
        _contentTypeService.Get("custom").Returns(contentType);
        _schemaTypeRegistry.GetProperties("CustomType").Returns(new[]
        {
            new SchemaPropertyInfo
            {
                Name = "someComplexField",
                PropertyType = "SomeCustomType",
                IsComplexType = true,
                AcceptedTypes = ["SomeCustomType"]
            }
        });

        var result = _sut.SuggestMappings("custom", "CustomType").ToList();

        result.Should().BeEmpty();
    }

    [Fact]
    public void ComplexProperty_BlockContentDefault_WithBlockList_IsShownButNotAutoApplied()
    {
        // blockContent popular default AND a BlockList property → confidence 60: a plausible
        // guess (the block property might hold these items) so it is shown for the user to
        // accept, but it is NOT auto-applied because the name never matched.
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
        mainEntity.Confidence.Should().Be(60);
        mainEntity.IsAutoMapped.Should().BeFalse();
        mainEntity.SuggestedSourceType.Should().Be("blockContent");
        mainEntity.SuggestedNestedSchemaTypeName.Should().Be("Question");
        mainEntity.SuggestedContentTypePropertyAlias.Should().Be("faqItems");
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

    [Theory]
    [InlineData("Article")]
    [InlineData("BlogPosting")]
    [InlineData("NewsArticle")]
    [InlineData("TechArticle")]
    [InlineData("Book")]
    public void ArticleFamily_Publisher_SuggestsReferenceToOrganization(string schemaType)
    {
        // publisher on an article/book is the site publisher: it must reference
        // the shared Organization graph node (as the page types do), NOT author a
        // fresh empty Organization shell (the old complexType/Organization default,
        // which produced a detached, @id-less publisher).
        var contentType = CreateContentTypeWithProperties("unrelated");
        _contentTypeService.Get("node").Returns(contentType);
        _schemaTypeRegistry.GetProperties(schemaType).Returns(new[]
        {
            new SchemaPropertyInfo
            {
                Name = "publisher",
                PropertyType = "Organization",
                IsComplexType = true,
                AcceptedTypes = ["Organization", "Person"]
            }
        });

        var result = _sut.SuggestMappings("node", schemaType).ToList();

        var publisher = result.First(s => s.SchemaPropertyName == "publisher");
        publisher.SuggestedSourceType.Should().Be("reference");
        publisher.SuggestedTargetPieceKey.Should().Be("organization");
        publisher.IsAutoMapped.Should().BeTrue();
        publisher.Confidence.Should().Be(90);
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
    public void SuggestedNestedSchemaTypeName_ComplexNoMatch_IsDropped()
    {
        // Complex type with no popular default and no property match → confidence 0 → dropped.
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

        result.Should().BeEmpty();
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
        result[0].Confidence.Should().Be(100); // exact name match drives confidence, block editor doesn't lower it
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
    public void ComplexProperty_WithMultiNodeTreePicker_KeepsPropertySourceType()
    {
        // MNTP goes through the same picker branch as the single content picker —
        // previously it missed it and could flip to a dead complexType config.
        var contentType = CreateContentTypeWithEditors(
            ("author", "Umbraco.MultiNodeTreePicker"));
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
        result[0].SuggestedSourceType.Should().Be("property");
        result[0].SuggestedNestedSchemaTypeName.Should().Be("Person");
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
        contentType.CompositionPropertyTypes.Returns(propertyTypes);
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

        // recipeInstructions → instructions (synonym + BlockList → blockContent, 80)
        var instructions = result.First(s => s.SchemaPropertyName == "recipeInstructions");
        instructions.SuggestedContentTypePropertyAlias.Should().Be("instructions");
        instructions.SuggestedSourceType.Should().Be("blockContent");
        instructions.SuggestedNestedSchemaTypeName.Should().Be("HowToStep");
        instructions.SuggestedResolverConfig.Should().NotBeNullOrEmpty();
        instructions.SuggestedResolverConfig.Should().Contain("stepName");
        instructions.SuggestedResolverConfig.Should().Contain("stepText");
        instructions.Confidence.Should().Be(80);

        // recipeIngredient → ingredients (synonym + BlockList → blockContent, 80)
        var ingredients = result.First(s => s.SchemaPropertyName == "recipeIngredient");
        ingredients.SuggestedContentTypePropertyAlias.Should().Be("ingredients");
        ingredients.SuggestedSourceType.Should().Be("blockContent");
        ingredients.SuggestedResolverConfig.Should().NotBeNullOrEmpty();
        ingredients.SuggestedResolverConfig.Should().Contain("ingredient");
        ingredients.Confidence.Should().Be(80);

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

        // review → reviews (synonym + BlockList → blockContent with Review config, 80)
        var review = result.First(s => s.SchemaPropertyName == "review");
        review.SuggestedContentTypePropertyAlias.Should().Be("reviews");
        review.SuggestedSourceType.Should().Be("blockContent");
        review.SuggestedNestedSchemaTypeName.Should().Be("Review");
        review.SuggestedResolverConfig.Should().NotBeNullOrEmpty();
        review.SuggestedResolverConfig.Should().Contain("reviewAuthor");
        review.SuggestedResolverConfig.Should().Contain("reviewBody");
        review.SuggestedResolverConfig.Should().Contain("ratingValue");
        review.Confidence.Should().Be(80);

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

        // url → ticketUrl would be a partial match (confidence 50) — dropped as junk,
        // so no "url" suggestion is returned at all.
        result.Should().NotContain(s => s.SchemaPropertyName == "url");

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
        // mainEntity is a blockContent popular default (confidence 60) with no name match —
        // shown for the user to accept, but not auto-applied.
        mainEntityMapping.IsAutoMapped.Should().BeFalse();

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
        // complexType popular default with no content-property match — shown but not auto-applied.
        result[0].IsAutoMapped.Should().BeFalse();
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
        // complexType popular default with no content-property match — shown but not auto-applied.
        result[0].IsAutoMapped.Should().BeFalse();
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
        // complexType popular default with no content-property match — shown but not auto-applied.
        result[0].IsAutoMapped.Should().BeFalse();
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
        // complexType popular default with no content-property match — shown but not auto-applied.
        result[0].IsAutoMapped.Should().BeFalse();
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
        result[0].Confidence.Should().Be(80);
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
        result[0].Confidence.Should().Be(80);
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
        result[0].Confidence.Should().Be(80);
        result[0].IsAutoMapped.Should().BeTrue();
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
        result[0].Confidence.Should().Be(80);
        result[0].IsAutoMapped.Should().BeTrue();
    }

    #endregion

    #endregion

    #region Type Intelligence Edge Cases

    [Fact]
    public void PrimitiveTypeAcceptedTypes_SuggestedNestedSchemaTypeName_IsNull()
    {
        // A schema property with only primitive accepted types (e.g. "Text")
        // should NOT be treated as complex and should have no nested schema type
        var contentType = CreateContentTypeWithProperties("headline");
        _contentTypeService.Get("article").Returns(contentType);
        _schemaTypeRegistry.GetProperties("Article").Returns(new[]
        {
            new SchemaPropertyInfo
            {
                Name = "headline",
                PropertyType = "Text",
                IsComplexType = false,
                AcceptedTypes = ["Text"]
            }
        });

        var result = _sut.SuggestMappings("article", "Article").ToList();

        result.Should().ContainSingle();
        result[0].SuggestedContentTypePropertyAlias.Should().Be("headline");
        result[0].Confidence.Should().Be(100);
        result[0].SuggestedSourceType.Should().Be("property");
        result[0].SuggestedNestedSchemaTypeName.Should().BeNull();
        result[0].IsComplexType.Should().BeFalse();
    }

    [Fact]
    public void BlockEditorDetection_BlockListAlias_SuggestsBlockContent()
    {
        // A complex schema property matched by a BlockList content property (no popular default)
        // should be detected as blockContent purely based on editor alias
        var contentType = CreateContentTypeWithEditors(
            ("customItems", "Umbraco.BlockList"));
        _contentTypeService.Get("custom").Returns(contentType);
        _schemaTypeRegistry.GetProperties("CustomType").Returns(new[]
        {
            new SchemaPropertyInfo
            {
                Name = "customItems",
                PropertyType = "CustomItem",
                IsComplexType = true,
                AcceptedTypes = ["CustomItem"]
            }
        });

        var result = _sut.SuggestMappings("custom", "CustomType").ToList();

        result.Should().ContainSingle();
        result[0].SuggestedSourceType.Should().Be("blockContent");
        result[0].SuggestedNestedSchemaTypeName.Should().Be("CustomItem");
        result[0].EditorAlias.Should().Be("Umbraco.BlockList");
        result[0].Confidence.Should().Be(100); // exact name match → 100; block editor doesn't lower it
        result[0].IsAutoMapped.Should().BeTrue();
    }

    [Fact]
    public void SuggestMappings_MultiplePartialMatches_AllDroppedAsJunk()
    {
        // Two content properties only partially match the schema property name (both score 50).
        // Partial matches are below the show threshold, so nothing is returned.
        var contentType = CreateContentTypeWithProperties("blogHeadline", "headlineText");
        _contentTypeService.Get("article").Returns(contentType);
        _schemaTypeRegistry.GetProperties("Article").Returns(new[]
        {
            new SchemaPropertyInfo { Name = "headline", PropertyType = "Text" }
        });

        var result = _sut.SuggestMappings("article", "Article").ToList();

        result.Should().BeEmpty("partial-name matches (confidence 50) are dropped");
    }

    #endregion

    #region Confidence Filtering (junk dropped, plausible shown, canonical auto-applied)

    [Fact]
    public void SuggestMappings_GenericBlockFallback_IsDroppedAsJunk()
    {
        // A complex array property with no popular default and no name match falls back to a
        // generic BlockList guess at confidence 40 — pure noise, so it must be dropped.
        var contentType = CreateContentTypeWithEditors(
            ("blocks", "Umbraco.BlockList"));
        _contentTypeService.Get("page").Returns(contentType);
        _schemaTypeRegistry.GetProperties("CustomType").Returns(new[]
        {
            new SchemaPropertyInfo
            {
                Name = "widgets",
                PropertyType = "OneOrMany<Widget>",
                IsComplexType = true,
                AcceptedTypes = ["Widget"]
            }
        });

        var result = _sut.SuggestMappings("page", "CustomType").ToList();

        result.Should().BeEmpty("generic block fallbacks (confidence 40) are below the show threshold");
    }

    [Fact]
    public void SuggestMappings_PlausibleComplexDefault_ShownButNotAutoApplied()
    {
        // A complexType popular default with no content-property match scores 60: plausible,
        // so returned for the user to accept, but never auto-ticked.
        var contentType = CreateContentTypeWithProperties("unrelated");
        _contentTypeService.Get("product").Returns(contentType);
        _schemaTypeRegistry.GetProperties("Product").Returns(new[]
        {
            new SchemaPropertyInfo
            {
                Name = "offers", PropertyType = "Offer",
                IsComplexType = true, AcceptedTypes = ["Offer"]
            }
        });

        var result = _sut.SuggestMappings("product", "Product").ToList();

        result.Should().ContainSingle();
        result[0].Confidence.Should().Be(60);
        result[0].IsAutoMapped.Should().BeFalse("60 is below the auto-apply threshold of 80");
    }

    [Fact]
    public void SuggestMappings_CanonicalBuiltInRows_AreAutoApplied()
    {
        // The built-in url/name/datePublished/dateModified fallbacks are canonical mappings
        // (schema url → node url, name → node Name, dates → Create/Update date) and must clear
        // the auto-apply bar so they stay pre-ticked.
        var contentType = CreateContentTypeWithProperties("unrelated");
        _contentTypeService.Get("page").Returns(contentType);
        _schemaTypeRegistry.GetProperties("Article").Returns(new[]
        {
            new SchemaPropertyInfo { Name = "url", PropertyType = "URL", AcceptedTypes = ["URL"] },
            new SchemaPropertyInfo { Name = "name", PropertyType = "Text", AcceptedTypes = ["Text"] },
            new SchemaPropertyInfo { Name = "datePublished", PropertyType = "Date", AcceptedTypes = ["Date"] },
            new SchemaPropertyInfo { Name = "dateModified", PropertyType = "Date", AcceptedTypes = ["Date"] },
        });

        var result = _sut.SuggestMappings("page", "Article").ToList();

        result.Should().HaveCount(4);
        result.Should().OnlyContain(s => s.Confidence >= 80 && s.IsAutoMapped);
        result.Select(s => s.SuggestedContentTypePropertyAlias).Should()
            .BeEquivalentTo(new[] { "__url", "__name", "__createDate", "__updateDate" });
    }

    [Fact]
    public void SuggestMappings_ConfiguredThresholds_AreHonoured()
    {
        // Lowering the auto-apply threshold to 60 (and show to 40) via options must make the
        // 60-confidence complexType default auto-apply.
        var options = Options.Create(new SchemaAutoMapperOptions
        {
            AutoApplyConfidenceThreshold = 60,
            ShowConfidenceThreshold = 40,
        });
        var sut = new SchemaAutoMapper(_contentTypeService, _schemaTypeRegistry, options);

        var contentType = CreateContentTypeWithProperties("unrelated");
        _contentTypeService.Get("product").Returns(contentType);
        _schemaTypeRegistry.GetProperties("Product").Returns(new[]
        {
            new SchemaPropertyInfo
            {
                Name = "offers", PropertyType = "Offer",
                IsComplexType = true, AcceptedTypes = ["Offer"]
            }
        });

        var result = sut.SuggestMappings("product", "Product").ToList();

        result.Should().ContainSingle();
        result[0].Confidence.Should().Be(60);
        result[0].IsAutoMapped.Should().BeTrue("the configured auto-apply threshold is 60");
    }

    #endregion

    #region RankSchemaProperties

    [Fact]
    public void RankSchemaProperties_UnknownType_ReturnsEmpty()
    {
        // Registry returns empty for an unknown type — ranking should mirror that
        // without throwing.
        _schemaTypeRegistry.GetProperties("DoesNotExist").Returns(Array.Empty<SchemaPropertyInfo>());

        var result = _sut.RankSchemaProperties("DoesNotExist");

        result.Should().BeEmpty();
    }

    [Fact]
    public void RankSchemaProperties_SortsPopularBeforeOthers()
    {
        // "Product.review" is in PopularSchemaDefaults → 100
        // "name" is in GlobalPopularPropertyNames → 80
        // "nutrition" is complex for Product (not a popular default for Product) → 60
        // "color" is plain text, not popular → 30
        _schemaTypeRegistry.GetProperties("Product").Returns(new[]
        {
            new SchemaPropertyInfo { Name = "color", PropertyType = "Text", IsComplexType = false },
            new SchemaPropertyInfo { Name = "nutrition", PropertyType = "NutritionInformation", IsComplexType = true },
            new SchemaPropertyInfo { Name = "name", PropertyType = "Text", IsComplexType = false },
            new SchemaPropertyInfo { Name = "review", PropertyType = "Review", IsComplexType = true },
        });

        var result = _sut.RankSchemaProperties("Product").ToList();

        result.Should().HaveCount(4);
        result.Select(r => r.Name).Should().ContainInOrder("review", "name", "nutrition", "color");
        result.Select(r => r.Confidence).Should().ContainInOrder(100, 80, 60, 30);
    }

    [Fact]
    public void RankSchemaProperties_IsPopular_TrueFor60AndAbove()
    {
        _schemaTypeRegistry.GetProperties("Product").Returns(new[]
        {
            new SchemaPropertyInfo { Name = "review", PropertyType = "Review", IsComplexType = true },           // 100
            new SchemaPropertyInfo { Name = "name", PropertyType = "Text", IsComplexType = false },              // 80
            new SchemaPropertyInfo { Name = "nutrition", PropertyType = "NutritionInformation", IsComplexType = true }, // 60
            new SchemaPropertyInfo { Name = "color", PropertyType = "Text", IsComplexType = false },             // 30
        });

        var result = _sut.RankSchemaProperties("Product").ToList();

        result.Single(r => r.Name == "review").IsPopular.Should().BeTrue();
        result.Single(r => r.Name == "name").IsPopular.Should().BeTrue();
        result.Single(r => r.Name == "nutrition").IsPopular.Should().BeTrue();
        result.Single(r => r.Name == "color").IsPopular.Should().BeFalse();
    }

    #endregion

    #region Structural Enrichment (rich-mapping dispatch branches)

    /// <summary>
    /// Branch 1 — complexType-from-scalar. A complex schema property (Author → Person) that
    /// name-matches a scalar content property is dead at runtime when its inner config is null.
    /// The enricher must fill <c>complexTypeMappings</c> binding the nested type's <c>Name</c>
    /// to the matched scalar so the mapping actually emits a value.
    /// </summary>
    [Fact]
    public void StructuralEnrichment_ComplexTypeFromScalar_FillsNameBinding()
    {
        var contentType = CreateContentTypeWithProperties("title", "authorName");
        _contentTypeService.Get("blogArticle").Returns(contentType);
        _schemaTypeRegistry.GetProperties("BlogPosting").Returns(new[]
        {
            new SchemaPropertyInfo { Name = "author", PropertyType = "Person", IsComplexType = true, AcceptedTypes = ["Person"] }
        });
        _schemaTypeRegistry.GetProperties("Person").Returns(new[]
        {
            new SchemaPropertyInfo { Name = "Name", PropertyType = "Text" },
            new SchemaPropertyInfo { Name = "Description", PropertyType = "Text" },
        });

        var result = _sut.SuggestMappings("blogArticle", "BlogPosting").ToList();

        var author = result.First(s => s.SchemaPropertyName == "author");
        author.SuggestedSourceType.Should().Be("complexType");
        author.SuggestedNestedSchemaTypeName.Should().Be("Person");
        author.SuggestedResolverConfig.Should().NotBeNullOrEmpty();
        author.SuggestedResolverConfig.Should().Contain("complexTypeMappings");
        author.SuggestedResolverConfig.Should().Contain("\"schemaProperty\":\"Name\"");
        author.SuggestedResolverConfig.Should().Contain("\"contentTypePropertyAlias\":\"authorName\"");
    }

    /// <summary>
    /// Branch 1 — prefix-grouping. A match on <c>locationName</c> for a complex <c>Location</c> →
    /// <c>Place</c> property must group the sibling <c>locationAddress</c> too, binding each
    /// camelCase suffix (<c>Name</c>, <c>Address</c>) to the matching property on <c>Place</c>.
    /// </summary>
    [Fact]
    public void StructuralEnrichment_ComplexTypeFromScalar_PrefixGroupsSiblings()
    {
        var contentType = CreateContentTypeWithProperties("title", "locationName", "locationAddress", "organiserName");
        _contentTypeService.Get("eventPage").Returns(contentType);
        _schemaTypeRegistry.GetProperties("Event").Returns(new[]
        {
            new SchemaPropertyInfo { Name = "location", PropertyType = "Place", IsComplexType = true, AcceptedTypes = ["Place"] }
        });
        _schemaTypeRegistry.GetProperties("Place").Returns(new[]
        {
            new SchemaPropertyInfo { Name = "Name", PropertyType = "Text" },
            new SchemaPropertyInfo { Name = "Address", PropertyType = "PostalAddress" },
        });

        var result = _sut.SuggestMappings("eventPage", "Event").ToList();

        var location = result.First(s => s.SchemaPropertyName == "location");
        location.SuggestedSourceType.Should().Be("complexType");
        location.SuggestedNestedSchemaTypeName.Should().Be("Place");
        location.SuggestedResolverConfig.Should().Contain("\"contentTypePropertyAlias\":\"locationName\"");
        location.SuggestedResolverConfig.Should().Contain("\"contentTypePropertyAlias\":\"locationAddress\"");
        location.SuggestedResolverConfig.Should().Contain("\"schemaProperty\":\"Name\"");
        location.SuggestedResolverConfig.Should().Contain("\"schemaProperty\":\"Address\"");
    }

    /// <summary>
    /// Branch 3 — string-list detection. An array-of-text schema property fed by a Block List whose
    /// element type has a single text field must become <c>blockContent</c> with
    /// <c>extractAs:"stringList"</c> pointing at the detected inner property — derived from the block,
    /// not a hard-coded alias.
    /// </summary>
    [Fact]
    public void StructuralEnrichment_StringList_DetectsSingleTextFieldBlock()
    {
        var dataTypeService = Substitute.For<IDataTypeService>();
        var sut = new SchemaAutoMapper(_contentTypeService, _schemaTypeRegistry, null, dataTypeService);

        var dataTypeKey = Guid.NewGuid();
        var elementKey = Guid.NewGuid();

        var blockProp = Substitute.For<IPropertyType>();
        blockProp.Alias.Returns("ingredients");
        blockProp.PropertyEditorAlias.Returns("Umbraco.BlockList");
        blockProp.DataTypeKey.Returns(dataTypeKey);
        var contentType = Substitute.For<IContentType>();
        contentType.PropertyTypes.Returns(new[] { blockProp });
        contentType.CompositionPropertyTypes.Returns(new[] { blockProp });
        _contentTypeService.Get("recipePage").Returns(contentType);

        var dataType = Substitute.For<IDataType>();
        dataType.ConfigurationData.Returns(new Dictionary<string, object>
        {
            ["blocks"] = $"[{{\"contentElementTypeKey\":\"{elementKey}\"}}]"
        });
        dataTypeService.GetAsync(dataTypeKey).Returns(Task.FromResult<IDataType?>(dataType));

        var innerProp = Substitute.For<IPropertyType>();
        innerProp.Alias.Returns("ingredient");
        innerProp.Name.Returns("Ingredient");
        innerProp.PropertyEditorAlias.Returns("Umbraco.TextBox");
        var elementType = Substitute.For<IContentType>();
        elementType.Alias.Returns("ingredientItem");
        elementType.Name.Returns("Ingredient Item");
        elementType.CompositionPropertyTypes.Returns(new[] { innerProp });
        _contentTypeService.Get(elementKey).Returns(elementType);

        _schemaTypeRegistry.GetProperties("Recipe").Returns(new[]
        {
            new SchemaPropertyInfo { Name = "recipeIngredient", PropertyType = "Text", IsComplexType = false, AcceptedTypes = ["String"] }
        });

        var result = sut.SuggestMappings("recipePage", "Recipe").ToList();

        var ingredient = result.First(s => s.SchemaPropertyName == "recipeIngredient");
        ingredient.SuggestedSourceType.Should().Be("blockContent");
        ingredient.SuggestedNestedSchemaTypeName.Should().BeNull();
        ingredient.SuggestedResolverConfig.Should().Contain("stringList");
        ingredient.SuggestedResolverConfig.Should().Contain("\"contentProperty\":\"ingredient\"");
    }

    /// <summary>
    /// Branch 4 — range-validation repair. A rich suggestion whose nested type is outside the target
    /// property's accepted range must be re-pointed onto an in-range complex type, so Schema.NET's
    /// typed setter does not silently discard the value.
    /// </summary>
    [Fact]
    public void StructuralEnrichment_RangeRepair_RedirectsOutOfRangeNestedType()
    {
        var registry = Substitute.For<ISchemaTypeRegistry>();
        registry.GetProperties("Organization").Returns(new[] { new SchemaPropertyInfo { Name = "Name" } });
        registry.GetType("Organization").Returns(new SchemaTypeInfo { Name = "Organization" });
        // Real Schema.NET CLR types so the assignability check sees that Person does NOT implement
        // IOrganization (out of range), forcing the redirect.
        registry.GetClrType("Person").Returns(typeof(Schema.NET.Person));
        registry.GetClrType("Organization").Returns(typeof(Schema.NET.Organization));

        var enricher = new StructuralEnricher(
            registry,
            (name, candidates) => candidates.FirstOrDefault(a => string.Equals(a, name, StringComparison.OrdinalIgnoreCase)),
            showThreshold: 60);

        var suggestion = new PropertyMappingSuggestion
        {
            SchemaPropertyName = "about",
            SuggestedSourceType = "complexType",
            SuggestedNestedSchemaTypeName = "Person", // not in the accepted range below
            AcceptedTypes = ["Organization"],
        };

        enricher.Enrich(
            new List<PropertyMappingSuggestion> { suggestion },
            Array.Empty<string>(),
            _ => Array.Empty<BlockElementTypeInfo>());

        suggestion.SuggestedNestedSchemaTypeName.Should().Be("Organization",
            "Person is out of range for a property that only accepts Organization");
    }

    #endregion

    #region Media logo complexType trap (regression)

    [Theory]
    [InlineData("RealEstateAgent")]
    [InlineData("Organization")]
    [InlineData("LocalBusiness")]
    public void SuggestMappings_LogoMediaPicker_StaysPropertySourced_NotComplexType(string schemaTypeName)
    {
        // A MediaPicker3 'logo' property must be suggested as a plain property mapping —
        // MediaPickerResolver already yields a fully-populated ImageObject at render time
        // (exactly like 'image', which has no popular default and therefore works).
        // Adopting the "{Type}.logo" popular default (complexType/ImageObject, null config)
        // is a trap: StructuralEnricher then binds ImageObject.Name <- the media alias, and
        // the renderer drops the resolved ImageObject on string-only ImageObject.Name,
        // emitting an empty {"@type":"ImageObject"} shell.
        var contentType = CreateContentTypeWithEditors(("logo", "Umbraco.MediaPicker3"));
        _contentTypeService.Get("siteSettings").Returns(contentType);
        _schemaTypeRegistry.GetProperties(schemaTypeName).Returns(new[]
        {
            new SchemaPropertyInfo
            {
                Name = "logo",
                PropertyType = "OneOrMany<Values<IImageObject, Uri>>",
                IsComplexType = true,
                AcceptedTypes = ["ImageObject", "URL"]
            }
        });

        var result = _sut.SuggestMappings("siteSettings", schemaTypeName).ToList();

        var logo = result.Should().ContainSingle(s => s.SchemaPropertyName == "logo").Subject;
        logo.SuggestedContentTypePropertyAlias.Should().Be("logo");
        logo.SuggestedSourceType.Should().Be("property",
            "a media picker resolves to a complete ImageObject via MediaPickerResolver — " +
            "the complexType popular default strands it in an empty shell");
        logo.SuggestedNestedSchemaTypeName.Should().BeNull(
            "property-sourced media needs no nested type — the resolver picks ImageObject itself");
        logo.SuggestedResolverConfig.Should().BeNull(
            "property-sourced media needs no inner complexTypeMappings config");
    }

    [Fact]
    public void SuggestMappings_NoLogoishProperty_DoesNotEmitDeadLogoRow()
    {
        // The popular default Organization.logo = complexType/ImageObject(null config).
        // With NO logo-ish content property there is nothing for an inner config to bind:
        // the suggestion would be a dead row (no ContentTypePropertyAlias, no config) that
        // renders an empty ImageObject shell if the editor accepts it. It must not surface.
        var contentType = CreateContentTypeWithEditors(("pageTitle", "Umbraco.TextBox"));
        _contentTypeService.Get("siteSettings").Returns(contentType);
        _schemaTypeRegistry.GetProperties("Organization").Returns(new[]
        {
            new SchemaPropertyInfo
            {
                Name = "logo",
                PropertyType = "OneOrMany<Values<IImageObject, Uri>>",
                IsComplexType = true,
                AcceptedTypes = ["ImageObject", "URL"]
            }
        });

        var result = _sut.SuggestMappings("siteSettings", "Organization").ToList();

        result.Should().NotContain(
            s => s.SchemaPropertyName == "logo" && string.IsNullOrEmpty(s.SuggestedContentTypePropertyAlias),
            "a logo popular default with no bindable content property is a dead complexType row");
    }

    #endregion
}
