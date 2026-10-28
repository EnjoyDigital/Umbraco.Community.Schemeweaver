using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Umbraco.Community.SchemeWeaver.Seo.Templates;
using Xunit;

namespace Umbraco.Community.SchemeWeaver.Tests.Unit.Seo;

public class SeoTemplateCatalogueTests
{
    private readonly SeoTemplateCatalogue _sut = SeoTestSupport.Catalogue;

    [Fact]
    public void Loads_TheEmbeddedCatalogue_WithEveryKindResolvable()
    {
        _sut.All.Should().HaveCountGreaterThanOrEqualTo(11);
        _sut.All.Select(t => t.Id).Should().OnlyHaveUniqueItems();
        _sut.All.Should().Contain(t => t.Id == "article" && t.SchemaType == "Article");
        _sut.Retired.Should().Contain(r => r.SchemaType == "FAQPage");
        _sut.BusinessTypes.Should().Contain(b => b.SchemaType == "Dentist");
    }

    [Fact]
    public void SlotKinds_PopulateAcceptedEditorsAndBuiltIns()
    {
        var headline = _sut.Get("article")!.FindSlot("headline")!;
        headline.AcceptedEditorAliases.Should().Contain("Umbraco.TextBox");
        headline.BuiltIns.Should().Contain("__name");

        var published = _sut.Get("article")!.FindSlot("datePublished")!;
        published.BuiltIns.Should().Contain("__createDate");
        published.DefaultSource!.Alias.Should().Be("__createDate");

        // Nested slots get defaults too.
        var authorName = _sut.Get("article")!.FindSlot("author")!.FindSubSlot("name")!;
        authorName.AcceptedEditorAliases.Should().NotBeEmpty();
    }

    [Theory]
    [InlineData("Article", "article")]
    [InlineData("BlogPosting", "article")]
    [InlineData("NewsArticle", "article")]
    [InlineData("ScholarlyArticle", "article")] // not named in the catalogue: found via the ancestor walk
    [InlineData("AboutPage", "web-page")]
    [InlineData("WebPage", "web-page")]
    [InlineData("Dentist", "local-business")] // curated business type
    [InlineData("Restaurant", "local-business")]
    [InlineData("FAQPage", "faq")]
    [InlineData("MobileApplication", "software-app")]
    public void FindForSchemaType_MapsTypesToKinds(string schemaType, string expectedId) =>
        _sut.FindForSchemaType(schemaType)!.Id.Should().Be(expectedId);

    [Theory]
    [InlineData("Thing")]
    [InlineData("Organization")]
    [InlineData("NotAType")]
    [InlineData("")]
    public void FindForSchemaType_UnknownOrUnowned_ReturnsNull(string schemaType) =>
        _sut.FindForSchemaType(schemaType).Should().BeNull();

    [Fact]
    public void AllowedPageTypes_UsesVariants_OrBusinessTypes_OrSelf()
    {
        _sut.AllowedPageTypes(_sut.Get("article")!).Select(v => v.SchemaType)
            .Should().Contain(["Article", "BlogPosting", "NewsArticle"]);
        _sut.AllowedPageTypes(_sut.Get("local-business")!).Select(v => v.SchemaType)
            .Should().Contain("Dentist");
        _sut.AllowedPageTypes(_sut.Get("recipe")!).Select(v => v.SchemaType)
            .Should().Equal("Recipe");
    }

    [Fact]
    public void RetiredKinds_AreFlagged()
    {
        _sut.Get("faq")!.IsRetired.Should().BeTrue();
        _sut.Get("how-to")!.IsRetired.Should().BeTrue();
        _sut.Get("article")!.IsRetired.Should().BeFalse();
    }

    [Fact]
    public void Validation_RejectsAVariantThatIsNotASubtype()
    {
        var document = new SeoCatalogueDocument
        {
            SlotKinds = { ["text"] = new SeoSlotKindDefaults() },
            Templates =
            {
                new SeoTemplate
                {
                    Id = "bad",
                    SchemaType = "Article",
                    Variants = [new SeoTemplateVariant { SchemaType = "Recipe" }],
                },
            },
        };

        var act = () => new SeoTemplateCatalogue(SeoTestSupport.Registry, document, NullLogger<SeoTemplateCatalogue>.Instance);
        act.Should().Throw<InvalidOperationException>().WithMessage("*Recipe*not a subtype*");
    }
}
