using FluentAssertions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Umbraco.Cms.Core.Models;
using Umbraco.Cms.Core.Services;
using Umbraco.Community.SchemeWeaver.Models.Api;
using Umbraco.Community.SchemeWeaver.Services;
using Umbraco.Community.SchemeWeaver.Services.ValueSchemas;
using Umbraco.Community.SchemeWeaver.Tests.Unit.TypeSafe.TestSupport;
using Umbraco.Community.SchemeWeaver.TypeSafe.Client;
using Umbraco.Community.SchemeWeaver.TypeSafe.Configuration;
using Umbraco.Community.SchemeWeaver.TypeSafe.Services;
using Xunit;
using static Umbraco.Community.SchemeWeaver.Tests.Unit.TypeSafe.TestSupport.TypeSafeTestContentTypes;

namespace Umbraco.Community.SchemeWeaver.Tests.Unit.TypeSafe;

/// <summary>
/// v2's third lifted limit: one content property feeding two schema properties. Two mechanisms,
/// both code: the bind answer's runner-up probability becomes a second claim when it clears
/// <see cref="TypeSafeOptions.SecondaryBindingMinProbability"/> and no other content property
/// claimed that schema property; and the Headline/Name rule fills whichever of the pair the
/// model left empty from the same source. Driven by a scripted <see cref="FakeTypeSafeClient"/>
/// because the distributions are the subject.
/// </summary>
/// <remarks>
/// A runner-up can never hold more than half of a distribution the chosen option leads, so it
/// could never clear the core's default show bar of 60. A secondary row is therefore gated on
/// <see cref="TypeSafeOptions.SecondaryBindingMinProbability"/> itself, at the core's DEFAULT
/// thresholds, and is never pre-ticked (<c>IsAutoMapped</c> stays false whatever the auto-apply
/// bar); a Block List property never gets one, because a runner-up block row would run the
/// whole route planner for a row that is only ever offered unticked.
/// </remarks>
public class TypeSafeMultipleTargetsTests
{
    private readonly IContentTypeService _contentTypes = Substitute.For<IContentTypeService>();
    private readonly IPropertyValueSchemaService _valueSchemas = Substitute.For<IPropertyValueSchemaService>();
    private readonly IServiceProvider _provider = Substitute.For<IServiceProvider>();
    private readonly TypeSafeOptions _options = new() { ApiKey = "test-key", EnableCrossNodeSources = false };
    private readonly SchemaAutoMapperOptions _autoMapperOptions = new();
    private readonly RecordingLogger<TypeSafePropertyMapper> _logger = new();

    public TypeSafeMultipleTargetsTests()
    {
        _valueSchemas.GetDataTypeValueSchemaAsync(Arg.Any<Guid>()).Returns(Task.FromResult<string?>(null));
        // Build first, then stub: the builder itself configures substitutes, which would
        // otherwise steal NSubstitute's "last call" from Get().
        var blogPost = ContentType("blogPost", "Blog Post", ("title", TextBox), ("summary", TextArea));
        _contentTypes.Get("blogPost").Returns(blogPost);
    }

    private TypeSafePropertyMapper CreateMapper(ITypeSafeClient client) => new(
        client,
        SharedSchemaRegistry.Graph,
        SharedSchemaRegistry.Registry,
        _contentTypes,
        _valueSchemas,
        _provider,
        Options.Create(_options),
        Options.Create(_autoMapperOptions),
        _logger);

    private static PropertyMappingSuggestion Row(IReadOnlyList<PropertyMappingSuggestion> rows, string schemaProperty)
        => rows.Should().ContainSingle(r => string.Equals(r.SchemaPropertyName, schemaProperty, StringComparison.OrdinalIgnoreCase),
            $"exactly one row for {schemaProperty} is expected").Subject;

