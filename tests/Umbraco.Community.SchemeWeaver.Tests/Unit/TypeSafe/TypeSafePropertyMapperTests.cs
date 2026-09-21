using System.Text.Json;
using System.Text.Json.Nodes;
using FluentAssertions;
using Microsoft.Extensions.Options;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Umbraco.Cms.Core.Models;
using Umbraco.Cms.Core.Services;
using Umbraco.Community.SchemeWeaver.Models.Api;
using Umbraco.Community.SchemeWeaver.Services;
using Umbraco.Community.SchemeWeaver.Tests.Unit.TypeSafe.TestSupport;
using Umbraco.Community.SchemeWeaver.TypeSafe.Client;
using Umbraco.Community.SchemeWeaver.TypeSafe.Configuration;
using Umbraco.Community.SchemeWeaver.TypeSafe.Services;
using Xunit;
using static Umbraco.Community.SchemeWeaver.Tests.Unit.TypeSafe.TestSupport.TypeSafeTestContentTypes;

namespace Umbraco.Community.SchemeWeaver.Tests.Unit.TypeSafe;

/// <summary>
/// <see cref="TypeSafePropertyMapper"/> driven by the gold oracle (<see cref="OracleTypeSafeClient"/>,
/// the C# twin of <c>eval/oracle.mjs</c>) over the REAL Schema.NET registry and type graph, with
/// NSubstitute content types. The oracle answers what a perfect model would, so every assertion
/// here is about the pipeline's mechanics: question assembly, claim collisions, the media rule,
/// descent, resolver-config shapes, the priors merge and the gating thresholds.
/// </summary>
public class TypeSafePropertyMapperTests
{
    private readonly IContentTypeService _contentTypes = Substitute.For<IContentTypeService>();
    private readonly ISchemeWeaverService _schemeWeaver = Substitute.For<ISchemeWeaverService>();
    private readonly IServiceProvider _provider = Substitute.For<IServiceProvider>();
    private readonly TypeSafeOptions _options = new() { ApiKey = "test-key" };
    private readonly SchemaAutoMapperOptions _autoMapperOptions = new();
    private readonly RecordingLogger<TypeSafePropertyMapper> _logger = new();

    public TypeSafePropertyMapperTests()
    {
        // Block element types are resolved lazily through the provider (a DI cycle otherwise).
        _provider.GetService(typeof(ISchemeWeaverService)).Returns(_schemeWeaver);
    }

    private TypeSafePropertyMapper CreateMapper(ITypeSafeClient client) => new(
        client,
        SharedSchemaRegistry.Graph,
        SharedSchemaRegistry.Registry,
        _contentTypes,
        _provider,
        Options.Create(_options),
        Options.Create(_autoMapperOptions),
        _logger);

    private void Register(IContentType contentType)
        => _contentTypes.Get(contentType.Alias).Returns(contentType);

    private void RegisterBlocks(string contentTypeAlias, string propertyAlias, params BlockElementTypeInfo[] blocks)
        => _schemeWeaver.GetBlockElementTypesAsync(contentTypeAlias, propertyAlias)
            .Returns(Task.FromResult<IEnumerable<BlockElementTypeInfo>>(blocks));

    private static void AssertJsonEquivalent(string? actual, string expected)
    {
        actual.Should().NotBeNullOrWhiteSpace();
        var actualNode = JsonNode.Parse(actual!);
        var expectedNode = JsonNode.Parse(expected);
        JsonNode.DeepEquals(actualNode, expectedNode).Should().BeTrue(
            $"resolver config should be {expected} but was {actual}");
    }

    private static PropertyMappingSuggestion Row(IReadOnlyList<PropertyMappingSuggestion> rows, string schemaProperty)
        => rows.Should().ContainSingle(r => string.Equals(r.SchemaPropertyName, schemaProperty, StringComparison.OrdinalIgnoreCase),
            $"exactly one row for {schemaProperty} is expected").Subject;

    // -----------------------------------------------------------------------
    // A recipe: the three rich shapes in one content type
    // -----------------------------------------------------------------------

