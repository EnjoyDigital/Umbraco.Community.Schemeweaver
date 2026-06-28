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
        // Real registry so the range-aware target check (IsCreativeWork) walks the
        // genuine Schema.NET parent chain (WPHeader/Review -> CreativeWork; Person/
        // Place/Service/Organization -> not).
        _sut = new BlockSchemaSuggester(_autoMapper, new SchemaTypeRegistry());
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

    // A block whose own property is a nested Block List of faq items: the parent route gets a
    // property mapping keyed by the nested block property, carrying the nested block's routes.
    [Fact]
    public void Suggest_NestedBlockProperty_AttachesNestedRoutesToParentRoute()
    {
        var nestedFaq = Element("faqItemBlock", "FAQ Item", "question", "answer");

        var section = new BlockElementTypeInfo
        {
            Alias = "faqSectionBlock",
            Name = "FAQ Section",
            Properties = ["items"],
            PropertyInfos =
            [
                new BlockElementPropertyInfo
                {
                    Alias = "items",
                    Name = "Items",
                    EditorAlias = "Umbraco.BlockList",
                    NestedBlockElementTypes = [nestedFaq]
                }
            ]
        };

        var result = _sut.Suggest([section]).ToList();

        result.Should().ContainSingle();
        var parentRoute = result[0].Routes.Should().ContainSingle().Subject;
        parentRoute.BlockAlias.Should().Be("faqSectionBlock");

        var nestedMapping = parentRoute.PropertyMappings
            .Should().ContainSingle(pm => pm.ContentProperty == "items").Subject;
        nestedMapping.Routes.Should().NotBeNullOrEmpty();
        nestedMapping.Routes!.Should().ContainSingle(r => r.BlockAlias == "faqItemBlock"
            && r.NestedSchemaType == "Question");
    }

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

    // --- v3 §3b: pre-fill stripHtml on a rich-text source feeding a plain-text nested property ---

    [Fact]
    public void Suggest_RichTextNestedPropertyToPlainTextTarget_PrefillsStripHtml()
    {
        _autoMapper.SuggestMappings("faqBlock", "Question").Returns(new List<PropertyMappingSuggestion>
        {
            new()
            {
                SchemaPropertyName = "name",
                SuggestedContentTypePropertyAlias = "answer",
                SuggestedSourceType = "property",
                EditorAlias = "Umbraco.RichText",
                AcceptedTypes = ["String"],
            }
        });

        var result = _sut.Suggest([Element("faqBlock", "FAQ Block", "question", "answer")]).ToList();

        var mapping = result[0].Routes[0].PropertyMappings.Should().ContainSingle().Subject;
        mapping.TransformType.Should().Be("stripHtml");
    }

    [Fact]
    public void Suggest_NonRichTextNestedProperty_NoTransform()
    {
        _autoMapper.SuggestMappings("faqBlock", "Question").Returns(new List<PropertyMappingSuggestion>
        {
            new()
            {
                SchemaPropertyName = "name",
                SuggestedContentTypePropertyAlias = "question",
                SuggestedSourceType = "property",
                EditorAlias = "Umbraco.TextBox",
                AcceptedTypes = ["String"],
            }
        });

        var result = _sut.Suggest([Element("faqBlock", "FAQ Block", "question", "answer")]).ToList();

        var mapping = result[0].Routes[0].PropertyMappings.Should().ContainSingle().Subject;
        mapping.TransformType.Should().BeNull();
    }

    // Person is not a CreativeWork, so it cannot live under hasPart (which would silently
    // drop it at generation time) — it routes to the Thing-range `about` instead.
    [Fact]
    public void Suggest_TeamBlock_RoutesToPersonAtAbout()
    {
        var result = _sut.Suggest([Element("teamMemberBlock", "Team Member", "memberName")]).ToList();

        result.Should().ContainSingle();
        result[0].SchemaProperty.Should().Be("about");
        result[0].Routes[0].NestedSchemaType.Should().Be("Person");
    }

    [Fact]
    public void Suggest_MapBlock_RoutesToPlaceAtAbout()
    {
        var result = _sut.Suggest([Element("mapBlock", "Map", "latitude", "longitude")]).ToList();

        result.Should().ContainSingle();
        result[0].SchemaProperty.Should().Be("about");
        result[0].Routes[0].NestedSchemaType.Should().Be("Place");
    }

    // Regression: a non-CreativeWork type (Service) must never be routed to hasPart,
    // because Schema.NET's typed hasPart (OneOrMany<ICreativeWork>) silently discards it.
    [Fact]
    public void Suggest_FeatureBlock_RoutesServiceToAbout_NotHasPart()
    {
        var result = _sut.Suggest([Element("featureBlock", "Feature", "title", "description")]).ToList();

        result.Should().ContainSingle();
        result[0].Routes[0].NestedSchemaType.Should().Be("Service");
        result[0].SchemaProperty.Should().Be("about", "Service is not a CreativeWork so hasPart would drop it");
    }

    // CreativeWork-derived types (WPHeader -> WebPageElement -> CreativeWork) stay on hasPart.
    [Fact]
    public void Suggest_HeroBlock_RoutesWPHeaderToHasPart()
    {
        var result = _sut.Suggest([Element("heroBlock", "Hero", "title", "subtitle")]).ToList();

        result.Should().ContainSingle();
        result[0].Routes[0].NestedSchemaType.Should().Be("WPHeader");
        result[0].SchemaProperty.Should().Be("hasPart");
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

        // mainEntity (faq) + about (team + map, neither a CreativeWork). Rich text skipped.
        result.Should().HaveCount(2);

        var mainEntity = result.Single(r => r.SchemaProperty == "mainEntity");
        mainEntity.Routes.Should().ContainSingle();
        mainEntity.Routes[0].NestedSchemaType.Should().Be("Question");

        var about = result.Single(r => r.SchemaProperty == "about");
        about.Routes.Should().HaveCount(2);
        about.Routes.Select(r => r.NestedSchemaType).Should().BeEquivalentTo(["Person", "Place"]);
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