    /// <summary>
    /// A client that answers each listed bind question with the given distribution over option
    /// NAMES (resolved to the offered keys), <c>__none</c> to every other bind, and "plain
    /// value" to every entity question.
    /// </summary>
    private static FakeTypeSafeClient Binds(params (string Alias, (string Property, double P)[] Distribution)[] binds)
        => new((id, q) =>
        {
            if (q.Type == "noul")
                return FakeTypeSafeClient.Noul(0.03);

            foreach (var (alias, distribution) in binds)
            {
                if (id != "bind__" + alias)
                    continue;

                var probabilities = new Dictionary<string, double>(StringComparer.Ordinal);
                foreach (var (property, p) in distribution)
                    probabilities[FakeTypeSafeClient.OptionNamed(q, property) ?? throw new InvalidOperationException($"{property} is not offered")] = p;
                return FakeTypeSafeClient.Distribution(probabilities);
            }

            var options = FakeTypeSafeClient.CriteriaOf(q).Keys.ToList();
            return FakeTypeSafeClient.Choice(options.Contains("__none") ? "__none" : options.Contains("__stop") ? "__stop" : options[0], 0.9);
        });

    [Fact]
    public async Task RunnerUp_AboveTheFloor_WithNoOtherClaimant_BecomesASecondRowAtItsProbability()
    {
        // Core defaults: show 60, auto-apply 80. The runner-up's 35 is under the show bar and
        // must still come through, on the secondary floor of 30.
        var client = Binds(("title", [("Headline", 0.65), ("AlternativeHeadline", 0.35)]));

        var rows = await CreateMapper(client).MapAsync("blogPost", "BlogPosting", []);

        var headline = Row(rows, "Headline");
        headline.Confidence.Should().Be(65);
        headline.IsAutoMapped.Should().BeFalse("65 is under the auto-apply bar of 80, exactly as for the heuristic");
        var secondary = Row(rows, "AlternativeHeadline");
        secondary.SuggestedContentTypePropertyAlias.Should().Be("title", "the same content property feeds a second schema property");
        secondary.SuggestedSourceType.Should().Be("property");
        secondary.Confidence.Should().Be(35, "a secondary row carries the runner-up's own probability, not the primary's confidence");
        secondary.IsAutoMapped.Should().BeFalse();
        client.AskedIds.Count(id => id == "bind__title").Should().Be(1, "the runner-up is read from the distribution already returned; it costs no question");
    }

    [Fact]
    public async Task RunnerUp_IsNeverPreTicked_EvenAboveTheAutoApplyBar()
    {
        // Bars lowered so the runner-up clears BOTH of them: it must still come through unticked.
        _autoMapperOptions.ShowConfidenceThreshold = 30;
        _autoMapperOptions.AutoApplyConfidenceThreshold = 40;
        var client = Binds(("title", [("Headline", 0.55), ("AlternativeHeadline", 0.45)]));

        var rows = await CreateMapper(client).MapAsync("blogPost", "BlogPosting", []);

        Row(rows, "Headline").IsAutoMapped.Should().BeTrue("the primary follows the auto-apply bar as usual");
        var secondary = Row(rows, "AlternativeHeadline");
        secondary.Confidence.Should().Be(45);
        secondary.IsAutoMapped.Should().BeFalse("a runner-up is offered for a click, never applied on the editor's behalf");
    }

    [Fact]
    public async Task RunnerUp_BelowTheFloor_DoesNotBecomeARow()
    {
        var client = Binds(("title", [("Headline", 0.75), ("AlternativeHeadline", 0.25)]));

        var rows = await CreateMapper(client).MapAsync("blogPost", "BlogPosting", []);

        Row(rows, "Headline").Confidence.Should().Be(75);
        rows.Should().NotContain(r => r.SchemaPropertyName == "AlternativeHeadline",
            "0.25 is under the default SecondaryBindingMinProbability of 0.30");
    }