    private OracleTypeSafeClient SetUpRecipe()
    {
        Register(ContentType("recipePage", "Recipe Page",
            ("ingredients", BlockList),
            ("instructions", BlockList),
            ("authorName", TextBox)));
        RegisterBlocks("recipePage", "ingredients",
            Block("ingredientBlock", "Ingredient", ("ingredient", TextBox)));
        RegisterBlocks("recipePage", "instructions",
            Block("stepBlock", "Step", ("stepName", TextBox), ("stepText", TextArea)));

        return new OracleTypeSafeClient(SharedSchemaRegistry.Graph,
            ExpectedMapping.BlockStringList("RecipeIngredient", "ingredients", "ingredient"),
            ExpectedMapping.BlockNested("RecipeInstructions", "instructions", "HowToStep", ("stepName", "Name"), ("stepText", "Text")),
            ExpectedMapping.Complex("Author", "Person", ("authorName", "Name")));
    }

    [Fact]
    public async Task Recipe_OneFieldBlock_BecomesStringList()
    {
        var oracle = SetUpRecipe();

        var rows = await CreateMapper(oracle).MapAsync("recipePage", "Recipe", []);

        var ingredients = Row(rows, "RecipeIngredient");
        ingredients.SuggestedSourceType.Should().Be("blockContent");
        ingredients.SuggestedContentTypePropertyAlias.Should().Be("ingredients");
        ingredients.SuggestedNestedSchemaTypeName.Should().BeNull("a string list has no nested type");
        ingredients.EditorAlias.Should().Be(BlockList);
        ingredients.Confidence.Should().Be(95);
        ingredients.IsAutoMapped.Should().BeTrue();
        AssertJsonEquivalent(ingredients.SuggestedResolverConfig, """{"extractAs":"stringList","contentProperty":"ingredient"}""");
    }

    [Fact]
    public async Task Recipe_MultiFieldBlock_BecomesNestedHowToStep()
    {
        var oracle = SetUpRecipe();

        var rows = await CreateMapper(oracle).MapAsync("recipePage", "Recipe", []);

        var instructions = Row(rows, "RecipeInstructions");
        instructions.SuggestedSourceType.Should().Be("blockContent");
        instructions.SuggestedContentTypePropertyAlias.Should().Be("instructions");
        instructions.SuggestedNestedSchemaTypeName.Should().Be("HowToStep",
            "the descent must cross the CreativeWork/ItemList multiple-inheritance seam");
        AssertJsonEquivalent(instructions.SuggestedResolverConfig, """
            {"nestedMappings":[
              {"schemaProperty":"Name","contentProperty":"stepName"},
              {"schemaProperty":"Text","contentProperty":"stepText"}
            ]}
            """);
    }

    [Fact]
    public async Task Recipe_SingleTextProperty_BecomesComplexTypePerson()
    {
        var oracle = SetUpRecipe();

        var rows = await CreateMapper(oracle).MapAsync("recipePage", "Recipe", []);

        var author = Row(rows, "Author");
        author.SuggestedSourceType.Should().Be("complexType");
        author.SuggestedContentTypePropertyAlias.Should().Be("authorName");
        author.SuggestedNestedSchemaTypeName.Should().Be("Person");
        author.IsComplexType.Should().BeTrue();
        AssertJsonEquivalent(author.SuggestedResolverConfig, """
            {"complexTypeMappings":[
              {"schemaProperty":"Name","sourceType":"property","contentTypePropertyAlias":"authorName"}
            ]}
            """);
    }

    [Fact]
    public async Task Recipe_EmitsOnlyTheThreeBoundRows()
    {
        var oracle = SetUpRecipe();

        var rows = await CreateMapper(oracle).MapAsync("recipePage", "Recipe", []);

        rows.Select(r => r.SchemaPropertyName).Should().BeEquivalentTo(["RecipeIngredient", "RecipeInstructions", "Author"]);
        rows.Should().OnlyContain(r => r.SchemaPropertyType != null && r.AcceptedTypes.Count > 0, "registry metadata is carried on every row");
    }

    // -----------------------------------------------------------------------
    // Claim collisions: several properties assembling ONE nested entity
    // -----------------------------------------------------------------------

    [Fact]
    public async Task Event_TwoPropertiesClaimingLocation_AssembleOnePlace()
    {
        Register(ContentType("eventPage", "Event Page",
            ("locationName", TextBox),
            ("locationAddress", TextBox)));
        var oracle = new OracleTypeSafeClient(SharedSchemaRegistry.Graph,
            ExpectedMapping.Complex("Location", "Place", ("locationName", "Name"), ("locationAddress", "Address")));

        var rows = await CreateMapper(oracle).MapAsync("eventPage", "Event", []);

        var location = Row(rows, "Location");
        location.SuggestedSourceType.Should().Be("complexType");
        location.SuggestedNestedSchemaTypeName.Should().Be("Place");
        AssertJsonEquivalent(location.SuggestedResolverConfig, """
            {"complexTypeMappings":[
              {"schemaProperty":"Name","sourceType":"property","contentTypePropertyAlias":"locationName"},
              {"schemaProperty":"Address","sourceType":"property","contentTypePropertyAlias":"locationAddress"}
            ]}
            """);
    }

