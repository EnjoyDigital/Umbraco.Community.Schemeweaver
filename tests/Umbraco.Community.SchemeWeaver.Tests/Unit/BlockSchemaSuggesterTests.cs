using FluentAssertions;
using NSubstitute;
using Xunit;
using Umbraco.Community.SchemeWeaver.Models.Api;
using Umbraco.Community.SchemeWeaver.Services;

namespace Umbraco.Community.SchemeWeaver.Tests.Unit;

public class BlockSchemaSuggesterTests
{
    private readonly ISchemaAutoMapper _autoMapper = Substitute.For<ISchemaAutoMapper>();
    private readonly BlockSchemaSuggester _sut;

    public BlockSchemaSuggesterTests()
    {
        // Default: the per-property auto-mapper returns nothing, so routes carry no
        // property mappings. Catalogue/target/dominance behaviour is what we assert here.
        _autoMapper.SuggestMappings(Arg.Any<string>(), Arg.Any<string>())
            .Returns([]);
        _sut = new BlockSchemaSuggester(_autoMapper);
    }

    private static BlockElementTypeInfo Element(string alias, string? name = null, params string[] propertyAliases)
        => new()
        {
            Alias = alias,
            Name = name ?? alias,
            Properties = propertyAliases.ToList(),
            PropertyInfos = propertyAliases
                .Select(p => new BlockElementPropertyInfo { Alias = p, Name = p, EditorAlias = "Umbraco.TextBox" })
                .ToList()
        };

    [Fact]
    public void Suggest_FaqBlock_RoutesToQuestionAtMainEntity()
    {
        var result = _sut.Suggest([Element("faqBlock", "FAQ Block", "question", "answer")]).ToList();

        result.Should().ContainSingle();
        result[0].SchemaProperty.Should().Be("mainEntity");
        result[0].Routes.Should().ContainSingle();
        result[0].Routes[0].BlockAlias.Should().Be("faqBlock");
        result[0].Routes[0].NestedSchemaType.Should().Be("Question");
    }

    [Fact]
    public void Suggest_TeamBlock_RoutesToPersonAtHasPart()
    {
        var result = _sut.Suggest([Element("teamMemberBlock", "Team Member", "memberName")]).ToList();

        result.Should().ContainSingle();
        result[0].SchemaProperty.Should().Be("hasPart");
        result[0].Routes[0].NestedSchemaType.Should().Be("Person");
    }

    [Fact]
    public void Suggest_MapBlock_RoutesToPlaceAtHasPart()
    {
        var result = _sut.Suggest([Element("mapBlock", "Map", "latitude", "longitude")]).ToList();

        result.Should().ContainSingle();
        result[0].SchemaProperty.Should().Be("hasPart");
        result[0].Routes[0].NestedSchemaType.Should().Be("Place");
    }

    [Fact]
    public void Suggest_RichTextBlock_IsSkipped()
    {
        var result = _sut.Suggest([Element("richTextBlock", "Rich Text", "body")]).ToList();

        result.Should().BeEmpty("rich text blocks carry no schema entity");
    }

    [Theory]
    [InlineData("ctaBlock", "Call To Action")]
    [InlineData("linkButtonBlock", "Link Button")]
    public void Suggest_ContentlessBlocks_AreSkipped(string alias, string name)
    {
        var result = _sut.Suggest([Element(alias, name, "url", "label")]).ToList();

        result.Should().BeEmpty();
    }

    [Fact]
    public void Suggest_UnrecognisedBlock_IsSkipped()
    {
        // No catalogue keyword hit and no skip keyword — dropped rather than emitting junk.
        var result = _sut.Suggest([Element("widgetBlock", "Widget", "config")]).ToList();

        result.Should().BeEmpty();
    }

    [Fact]
    public void Suggest_MixedPage_GroupsRoutesByTargetProperty()
    {
        var result = _sut.Suggest(
        [
            Element("faqBlock", "FAQ", "question", "answer"),
            Element("teamBlock", "Team", "personName"),
            Element("mapBlock", "Map", "lat", "lng"),
            Element("richTextBlock", "Rich Text", "body")
        ]).ToList();

        // mainEntity (faq) + hasPart (team + map). Rich text skipped.
        result.Should().HaveCount(2);

        var mainEntity = result.Single(r => r.SchemaProperty == "mainEntity");
        mainEntity.Routes.Should().ContainSingle();
        mainEntity.Routes[0].NestedSchemaType.Should().Be("Question");

        var hasPart = result.Single(r => r.SchemaProperty == "hasPart");
        hasPart.Routes.Should().HaveCount(2);
        hasPart.Routes.Select(r => r.NestedSchemaType).Should().BeEquivalentTo(["Person", "Place"]);
    }

    // Dominance rule: at most ONE block element type may target mainEntity.
    [Fact]
    public void Suggest_TwoMainEntityCandidates_OnlyOneKeepsMainEntity()
    {
        var result = _sut.Suggest(
        [
            Element("faqBlock", "FAQ", "question"),
            Element("questionAccordion", "Question Accordion", "question")
        ]).ToList();

        var mainEntityRouteCount = result
            .Where(r => r.SchemaProperty == "mainEntity")
            .SelectMany(r => r.Routes)
            .Count();
        mainEntityRouteCount.Should().Be(1, "only the dominant block type may claim mainEntity");

        // The demoted candidate falls back to hasPart — two routes total, no loss.
        var totalRoutes = result.SelectMany(r => r.Routes).Count();
        totalRoutes.Should().Be(2);
        result.Should().Contain(r => r.SchemaProperty == "hasPart");
    }

    [Fact]
    public void Suggest_ReusesAutoMapperForPerPropertyMappings()
    {
        _autoMapper.SuggestMappings("teamBlock", "Person").Returns(
        [
            new PropertyMappingSuggestion
            {
                SchemaPropertyName = "name",
                SuggestedContentTypePropertyAlias = "memberName",
                SuggestedSourceType = "property",
                Confidence = 80
            },
            // built-in alias is filtered out — does not resolve on block elements
            new PropertyMappingSuggestion
            {
                SchemaPropertyName = "url",
                SuggestedContentTypePropertyAlias = "__url",
                SuggestedSourceType = "property",
                Confidence = 80
            },
            // non-property source type is filtered out
            new PropertyMappingSuggestion
            {
                SchemaPropertyName = "worksFor",
                SuggestedContentTypePropertyAlias = null,
                SuggestedSourceType = "reference",
                Confidence = 70
            }
        ]);

        var result = _sut.Suggest([Element("teamBlock", "Team", "memberName")]).ToList();

        var route = result.Single().Routes.Single();
        route.PropertyMappings.Should().ContainSingle();
        route.PropertyMappings[0].SchemaProperty.Should().Be("name");
        route.PropertyMappings[0].ContentProperty.Should().Be("memberName");
    }
}