    [Fact]
    public async Task RunnerUp_UnderALoweredFloor_ComesThroughOnThatFloor()
    {
        _options.SecondaryBindingMinProbability = 0.20;
        var client = Binds(("title", [("Headline", 0.75), ("AlternativeHeadline", 0.25)]));

        var rows = await CreateMapper(client).MapAsync("blogPost", "BlogPosting", []);

        var secondary = Row(rows, "AlternativeHeadline");
        secondary.Confidence.Should().Be(25, "the floor is the option, not the core's show threshold of 60");
        secondary.IsAutoMapped.Should().BeFalse();
    }

    [Fact]
    public async Task RunnerUp_WhosePropertyAnotherContentPropertyClaimed_DoesNotBecomeARow()
    {
        var client = Binds(
            ("title", [("Headline", 0.6), ("Description", 0.4)]),
            ("summary", [("Description", 0.9)]));

        var rows = await CreateMapper(client).MapAsync("blogPost", "BlogPosting", []);

        var description = Row(rows, "Description");
        description.SuggestedContentTypePropertyAlias.Should().Be("summary", "a primary claim is never displaced by a runner-up");
        description.Confidence.Should().Be(90);
        rows.Where(r => r.SuggestedContentTypePropertyAlias == "title").Select(r => r.SchemaPropertyName)
            .Should().BeEquivalentTo(["Headline", "Name"], "title feeds Headline (and Name by rule) only");
    }

    [Fact]
    public async Task RunnerUp_OfABlockListProperty_NeverBecomesARow()
    {
        // A runner-up block row would run the whole route planner (root/skip, descent, inner
        // questions) for a row that is only ever offered unticked, so a Block List property
        // keeps its primary only, whatever the runner-up's probability.
        var schemeWeaver = Substitute.For<ISchemeWeaverService>();
        schemeWeaver.GetBlockElementTypesAsync("sectionsPage", "sections")
            .Returns(Task.FromResult<IEnumerable<BlockElementTypeInfo>>([]));
        _provider.GetService(typeof(ISchemeWeaverService)).Returns(schemeWeaver);
        // Build first, then stub (the builder configures substitutes of its own).
        var sectionsPage = ContentType("sectionsPage", "Sections Page", ("title", TextBox), ("sections", BlockList));
        _contentTypes.Get("sectionsPage").Returns(sectionsPage);
        var client = Binds(
            ("title", [("Headline", 0.9)]),
            ("sections", [("HasPart", 0.6), ("About", 0.4)]));

        var rows = await CreateMapper(client).MapAsync("sectionsPage", "BlogPosting", []);

        Row(rows, "HasPart").SuggestedContentTypePropertyAlias.Should().Be("sections");
        rows.Should().NotContain(r => string.Equals(r.SchemaPropertyName, "About", StringComparison.OrdinalIgnoreCase),
            "a Block List's runner-up is never a secondary row");
    }

    [Fact]
    public async Task HeadlineBound_NameEmpty_YieldsNameFromTheSameProperty()
    {
        var client = Binds(("title", [("Headline", 0.9)]));

        var rows = await CreateMapper(client).MapAsync("blogPost", "BlogPosting", []);

        var name = Row(rows, "Name");
        name.SuggestedContentTypePropertyAlias.Should().Be("title");
        name.SuggestedSourceType.Should().Be("property");
        name.Confidence.Should().Be(90, "the rule copies the bound side's confidence");
        name.IsAutoMapped.Should().BeTrue();
        Row(rows, "Headline").SuggestedContentTypePropertyAlias.Should().Be("title");
    }