    // -----------------------------------------------------------------------
    // The media rule is code, not judgment
    // -----------------------------------------------------------------------

    [Fact]
    public async Task MediaPicker_StaysPropertyEvenWhenTheModelSaysEntity()
    {
        Register(ContentType("blogPost", "Blog Post", ("heroImage", MediaPicker3)));
        // The oracle would answer "entity" (0.97) for Image if asked; it must never be asked.
        var oracle = new OracleTypeSafeClient(SharedSchemaRegistry.Graph,
            ExpectedMapping.Complex("Image", "ImageObject", ("heroImage", "ContentUrl")));

        var rows = await CreateMapper(oracle).MapAsync("blogPost", "BlogPosting", []);

        var image = Row(rows, "Image");
        image.SuggestedSourceType.Should().Be("property", "the media resolver already yields a full ImageObject");
        image.SuggestedContentTypePropertyAlias.Should().Be("heroImage");
        image.SuggestedNestedSchemaTypeName.Should().BeNull();
        image.SuggestedResolverConfig.Should().BeNull();
        oracle.Inner.AskedIds.Should().NotContain(id => id.StartsWith("entity__", StringComparison.Ordinal),
            "the entity question is skipped by rule for media");
    }

    // -----------------------------------------------------------------------
    // Priors merge
    // -----------------------------------------------------------------------

    private OracleTypeSafeClient SetUpFaq()
    {
        Register(ContentType("faqPage", "FAQ Page", ("title", TextBox), ("faqs", BlockList)));
        RegisterBlocks("faqPage", "faqs", Block("faqItem", "FAQ item", ("question", TextBox), ("answer", TextArea)));
        return new OracleTypeSafeClient(SharedSchemaRegistry.Graph,
            ExpectedMapping.Property("Headline", "title", 0.91),
            ExpectedMapping.BlockNested("MainEntity", "faqs", "Question", ("question", "Name"), ("answer", "AcceptedAnswer")));
    }

    // PriorsMode.None (the default) was chosen by measurement: on the eval harness the
    // override rule the package first shipped with halved rich coverage, and gap filling cost
    // precision. So by default the heuristic's rows never override, and never even join,
    // TypeSafe's; they are only the fallback when TypeSafe is unconfigured or fails.

    [Fact]
    public async Task Priors_Default_TypeSafeRowsWinEvenOverExactPriors()
    {
        var oracle = SetUpFaq();
        var priors = new List<PropertyMappingSuggestion>
        {
            new()
            {
                SchemaPropertyName = "MainEntity",
                SuggestedContentTypePropertyAlias = "faqs",
                SuggestedSourceType = "blockContent",
                SuggestedNestedSchemaTypeName = "Question",
                Confidence = 100,
                SuggestedResolverConfig = """{"prior":true}""",
            },
            new() { SchemaPropertyName = "Headline", SuggestedContentTypePropertyAlias = "title", SuggestedSourceType = "property", Confidence = 100 },
        };

        var rows = await CreateMapper(oracle).MapAsync("faqPage", "FAQPage", priors);

        var mainEntity = Row(rows, "MainEntity");
        mainEntity.Confidence.Should().Be(95, "the calibrated binding, not the heuristic's tier");
        mainEntity.SuggestedNestedSchemaTypeName.Should().Be("Question");
        mainEntity.SuggestedResolverConfig.Should().NotBe("""{"prior":true}""", "a prior never overrides a TypeSafe row");
        Row(rows, "Headline").Confidence.Should().Be(91, "even an exact-alias prior gives way to the calibrated binding");
    }

    [Fact]
    public async Task Priors_Default_PriorsForUnboundPropertiesAreNotAdded()
    {
        var oracle = SetUpFaq();
        var priors = new List<PropertyMappingSuggestion>
        {
            new() { SchemaPropertyName = "Publisher", SuggestedSourceType = "reference", SuggestedTargetPieceKey = "organization", Confidence = 90 },
            new() { SchemaPropertyName = "Name", SuggestedContentTypePropertyAlias = "name", SuggestedSourceType = "property", Confidence = 100 },
        };

        var rows = await CreateMapper(oracle).MapAsync("faqPage", "FAQPage", priors);

        rows.Should().NotContain(r => r.SchemaPropertyName == "Publisher", "PriorsMode.None emits TypeSafe's rows only");
        rows.Should().NotContain(r => r.SchemaPropertyName == "Name");
        rows.Select(r => r.SchemaPropertyName).Should().BeEquivalentTo(["MainEntity", "Headline"]);
    }

    [Fact]
    public async Task Priors_GapFill_AddsRuleShapedPriorsOnlyWhereTypeSafeHasNoRow()
    {
        _options.PriorsMode = TypeSafePriorsMode.GapFill;
        var oracle = SetUpFaq();
        var priors = new List<PropertyMappingSuggestion>
        {
            // Bound by TypeSafe: never overridden, even at 100.
            new() { SchemaPropertyName = "MainEntity", SuggestedContentTypePropertyAlias = "faqs", SuggestedSourceType = "blockContent", Confidence = 100, SuggestedResolverConfig = """{"prior":true}""" },
            // Unbound and rule-shaped (a cross-piece reference TypeSafe cannot express): added.
            new() { SchemaPropertyName = "Publisher", SuggestedSourceType = "reference", SuggestedTargetPieceKey = "organization", Confidence = 90 },
            // Unbound exact-alias match: added.
            new() { SchemaPropertyName = "Name", SuggestedContentTypePropertyAlias = "name", SuggestedSourceType = "property", Confidence = 100 },
            // Unbound synonym-tier name guess: not a rule, not added.
            new() { SchemaPropertyName = "Description", SuggestedContentTypePropertyAlias = "title", SuggestedSourceType = "property", Confidence = 80 },
            // Unbound and rule-shaped but below the show threshold: gated out like any other row.
            new() { SchemaPropertyName = "About", SuggestedSourceType = "reference", SuggestedTargetPieceKey = "organization", Confidence = 40 },
        };

        var rows = await CreateMapper(oracle).MapAsync("faqPage", "FAQPage", priors);

        Row(rows, "MainEntity").SuggestedResolverConfig.Should().NotBe("""{"prior":true}""");
        Row(rows, "Publisher").SuggestedTargetPieceKey.Should().Be("organization");
        Row(rows, "Publisher").IsAutoMapped.Should().BeTrue("gap-filled rows are gated by the same thresholds");
        Row(rows, "Name").Confidence.Should().Be(100);
        rows.Should().NotContain(r => r.SchemaPropertyName == "Description");
        rows.Should().NotContain(r => r.SchemaPropertyName == "About");
        rows.Take(2).Select(r => r.SchemaPropertyName).Should().Equal(
            new[] { "MainEntity", "Headline" },
            "TypeSafe's rows come first, by confidence, and the gap-filled priors follow");
    }

    // -----------------------------------------------------------------------
    // Gating
    // -----------------------------------------------------------------------

    private OracleTypeSafeClient SetUpBlogPost(double titleConfidence, double summaryConfidence)
    {
        Register(ContentType("blogPost", "Blog Post", ("title", TextBox), ("summary", TextArea)));
        return new OracleTypeSafeClient(SharedSchemaRegistry.Graph,
            ExpectedMapping.Property("Headline", "title", titleConfidence),
            ExpectedMapping.Property("Description", "summary", summaryConfidence));
    }

    [Fact]
    public async Task Gating_RowBelowShowThreshold_IsDropped()
    {
        var oracle = SetUpBlogPost(titleConfidence: 0.5, summaryConfidence: 0.9);

        var rows = await CreateMapper(oracle).MapAsync("blogPost", "BlogPosting", []);

        rows.Should().NotContain(r => r.SchemaPropertyName == "Headline", "50 is below the default show threshold of 60");
        Row(rows, "Description").Confidence.Should().Be(90);
    }

    [Fact]
    public async Task Gating_IsAutoMappedFollowsAutoApplyThreshold()
    {
        var oracle = SetUpBlogPost(titleConfidence: 0.7, summaryConfidence: 0.9);

        var rows = await CreateMapper(oracle).MapAsync("blogPost", "BlogPosting", []);

        Row(rows, "Headline").IsAutoMapped.Should().BeFalse("70 is shown but not pre-ticked");
        Row(rows, "Description").IsAutoMapped.Should().BeTrue("90 clears the default auto-apply bar of 80");
    }