    [Fact]
    public async Task BlockList_SplitAcrossContainerTargets_IsKeptAtTheFamilysMass()
    {
        // Same distribution as the runner-up rule reads, different rule: which container a
        // block list belongs on is a secondary choice, so the winner's 0.40 (under the show bar
        // of 60) is judged by the family's 0.85 and the list is kept, pre-ticked, on the winner.
        var schemeWeaver = Substitute.For<ISchemeWeaverService>();
        schemeWeaver.GetBlockElementTypesAsync("sectionsPage", "sections")
            .Returns(Task.FromResult<IEnumerable<BlockElementTypeInfo>>([]));
        _provider.GetService(typeof(ISchemeWeaverService)).Returns(schemeWeaver);
        var sectionsPage = ContentType("sectionsPage", "Sections Page", ("title", TextBox), ("sections", BlockList));
        _contentTypes.Get("sectionsPage").Returns(sectionsPage);
        var client = Binds(
            ("title", [("Headline", 0.9)]),
            ("sections", [("HasPart", 0.40), ("MainEntity", 0.35), ("About", 0.10), ("Keywords", 0.05)]));

        var rows = await CreateMapper(client).MapAsync("sectionsPage", "BlogPosting", []);

        var hasPart = Row(rows, "HasPart");
        hasPart.SuggestedContentTypePropertyAlias.Should().Be("sections");
        hasPart.SuggestedSourceType.Should().Be("blockContent");
        hasPart.Confidence.Should().Be(85, "0.40 + 0.35 + 0.10 over the container family, not the winner's 0.40; Keywords is not a container");
        hasPart.IsAutoMapped.Should().BeTrue();
        rows.Should().NotContain(r => r.SchemaPropertyName == "MainEntity", "the family rule keeps the winner only");
    }

    [Fact]
    public async Task RunnerUp_OnTheOtherOfThePair_DoesNotPreEmptTheHeadlineNameRule()
    {
        // The pair is exactly where the model spreads probability. The rule must win: Name at
        // the bound side's 60 from the same source, not a 35 runner-up row.
        var client = Binds(("title", [("Headline", 0.6), ("Name", 0.35)]));

        var rows = await CreateMapper(client).MapAsync("blogPost", "BlogPosting", []);

        var name = Row(rows, "Name");
        name.SuggestedContentTypePropertyAlias.Should().Be("title");
        name.Confidence.Should().Be(60, "the rule copies the bound side's confidence and runs before the runner-up pass");
        Row(rows, "Headline").Confidence.Should().Be(60);
    }

    [Fact]
    public async Task NameBound_HeadlineEmpty_YieldsHeadlineFromTheSameProperty()
    {
        var client = Binds(("title", [("Name", 0.85)]));

        var rows = await CreateMapper(client).MapAsync("blogPost", "BlogPosting", []);

        var headline = Row(rows, "Headline");
        headline.SuggestedContentTypePropertyAlias.Should().Be("title");
        headline.Confidence.Should().Be(85);
        Row(rows, "Name").Confidence.Should().Be(85);
    }

    [Fact]
    public async Task BothBoundFromDifferentProperties_TheRuleChangesNothing()
    {
        var client = Binds(
            ("title", [("Headline", 0.9)]),
            ("summary", [("Name", 0.7)]));

        var rows = await CreateMapper(client).MapAsync("blogPost", "BlogPosting", []);

        Row(rows, "Headline").SuggestedContentTypePropertyAlias.Should().Be("title");
        Row(rows, "Name").SuggestedContentTypePropertyAlias.Should().Be("summary", "the model's own binding is never overridden by the rule");
    }

    [Fact]
    public async Task NothingChanges_ForASchemaTypeWithoutHeadline()
    {
        // Product has Name but no Headline: the rule needs both to exist on the type.
        var productPage = ContentType("productPage", "Product Page", ("title", TextBox));
        _contentTypes.Get("productPage").Returns(productPage);
        var client = Binds(("title", [("Name", 0.9)]));

        var rows = await CreateMapper(client).MapAsync("productPage", "Product", []);

        rows.Select(r => r.SchemaPropertyName).Should().BeEquivalentTo(["Name"]);
        rows.Should().NotContain(r => string.Equals(r.SchemaPropertyName, "Headline", StringComparison.OrdinalIgnoreCase));
    }
}