    [Fact]
    public async Task Gating_CustomCoreThresholds_AreHonoured()
    {
        _autoMapperOptions.AutoApplyConfidenceThreshold = 95;
        _autoMapperOptions.ShowConfidenceThreshold = 50;
        var oracle = SetUpBlogPost(titleConfidence: 0.55, summaryConfidence: 0.96);

        var rows = await CreateMapper(oracle).MapAsync("blogPost", "BlogPosting", []);

        Row(rows, "Headline").IsAutoMapped.Should().BeFalse();
        Row(rows, "Description").IsAutoMapped.Should().BeTrue();
    }

    [Fact]
    public async Task Gating_MinBindingConfidence_DropsRowsAtBindTime()
    {
        _options.MinBindingConfidence = 75;
        var oracle = SetUpBlogPost(titleConfidence: 0.7, summaryConfidence: 0.9);

        var rows = await CreateMapper(oracle).MapAsync("blogPost", "BlogPosting", []);

        rows.Should().NotContain(r => r.SchemaPropertyName == "Headline");
        Row(rows, "Description").Confidence.Should().Be(90);
    }

    // -----------------------------------------------------------------------
    // Never throws, never invents
    // -----------------------------------------------------------------------

    public static TheoryData<string> DegenerateAnswerModes => new()
    {
        "missing", "malformed-choice", "unoffered-option", "nan-confidence", "wrong-type", "none",
    };

    [Theory]
    [MemberData(nameof(DegenerateAnswerModes))]
    public async Task DegenerateAnswers_NeverThrowAndProduceNoRow(string mode)
    {
        Register(ContentType("blogPost", "Blog Post", ("title", TextBox), ("faqs", BlockList)));
        RegisterBlocks("blogPost", "faqs", Block("faqItem", "FAQ item", ("question", TextBox), ("answer", TextArea)));
        var client = new FakeTypeSafeClient((_, q) => mode switch
        {
            "missing" => null,
            "malformed-choice" => new SystemOneAnswer { Type = "choice", Choice = null, Confidence = 0.9 },
            "unoffered-option" => new SystemOneAnswer { Type = "choice", Choice = "inventedAlias", Confidence = 0.9 },
            "nan-confidence" => q.Type == "noul"
                ? new SystemOneAnswer { Type = "noul", Noul = double.NaN }
                : new SystemOneAnswer { Type = "choice", Choice = FakeTypeSafeClient.CriteriaOf(q).Keys.First(), Confidence = double.NaN },
            "wrong-type" => new SystemOneAnswer { Type = "noul", Noul = 0.9 },
            _ => q.Type == "noul" ? FakeTypeSafeClient.Noul(0) : FakeTypeSafeClient.Choice("__none", 0.1),
        });

        var act = () => CreateMapper(client).MapAsync("blogPost", "BlogPosting", []);

        var rows = await act.Should().NotThrowAsync();
        rows.Subject.Should().BeEmpty($"mode '{mode}' must degrade to an empty mapping");
    }

    [Fact]
    public async Task UnknownContentType_ReturnsEmptyWithoutAskingTheModel()
    {
        _contentTypes.Get("missing").Returns((IContentType?)null);
        var client = new FakeTypeSafeClient((_, _) => FakeTypeSafeClient.Choice("__none"));

        var rows = await CreateMapper(client).MapAsync("missing", "BlogPosting", []);

        rows.Should().BeEmpty();
        client.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task EveryEmittedAlias_ExistsOnTheContentTypeOrItsBlocks()
    {
        var oracle = SetUpRecipe();
        var known = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "ingredients", "instructions", "authorName", "ingredient", "stepName", "stepText",
            "__url", "__name", "__createDate", "__updateDate",
        };

        var rows = await CreateMapper(oracle).MapAsync("recipePage", "Recipe", []);

        rows.Should().NotBeEmpty();
        foreach (var row in rows)
        {
            known.Should().Contain(row.SuggestedContentTypePropertyAlias!);
            if (row.SuggestedResolverConfig is null)
                continue;

            var config = JsonNode.Parse(row.SuggestedResolverConfig)!.AsObject();
            if (config["contentProperty"] is { } cp)
                known.Should().Contain(cp.GetValue<string>());
            foreach (var inner in config["nestedMappings"]?.AsArray() ?? [])
                known.Should().Contain(inner!["contentProperty"]!.GetValue<string>());
            foreach (var inner in config["complexTypeMappings"]?.AsArray() ?? [])
                known.Should().Contain(inner!["contentTypePropertyAlias"]!.GetValue<string>());
        }
    }

    [Fact]
    public async Task BindQuestions_OfferOnlyRealSchemaPropertiesPlusNone()
    {
        var oracle = SetUpRecipe();
        var registryNames = SharedSchemaRegistry.Registry.GetProperties("Recipe").Select(p => p.Name).ToHashSet(StringComparer.Ordinal);

        await CreateMapper(oracle).MapAsync("recipePage", "Recipe", []);

        var binds = oracle.Inner.AskedQuestions.Where(kv => kv.Key.StartsWith("bind__", StringComparison.Ordinal)).ToList();
        binds.Should().HaveCount(7, "three content properties plus the four built-ins");
        foreach (var (_, question) in binds)
        {
            var options = FakeTypeSafeClient.CriteriaOf(question).Keys.ToList();
            options.Should().Contain("__none");
            options.Where(o => o != "__none").Should().OnlyContain(o => registryNames.Contains(o));
        }
    }

    // -----------------------------------------------------------------------
    // Beam search: the greedy instability the eval measured
    // -----------------------------------------------------------------------

    [Theory]
    [InlineData(3, "Restaurant")]
    [InlineData(1, "Corporation")]
    public async Task Descent_BeamRecoversFromGreedyDeadEnd(int beamWidth, string expectedNestedType)
    {
        // Level 1 from Organization: the top child (Corporation, 0.6) is a dead end; the runner-up
        // (LocalBusiness, 0.4) leads to FoodEstablishment (0.95) then Restaurant (0.99), whose
        // geometric-mean path score beats 0.6. Greedy (width 1) commits to Corporation and can
        // never find out.
        _options.BeamWidth = beamWidth;
        Register(ContentType("blogPost", "Blog Post", ("authorName", TextBox)));
        var client = new FakeTypeSafeClient((id, q) =>
        {
            if (id.StartsWith("bind__authorName", StringComparison.Ordinal))
                return FakeTypeSafeClient.Choice(FakeTypeSafeClient.OptionNamed(q, "Author")!, 0.9);
            if (id.StartsWith("bind__", StringComparison.Ordinal))
                return FakeTypeSafeClient.Choice("__none", 0.9);
            if (id.StartsWith("entity__", StringComparison.Ordinal))
                return FakeTypeSafeClient.Noul(0.97);
            if (id.StartsWith("root__", StringComparison.Ordinal))
                return FakeTypeSafeClient.Choice("Organization", 0.9);
            if (id.StartsWith("desc__", StringComparison.Ordinal))
            {
                return FakeTypeSafeClient.CurrentTypeOf(q) switch
                {
                    "Organization" => FakeTypeSafeClient.Distribution(new Dictionary<string, double> { ["Corporation"] = 0.6, ["LocalBusiness"] = 0.4 }),
                    "LocalBusiness" => FakeTypeSafeClient.Distribution(new Dictionary<string, double> { ["FoodEstablishment"] = 0.95 }),
                    "FoodEstablishment" => FakeTypeSafeClient.Distribution(new Dictionary<string, double> { ["Restaurant"] = 0.99 }),
                    "Restaurant" => FakeTypeSafeClient.Distribution(new Dictionary<string, double> { ["__stop"] = 1.0 }),
                    _ => FakeTypeSafeClient.Distribution(new Dictionary<string, double> { ["__stop"] = 0.01 }),
                };
            }
            if (id.StartsWith("inner__", StringComparison.Ordinal))
                return FakeTypeSafeClient.Choice(FakeTypeSafeClient.OptionNamed(q, "Name")!, 0.9);
            return null;
        });

        var rows = await CreateMapper(client).MapAsync("blogPost", "BlogPosting", []);

        var author = Row(rows, "Author");
        author.SuggestedSourceType.Should().Be("complexType");
        author.SuggestedNestedSchemaTypeName.Should().Be(expectedNestedType);
    }

    // -----------------------------------------------------------------------
    // The state the model sees, and where it comes from
    // -----------------------------------------------------------------------

    [Fact]
    public async Task State_DescribesBlockListsByElementTypesAndFields()
    {
        var oracle = SetUpRecipe();

        await CreateMapper(oracle).MapAsync("recipePage", "Recipe", []);

        oracle.Inner.Requests.Should().NotBeEmpty();
        oracle.Inner.Requests.Select(r => r.State).Distinct().Should().ContainSingle("every request in a mapper call shares one state");

        using var state = JsonDocument.Parse(JsonSerializer.Serialize(oracle.Inner.Requests[0].State));
        var root = state.RootElement;
        root.GetProperty("targetSchemaType").GetString().Should().Be("Recipe");
        var contentType = root.GetProperty("umbracoContentType");
        contentType.GetProperty("alias").GetString().Should().Be("recipePage");
        contentType.GetProperty("name").GetString().Should().Be("Recipe Page");

        var properties = contentType.GetProperty("properties").EnumerateArray().ToList();
        properties.Select(p => p.GetProperty("alias").GetString())
            .Should().Equal("ingredients", "instructions", "authorName", "__url", "__name", "__createDate", "__updateDate");

        var instructions = properties.Single(p => p.GetProperty("alias").GetString() == "instructions");
        instructions.GetProperty("editor").GetString().Should().Be(BlockList);
        instructions.GetProperty("isRepeatingList").GetBoolean().Should().BeTrue();
        var block = instructions.GetProperty("blockTypes").EnumerateArray().Single();
        block.GetProperty("blockType").GetString().Should().Be("stepBlock");
        block.GetProperty("blockName").GetString().Should().Be("Step");
        block.GetProperty("fields").EnumerateArray().Select(f => f.GetProperty("alias").GetString()).Should().Equal("stepName", "stepText");
        block.GetProperty("fields").EnumerateArray().Select(f => f.GetProperty("editor").GetString()).Should().Equal(TextBox, TextArea);

        var authorName = properties.Single(p => p.GetProperty("alias").GetString() == "authorName");
        authorName.TryGetProperty("blockTypes", out _).Should().BeFalse("a scalar property carries no block description");
    }

    [Fact]
    public async Task BlockElementTypes_ComeFromSchemeWeaverServiceResolvedLazily()
    {
        var oracle = SetUpRecipe();

        _provider.DidNotReceive().GetService(typeof(ISchemeWeaverService));

        await CreateMapper(oracle).MapAsync("recipePage", "Recipe", []);

        _provider.Received(1).GetService(typeof(ISchemeWeaverService));
        await _schemeWeaver.Received(1).GetBlockElementTypesAsync("recipePage", "ingredients");
        await _schemeWeaver.Received(1).GetBlockElementTypesAsync("recipePage", "instructions");
    }

    [Fact]
    public async Task ContentTypeWithoutBlocks_NeverResolvesSchemeWeaverService()
    {
        var oracle = SetUpBlogPost(0.9, 0.9);

        await CreateMapper(oracle).MapAsync("blogPost", "BlogPosting", []);

        _provider.DidNotReceive().GetService(typeof(ISchemeWeaverService));
        await _schemeWeaver.DidNotReceiveWithAnyArgs().GetBlockElementTypesAsync(default!, default!);
    }

    [Fact]
    public async Task BlockIntrospectionFailure_DegradesToNameOnlyWithoutFailingTheMapping()
    {
        Register(ContentType("faqPage", "FAQ Page", ("title", TextBox), ("faqs", BlockList)));
        _schemeWeaver.GetBlockElementTypesAsync("faqPage", "faqs").ThrowsAsync(new InvalidOperationException("boom"));
        var oracle = new OracleTypeSafeClient(SharedSchemaRegistry.Graph, ExpectedMapping.Property("Headline", "title", 0.9));

        var rows = await CreateMapper(oracle).MapAsync("faqPage", "FAQPage", []);

        Row(rows, "Headline").Confidence.Should().Be(90);
        oracle.Inner.AskedIds.Should().Contain("bind__faqs", "the block is still offered, described by name alone");
    }

    [Fact]
    public async Task ApiFailure_PropagatesToTheCaller()
    {
        // The mapper throws; the decorator (TypeSafeSchemaAutoMapper) owns the fallback.
        Register(ContentType("blogPost", "Blog Post", ("title", TextBox)));
        var client = new FakeTypeSafeClient((_, _) => null) { Throws = new TypeSafeApiException("HTTP 529", 529) };

        var act = () => CreateMapper(client).MapAsync("blogPost", "BlogPosting", []);

        await act.Should().ThrowAsync<TypeSafeApiException>();
    }
}
