using System.Text.Json;
using System.Text.Json.Nodes;
using FluentAssertions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Umbraco.Cms.Core.Models;
using Umbraco.Cms.Core.Services;
using Umbraco.Community.SchemeWeaver.Models.Api;
using Umbraco.Community.SchemeWeaver.Services;
using Umbraco.Community.SchemeWeaver.Services.Resolvers;
using Umbraco.Community.SchemeWeaver.Services.ValueSchemas;
using Umbraco.Community.SchemeWeaver.Tests.Unit.TypeSafe.TestSupport;
using Umbraco.Community.SchemeWeaver.TypeSafe.Client;
using Umbraco.Community.SchemeWeaver.TypeSafe.Configuration;
using Umbraco.Community.SchemeWeaver.TypeSafe.Services;
using Umbraco.Community.SchemeWeaver.TypeSafe.Services.Judgments;
using Xunit;
using static Umbraco.Community.SchemeWeaver.Tests.Unit.TypeSafe.TestSupport.TypeSafeTestContentTypes;

namespace Umbraco.Community.SchemeWeaver.Tests.Unit.TypeSafe;

/// <summary>
/// The v2 per-element-type block planning (<see cref="BlockRoutePlanner"/> and
/// <see cref="RouteConfigWriter"/>) driven by the gold oracle: blocks inside blocks become the
/// core's recursive <c>routes</c> config, element types that land on different Schema.org types
/// get a route each, an element type that does not belong is left out, and a list the v1 shape
/// can still express keeps that shape byte for byte. The strongest contract here is with the
/// core's <c>BlockContentResolver</c>: every emitted config must deserialise through its own
/// <see cref="ResolverConfigModel"/>.
/// </summary>
public class TypeSafeBlockRoutesTests
{
    private static readonly JsonSerializerOptions CoreParse = new() { PropertyNameCaseInsensitive = true };

    private readonly IContentTypeService _contentTypes = Substitute.For<IContentTypeService>();
    private readonly ISchemeWeaverService _schemeWeaver = Substitute.For<ISchemeWeaverService>();
    private readonly IPropertyValueSchemaService _valueSchemas = Substitute.For<IPropertyValueSchemaService>();
    private readonly IServiceProvider _provider = Substitute.For<IServiceProvider>();
    private readonly TypeSafeOptions _options = new() { ApiKey = "test-key" };
    private readonly SchemaAutoMapperOptions _autoMapperOptions = new();
    private readonly RecordingLogger<TypeSafePropertyMapper> _logger = new();

    public TypeSafeBlockRoutesTests()
    {
        _provider.GetService(typeof(ISchemeWeaverService)).Returns(_schemeWeaver);
        _valueSchemas.GetDataTypeValueSchemaAsync(Arg.Any<Guid>()).Returns(Task.FromResult<string?>(null));
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

    private void Register(IContentType contentType)
        => _contentTypes.Get(contentType.Alias).Returns(contentType);

    private void RegisterBlocks(string contentTypeAlias, string propertyAlias, params BlockElementTypeInfo[] blocks)
        => _schemeWeaver.GetBlockElementTypesAsync(contentTypeAlias, propertyAlias)
            .Returns(Task.FromResult<IEnumerable<BlockElementTypeInfo>>(blocks));

    private static PropertyMappingSuggestion Row(IReadOnlyList<PropertyMappingSuggestion> rows, string schemaProperty)
        => rows.Should().ContainSingle(r => string.Equals(r.SchemaPropertyName, schemaProperty, StringComparison.OrdinalIgnoreCase),
            $"exactly one row for {schemaProperty} is expected").Subject;

    private static ResolverConfigModel ParseAsTheCoreDoes(string? config)
    {
        config.Should().NotBeNullOrWhiteSpace();
        return JsonSerializer.Deserialize<ResolverConfigModel>(config!, CoreParse)!;
    }

    private static void AssertJsonEquivalent(string? actual, string expected)
    {
        actual.Should().NotBeNullOrWhiteSpace();
        JsonNode.DeepEquals(JsonNode.Parse(actual!), JsonNode.Parse(expected)).Should().BeTrue(
            $"resolver config should be {expected} but was {actual}");
    }

    private static IReadOnlyList<string> OptionKeys(SystemOneQuestion question)
        => FakeTypeSafeClient.CriteriaOf(question).Keys.ToList();

    // -----------------------------------------------------------------------
    // Blocks inside blocks: a FAQ section holding a list of FAQ items
    // -----------------------------------------------------------------------

    /// <summary>
    /// <c>nestedBlocksPage.sections</c> holds <c>faqSection</c> blocks (a heading and a nested
    /// Block List of <c>faqItem</c> blocks, each a question and an answer): the shape v1 could
    /// only describe as a dead <c>wrapInType "Thing"</c> inner mapping.
    /// </summary>
    private OracleTypeSafeClient SetUpNestedFaq()
    {
        Register(ContentType("nestedBlocksPage", "Nested Blocks Page", ("title", TextBox), ("sections", BlockList)));
        RegisterBlocks("nestedBlocksPage", "sections",
            BlockOf("faqSection", "FAQ Section",
                Field("heading", TextBox),
                NestedField("questions", BlockList,
                    Block("faqItem", "FAQ Item", ("question", TextBox), ("answer", TextArea)))));

        return new OracleTypeSafeClient(SharedSchemaRegistry.Graph,
            ExpectedMapping.Property("Headline", "title", 0.9),
            ExpectedMapping.BlockRoutes("HasPart", "sections",
                ExpectedRoute.Of("faqSection", "WebPageElement", ("heading", "Name"), ("questions", "HasPart"))
                    .WithNested("questions",
                        ExpectedRoute.Of("faqItem", "Question", ("question", "Name"), ("answer", "AcceptedAnswer")))));
    }

    [Fact]
    public async Task NestedBlocks_EmitRoutesWithNestedRoutes_ThatTheCoreParses()
    {
        var oracle = SetUpNestedFaq();

        var rows = await CreateMapper(oracle).MapAsync("nestedBlocksPage", "WebPage", []);

        var hasPart = Row(rows, "HasPart");
        hasPart.SuggestedSourceType.Should().Be("blockContent");
        hasPart.SuggestedContentTypePropertyAlias.Should().Be("sections");
        hasPart.SuggestedNestedSchemaTypeName.Should().BeNull("with routes the nested type lives on each route, and the core ignores the mapping-level one");
        hasPart.IsAutoMapped.Should().BeTrue();

        var config = ParseAsTheCoreDoes(hasPart.SuggestedResolverConfig);
        config.NestedMappings.Should().BeNull("routes take precedence and the v1 list is not emitted alongside them");
        config.Routes.Should().ContainSingle();

        var section = config.Routes![0];
        section.BlockAlias.Should().Be("faqSection");
        section.NestedSchemaType.Should().Be("WebPageElement");
        section.PropertyMappings.Should().HaveCount(2);
        section.PropertyMappings![0].SchemaProperty.Should().Be("Name");
        section.PropertyMappings[0].ContentProperty.Should().Be("heading");
        section.PropertyMappings[0].WrapInType.Should().BeNull("Name is a plain text property");

        var questions = section.PropertyMappings[1];
        questions.SchemaProperty.Should().Be("HasPart");
        questions.ContentProperty.Should().Be("questions");
        questions.WrapInType.Should().BeNull("a block-editor field is never wrapped: that was v1's dead inner mapping");
        questions.ExtractAs.Should().BeNull();
        questions.Routes.Should().ContainSingle();

        var item = config.Routes[0].PropertyMappings![1].Routes![0];
        item.NestedSchemaType.Should().Be("Question", "the nested route's type descends from WebPageElement.hasPart's own range");
        item.BlockAlias.Should().Be("faqItem");
        item.PropertyMappings.Should().HaveCount(2);
        item.PropertyMappings![0].SchemaProperty.Should().Be("Name");
        item.PropertyMappings[0].ContentProperty.Should().Be("question");
        item.PropertyMappings[1].SchemaProperty.Should().Be("AcceptedAnswer");
        item.PropertyMappings[1].ContentProperty.Should().Be("answer");
        item.PropertyMappings[1].WrapInType.Should().Be("Answer", "a scalar onto an entity-ranged property is wrapped in the range's first type");
    }

    [Fact]
    public async Task NestedBlocks_RoutesJson_UsesTheCoreKeysExactly()
    {
        var oracle = SetUpNestedFaq();

        var rows = await CreateMapper(oracle).MapAsync("nestedBlocksPage", "WebPage", []);

        AssertJsonEquivalent(Row(rows, "HasPart").SuggestedResolverConfig, """
            {"routes":[{"blockAlias":"faqSection","nestedSchemaType":"WebPageElement","propertyMappings":[
              {"schemaProperty":"Name","contentProperty":"heading"},
              {"schemaProperty":"HasPart","contentProperty":"questions","routes":[
                {"blockAlias":"faqItem","nestedSchemaType":"Question","propertyMappings":[
                  {"schemaProperty":"Name","contentProperty":"question"},
                  {"schemaProperty":"AcceptedAnswer","contentProperty":"answer","wrapInType":"Answer"}
                ]}
              ]}
            ]}]}
            """);
    }

    // -----------------------------------------------------------------------
    // What does not need routes keeps the v1 shape, byte for byte
    // -----------------------------------------------------------------------

    private OracleTypeSafeClient SetUpRecipe()
    {
        Register(ContentType("recipePage", "Recipe Page", ("instructions", BlockList)));
        RegisterBlocks("recipePage", "instructions",
            Block("stepBlock", "Step", ("stepName", TextBox), ("stepText", TextArea)));
        return new OracleTypeSafeClient(SharedSchemaRegistry.Graph,
            ExpectedMapping.BlockNested("RecipeInstructions", "instructions", "HowToStep", ("stepName", "Name"), ("stepText", "Text")));
    }

    [Fact]
    public async Task SingleElementTypeWithoutNesting_KeepsTheV1NestedMappingsByteForByte()
    {
        const string v1 = """{"nestedMappings":[{"schemaProperty":"Name","contentProperty":"stepName"},{"schemaProperty":"Text","contentProperty":"stepText"}]}""";

        var auto = Row(await CreateMapper(SetUpRecipe()).MapAsync("recipePage", "Recipe", []), "RecipeInstructions");
        auto.SuggestedNestedSchemaTypeName.Should().Be("HowToStep");
        auto.SuggestedResolverConfig.Should().Be(v1, "a single-element list under Auto is the v1 config, key order and case included");

        _options.RoutesMode = TypeSafeRoutesMode.Off;
        var off = Row(await CreateMapper(SetUpRecipe()).MapAsync("recipePage", "Recipe", []), "RecipeInstructions");
        off.SuggestedResolverConfig.Should().Be(auto.SuggestedResolverConfig, "Off is the v1 path itself, so the two must agree byte for byte");
        off.SuggestedNestedSchemaTypeName.Should().Be("HowToStep");
    }

    // -----------------------------------------------------------------------
    // Mixed lists: different types, converging types, a type that does not belong
    // -----------------------------------------------------------------------

    private void RegisterMixedBody(params BlockElementTypeInfo[] blocks)
    {
        Register(ContentType("bodyPage", "Body Page", ("body", BlockList)));
        RegisterBlocks("bodyPage", "body", blocks);
    }

    [Fact]
    public async Task ElementTypes_DescendingToDifferentTypes_EmitOneRouteEach()
    {
        RegisterMixedBody(
            Block("faqBlock", "FAQ", ("question", TextBox), ("answer", TextArea)),
            Block("teamMember", "Team Member", ("personName", TextBox), ("role", TextBox)));
        var oracle = new OracleTypeSafeClient(SharedSchemaRegistry.Graph,
            ExpectedMapping.BlockRoutes("About", "body",
                ExpectedRoute.Of("faqBlock", "Question", ("question", "Name"), ("answer", "AcceptedAnswer")),
                ExpectedRoute.Of("teamMember", "Person", ("personName", "Name"), ("role", "JobTitle"))));

        var rows = await CreateMapper(oracle).MapAsync("bodyPage", "WebPage", []);

        var about = Row(rows, "About");
        about.SuggestedNestedSchemaTypeName.Should().BeNull();
        var config = ParseAsTheCoreDoes(about.SuggestedResolverConfig);
        config.NestedMappings.Should().BeNull();
        config.Routes.Should().HaveCount(2);
        config.Routes!.Select(r => (r.BlockAlias, r.NestedSchemaType)).Should().Equal(("faqBlock", "Question"), ("teamMember", "Person"));
        config.Routes[0].PropertyMappings!.Select(m => (m.SchemaProperty, m.ContentProperty)).Should().Equal(("Name", "question"), ("AcceptedAnswer", "answer"));
        config.Routes[1].PropertyMappings!.Select(m => (m.SchemaProperty, m.ContentProperty)).Should().Equal(("Name", "personName"), ("JobTitle", "role"));
    }

    [Fact]
    public async Task ConvergingElementTypes_KeepTheV1Shape_WithFieldsMergedFirstElementWins()
    {
        // Both element types become a Question and bind their shared fields the same way; the
        // second declares its fields in a different order and adds one. The v1 shape can express
        // that, so it is kept, merged in the first element's order with the extra field last.
        RegisterMixedBody(
            Block("faqBlock", "FAQ", ("question", TextBox), ("answer", TextArea)),
            Block("faqAltBlock", "FAQ (alt)", ("answer", TextArea), ("question", TextBox), ("extra", TextArea)));
        var oracle = new OracleTypeSafeClient(SharedSchemaRegistry.Graph,
            ExpectedMapping.BlockRoutes("About", "body",
                ExpectedRoute.Of("faqBlock", "Question", ("question", "Name"), ("answer", "AcceptedAnswer")),
                ExpectedRoute.Of("faqAltBlock", "Question", ("question", "Name"), ("answer", "AcceptedAnswer"), ("extra", "Text"))));

        var rows = await CreateMapper(oracle).MapAsync("bodyPage", "WebPage", []);

        var about = Row(rows, "About");
        about.SuggestedNestedSchemaTypeName.Should().Be("Question");
        AssertJsonEquivalent(about.SuggestedResolverConfig, """
            {"nestedMappings":[
              {"schemaProperty":"Name","contentProperty":"question"},
              {"schemaProperty":"AcceptedAnswer","contentProperty":"answer","wrapInType":"Answer"},
              {"schemaProperty":"Text","contentProperty":"extra"}
            ]}
            """);
    }

    [Fact]
    public async Task ElementTypeAnsweringNone_IsOmitted_AndForcesRoutes()
    {
        RegisterMixedBody(
            Block("faqBlock", "FAQ", ("question", TextBox), ("answer", TextArea)),
            Block("ctaBlock", "Call to action", ("ctaText", TextBox), ("ctaLink", TextBox)));
        var oracle = new OracleTypeSafeClient(SharedSchemaRegistry.Graph,
            ExpectedMapping.BlockRoutes("About", "body",
                ExpectedRoute.Of("faqBlock", "Question", ("question", "Name"), ("answer", "AcceptedAnswer")),
                ExpectedRoute.Skip("ctaBlock")));

        var rows = await CreateMapper(oracle).MapAsync("bodyPage", "WebPage", []);

        var about = Row(rows, "About");
        about.SuggestedNestedSchemaTypeName.Should().BeNull();
        var config = ParseAsTheCoreDoes(about.SuggestedResolverConfig);
        config.Routes.Should().ContainSingle().Which.BlockAlias.Should().Be("faqBlock",
            "a skipped element type alongside a kept one needs routes: the v1 shape would apply Question to the call to action too");
        about.SuggestedResolverConfig.Should().NotContain("ctaBlock").And.NotContain("ctaText").And.NotContain("ctaLink");

        var rootQuestion = oracle.Inner.AskedQuestions["root__About__ctaBlock"];
        OptionKeys(rootQuestion)[0].Should().Be(JudgmentSession.None, "leaving a block type out is the default, so it is listed first");
        oracle.Inner.AskedIds.Should().NotContain(id => id.StartsWith("inner__About__ctaBlock", StringComparison.Ordinal),
            "a skipped element type's fields are never bound");
    }

    // -----------------------------------------------------------------------
    // Nested fields: string lists by rule, and never a wrapped block editor
    // -----------------------------------------------------------------------

    [Fact]
    public async Task NestedSingleFieldBlock_BecomesAStringList_WithoutAsking()
    {
        Register(ContentType("nestedBlocksPage", "Nested Blocks Page", ("sections", BlockList)));
        RegisterBlocks("nestedBlocksPage", "sections",
            BlockOf("taggedSection", "Tagged Section",
                Field("heading", TextBox),
                NestedField("tags", BlockList, Block("tagItem", "Tag", ("tag", TextBox)))));
        var oracle = new OracleTypeSafeClient(SharedSchemaRegistry.Graph,
            ExpectedMapping.BlockRoutes("HasPart", "sections",
                ExpectedRoute.Of("taggedSection", "WebPageElement", ("heading", "Name"), ("tags", "Keywords"))));

        var rows = await CreateMapper(oracle).MapAsync("nestedBlocksPage", "WebPage", []);

        var config = ParseAsTheCoreDoes(Row(rows, "HasPart").SuggestedResolverConfig);
        var tags = config.Routes.Should().ContainSingle().Which.PropertyMappings!.Single(m => m.ContentProperty == "tags");
        tags.SchemaProperty.Should().Be("Keywords");
        tags.ExtractAs.Should().Be("stringList");
        tags.NestedContentProperty.Should().Be("tag");
        tags.Routes.Should().BeNull();
        tags.WrapInType.Should().BeNull();
        oracle.Inner.AskedIds.Should().NotContain("shape__HasPart__taggedSection__tags", "one-field blocks can only be a list of strings: a rule, not a judgment")
            .And.NotContain("strlist__HasPart__taggedSection__tags");
    }

    [Fact]
    public async Task BlockEditorField_WithoutANestedPlan_IsDroppedAndNeverWrapped()
    {
        // The nested list's element types could not be discovered (empty), so there is nothing
        // to plan: the field is left out of the config rather than emitted as a scalar with a
        // wrapInType the core would warn on and drop.
        Register(ContentType("nestedBlocksPage", "Nested Blocks Page", ("sections", BlockList)));
        RegisterBlocks("nestedBlocksPage", "sections",
            BlockOf("faqSection", "FAQ Section",
                Field("heading", TextBox),
                NestedField("items", BlockList)));
        var oracle = new OracleTypeSafeClient(SharedSchemaRegistry.Graph,
            ExpectedMapping.BlockRoutes("HasPart", "sections",
                ExpectedRoute.Of("faqSection", "WebPageElement", ("heading", "Name"), ("items", "HasPart"))));

        var rows = await CreateMapper(oracle).MapAsync("nestedBlocksPage", "WebPage", []);

        var hasPart = Row(rows, "HasPart");
        hasPart.SuggestedResolverConfig.Should().NotContain("items").And.NotContain("wrapInType");
        AssertJsonEquivalent(hasPart.SuggestedResolverConfig, """{"nestedMappings":[{"schemaProperty":"Name","contentProperty":"heading"}]}""");
        hasPart.SuggestedNestedSchemaTypeName.Should().Be("WebPageElement", "with nothing nested left, the v1 shape still fits");
        oracle.Inner.AskedIds.Should().NotContain("inner__HasPart__faqSection__items", "a block field that cannot be planned is not offered as a scalar");
    }

    [Fact]
    public async Task DepthCap_StopsRecursion()
    {
        Register(ContentType("deepPage", "Deep Page", ("sections", BlockList)));
        RegisterBlocks("deepPage", "sections",
            BlockOf("section", "Section",
                Field("heading", TextBox),
                NestedField("groups", BlockList,
                    BlockOf("group", "Group",
                        Field("groupName", TextBox),
                        NestedField("questions", BlockList,
                            Block("faqItem", "FAQ Item", ("question", TextBox), ("answer", TextArea)))))));
        OracleTypeSafeClient Oracle() => new(SharedSchemaRegistry.Graph,
            ExpectedMapping.BlockRoutes("HasPart", "sections",
                ExpectedRoute.Of("section", "WebPageElement", ("heading", "Name"), ("groups", "HasPart"))
                    .WithNested("groups",
                        ExpectedRoute.Of("group", "WebPageElement", ("groupName", "Name"), ("questions", "HasPart"))
                            .WithNested("questions",
                                ExpectedRoute.Of("faqItem", "Question", ("question", "Name"), ("answer", "AcceptedAnswer"))))));

        _options.MaxBlockRouteDepth = 1;
        var capped = Oracle();
        var cappedConfig = Row(await CreateMapper(capped).MapAsync("deepPage", "WebPage", []), "HasPart").SuggestedResolverConfig;

        cappedConfig.Should().Contain("\"group\"").And.NotContain("faqItem").And.NotContain("questions");
        var group = ParseAsTheCoreDoes(cappedConfig).Routes![0].PropertyMappings!.Single(m => m.ContentProperty == "groups").Routes!.Single();
        group.PropertyMappings!.Select(m => m.ContentProperty).Should().BeEquivalentTo(new[] { "groupName" }, "the level-2 list is beyond the cap and dropped, never wrapped");
        capped.Inner.AskedIds.Should().NotContain(id => id.Contains("__questions", StringComparison.Ordinal));

        _options.MaxBlockRouteDepth = 2;
        var full = Oracle();
        var fullConfig = Row(await CreateMapper(full).MapAsync("deepPage", "WebPage", []), "HasPart").SuggestedResolverConfig;

        fullConfig.Should().Contain("faqItem");
        ParseAsTheCoreDoes(fullConfig).Routes![0].PropertyMappings![1].Routes![0].PropertyMappings![1].Routes![0].NestedSchemaType.Should().Be("Question");
    }

    // -----------------------------------------------------------------------
    // RoutesMode
    // -----------------------------------------------------------------------

    [Fact]
    public async Task RoutesModeOff_ReproducesV1IdsAndOutputs()
    {
        _options.RoutesMode = TypeSafeRoutesMode.Off;
        var oracle = SetUpNestedFaq();

        var rows = await CreateMapper(oracle).MapAsync("nestedBlocksPage", "WebPage", []);

        oracle.Inner.AskedIds.Should().NotContain(id => id.Contains("__faqSection", StringComparison.Ordinal), "no planner question carries an element-type path");
        oracle.Inner.AskedIds.Should().Contain("shape__HasPart").And.Contain("inner__HasPart__heading");
        oracle.Inner.AskedIds.Should().Contain(id => id.StartsWith("desc__HasPart__0__", StringComparison.Ordinal), "the v1 descent id is desc__{prop}__{depth}__{beam}");

        var hasPart = Row(rows, "HasPart");
        hasPart.SuggestedNestedSchemaTypeName.Should().Be("WebPageElement");
        var config = ParseAsTheCoreDoes(hasPart.SuggestedResolverConfig);
        config.Routes.Should().BeNull("Off never emits routes");
        config.NestedMappings.Should().Contain(m => m.SchemaProperty == "Name" && m.ContentProperty == "heading");
    }

    [Fact]
    public async Task RoutesModeAlways_EmitsRoutesForASingleElementType()
    {
        _options.RoutesMode = TypeSafeRoutesMode.Always;

        var rows = await CreateMapper(SetUpRecipe()).MapAsync("recipePage", "Recipe", []);

        var instructions = Row(rows, "RecipeInstructions");
        instructions.SuggestedNestedSchemaTypeName.Should().BeNull();
        AssertJsonEquivalent(instructions.SuggestedResolverConfig, """
            {"routes":[{"blockAlias":"stepBlock","nestedSchemaType":"HowToStep","propertyMappings":[
              {"schemaProperty":"Name","contentProperty":"stepName"},
              {"schemaProperty":"Text","contentProperty":"stepText"}
            ]}]}
            """);
    }

    // -----------------------------------------------------------------------
    // Option order for a nested list field
    // -----------------------------------------------------------------------

    [Fact]
    public async Task InnerTargets_ForABlockEditorField_OfferTheCollectionTargetsFirst()
    {
        var oracle = SetUpNestedFaq();

        await CreateMapper(oracle).MapAsync("nestedBlocksPage", "WebPage", []);

        var registryOrder = SharedSchemaRegistry.Registry.GetProperties("WebPageElement").Select(p => p.Name).ToList();
        var collectionFirst = new[] { "MainEntity", "HasPart", "About", "ItemListElement" }
            .Where(n => registryOrder.Contains(n, StringComparer.OrdinalIgnoreCase))
            .ToList();
        collectionFirst.Should().NotBeEmpty();

        var blockFieldKeys = OptionKeys(oracle.Inner.AskedQuestions["inner__HasPart__faqSection__questions"]);
        blockFieldKeys.Take(collectionFirst.Count).Should().Equal(collectionFirst,
            "a repeating list of blocks is a collection of things, and option order was measured to matter");
        blockFieldKeys[^1].Should().Be(JudgmentSession.None);
        blockFieldKeys.SkipLast(1).Should().BeEquivalentTo(registryOrder, "the same real properties are offered, only reordered");

        var scalarFieldKeys = OptionKeys(oracle.Inner.AskedQuestions["inner__HasPart__faqSection__heading"]);
        scalarFieldKeys.SkipLast(1).Should().Equal(registryOrder, "a scalar field is offered the nested type's properties in registry order, as v1 did");
    }

    // -----------------------------------------------------------------------
    // Question ids: two aliases that sanitise to the same id both get a question
    // -----------------------------------------------------------------------

    private (BlockRoutePlanner Planner, FakeTypeSafeClient Client) CreatePlanner(Func<string, SystemOneQuestion, SystemOneAnswer?> answer)
    {
        var client = new FakeTypeSafeClient(answer);
        var session = new JudgmentSession(client, _options.MaxQuestionsPerRequest, _logger);
        var planner = new BlockRoutePlanner(
            session,
            SharedSchemaRegistry.Graph,
            nestedType => SharedSchemaRegistry.Registry.GetProperties(nestedType).ToList(),
            _options,
            _logger);
        return (planner, client);
    }

    /// <summary>
    /// Keeps every element on its first declared root, stops the descent at once, binds a
    /// nested list field to <c>HasPart</c> and a scalar to <c>Name</c> (or <c>Description</c>
    /// for a suffixed id, so two colliding fields can be told apart in the plan), and judges
    /// every nested list a list of things.
    /// </summary>
    private static SystemOneAnswer AnswerByIdKind(string id, SystemOneQuestion question)
    {
        var options = FakeTypeSafeClient.CriteriaOf(question).Keys.ToList();
        var choice = id.Split("__")[0] switch
        {
            "root" => options.First(o => o != JudgmentSession.None),
            "desc" => FakeTypeSafeClient.OptionNamed(question, JudgmentSession.Stop) ?? options[0],
            "shape" => "nested",
            "strlist" => options[0],
            "inner" when (question.Instructions as string ?? string.Empty).Contains("itself a repeating list") => FakeTypeSafeClient.OptionNamed(question, "HasPart"),
            "inner" => FakeTypeSafeClient.OptionNamed(question, id.EndsWith("_2", StringComparison.Ordinal) ? "Description" : "Name"),
            _ => null,
        };
        return FakeTypeSafeClient.Choice(choice ?? JudgmentSession.None, 0.9);
    }

    [Fact]
    public async Task PlannerFieldIds_TwoAliasesThatSanitiseToTheSameId_BothGetAQuestion()
    {
        var (planner, client) = CreatePlanner(AnswerByIdKind);
        var subject = new BlockListSubject("HasPart", "body", "WebPage", "HasPart",
            [Block("faqBlock", "FAQ", ("main-text", TextBox), ("main_text", TextArea))]);

        var plans = await planner.PlanAsync(new { }, [subject], CancellationToken.None);

        const string first = "inner__HasPart__faqBlock__main_text";
        const string second = "inner__HasPart__faqBlock__main_text_2";
        client.Requests.Should().Contain(r => r.Questions.ContainsKey(first) && r.Questions.ContainsKey(second),
            "main-text and main_text sanitise to the same id in the same round, and the second must not overwrite the first's question");
        var plan = plans["HasPart"];
        plan.NestedMappings.Select(m => (m.ContentProperty, m.SchemaProperty)).Should().Equal(
            new[] { ("main-text", "Name"), ("main_text", "Description") },
            "both fields are bound, each from its own answer");
        plan.Routes.Should().ContainSingle().Which.PropertyMappings.Select(m => m.ContentProperty).Should().Equal("main-text", "main_text");
    }

    [Fact]
    public async Task PlannerNestedFieldIds_TwoNestedListsThatSanitiseToTheSameId_BothGetShapeAndTextQuestions()
    {
        var (planner, client) = CreatePlanner(AnswerByIdKind);
        var subject = new BlockListSubject("HasPart", "body", "WebPage", "HasPart",
            [BlockOf("section", "Section",
                Field("heading", TextBox),
                NestedField("sub-items", BlockList, Block("item", "Item", ("question", TextBox), ("answer", TextArea))),
                NestedField("sub_items", BlockList, Block("item", "Item", ("question", TextBox), ("answer", TextArea))))]);

        var plans = await planner.PlanAsync(new { }, [subject], CancellationToken.None);

        client.AskedIds.Should().Contain(
            [
                "inner__HasPart__section__sub_items", "inner__HasPart__section__sub_items_2",
                "shape__HasPart__section__sub_items", "shape__HasPart__section__sub_items_2",
                "strlist__HasPart__section__sub_items", "strlist__HasPart__section__sub_items_2",
            ],
            "every planner question id is made unique within its round, the speculative string-list question included");
        var route = plans["HasPart"].Routes.Should().ContainSingle().Subject;
        route.PropertyMappings.Select(m => m.ContentProperty).Should().Equal("heading", "sub-items", "sub_items");
        route.PropertyMappings.Where(m => m.Routes is not null).Should().HaveCount(2, "each nested list got its own plan from its own answers");
    }

    // -----------------------------------------------------------------------
    // Never invents, never throws
    // -----------------------------------------------------------------------

    [Fact]
    public async Task EveryBlockAliasAndFieldAlias_InTheRoutes_IsReal()
    {
        var oracle = SetUpNestedFaq();
        var blocks = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "faqSection", "faqItem" };
        var fields = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "heading", "questions", "question", "answer" };

        var rows = await CreateMapper(oracle).MapAsync("nestedBlocksPage", "WebPage", []);

        var config = JsonNode.Parse(Row(rows, "HasPart").SuggestedResolverConfig!)!.AsObject();
        var visited = 0;
        void Walk(JsonArray routes)
        {
            foreach (var route in routes)
            {
                blocks.Should().Contain(route!["blockAlias"]!.GetValue<string>());
                foreach (var mapping in route["propertyMappings"]!.AsArray())
                {
                    visited++;
                    fields.Should().Contain(mapping!["contentProperty"]!.GetValue<string>());
                    if (mapping["nestedContentProperty"] is { } ncp)
                        fields.Should().Contain(ncp.GetValue<string>());
                    if (mapping["routes"] is JsonArray nested)
                        Walk(nested);
                }
            }
        }

        Walk(config["routes"]!.AsArray());
        visited.Should().Be(4);
    }

    public static TheoryData<string> DegenerateAnswerModes => new()
    {
        "missing", "malformed-choice", "unoffered-option", "nan-confidence", "wrong-type", "none", "first-option",
    };

    [Theory]
    [MemberData(nameof(DegenerateAnswerModes))]
    public async Task DegenerateAnswers_NeverThrow_AndNeverInventAnAlias(string mode)
    {
        SetUpNestedFaq();
        var known = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "title", "sections", "heading", "questions", "question", "answer", "__url", "__name", "__createDate", "__updateDate",
        };
        var client = new FakeTypeSafeClient((_, q) => mode switch
        {
            "missing" => null,
            "malformed-choice" => new SystemOneAnswer { Type = "choice", Choice = null, Confidence = 0.9 },
            "unoffered-option" => new SystemOneAnswer { Type = "choice", Choice = "inventedAlias", Confidence = 0.9 },
            "nan-confidence" => q.Type == "noul"
                ? new SystemOneAnswer { Type = "noul", Noul = double.NaN }
                : new SystemOneAnswer { Type = "choice", Choice = FakeTypeSafeClient.CriteriaOf(q).Keys.First(), Confidence = double.NaN },
            "wrong-type" => new SystemOneAnswer { Type = "noul", Noul = 0.9 },
            "first-option" => q.Type == "noul" ? FakeTypeSafeClient.Noul(0.9) : FakeTypeSafeClient.Choice(FakeTypeSafeClient.CriteriaOf(q).Keys.First(), 0.9),
            _ => q.Type == "noul" ? FakeTypeSafeClient.Noul(0) : FakeTypeSafeClient.Choice("__none", 0.1),
        });

        var act = () => CreateMapper(client).MapAsync("nestedBlocksPage", "WebPage", []);

        var rows = (await act.Should().NotThrowAsync()).Subject;
        foreach (var row in rows)
        {
            known.Should().Contain(row.SuggestedContentTypePropertyAlias!);
            if (row.SuggestedResolverConfig is null)
                continue;

            row.SuggestedResolverConfig.Should().NotContain("inventedAlias");
            foreach (var alias in JsonNode.Parse(row.SuggestedResolverConfig)!.AsObject().DescendantContentProperties())
                known.Should().Contain(alias);
        }
    }
}

/// <summary>Walks a resolver config for every content alias it names, at any nesting depth.</summary>
internal static class ResolverConfigJsonExtensions
{
    public static IEnumerable<string> DescendantContentProperties(this JsonObject config)
    {
        foreach (var key in new[] { "contentProperty", "nestedContentProperty", "contentTypePropertyAlias" })
        {
            if (config[key] is { } value)
                yield return value.GetValue<string>();
        }

        foreach (var key in new[] { "routes", "nestedMappings", "complexTypeMappings", "propertyMappings" })
        {
            if (config[key] is not JsonArray array)
                continue;

            foreach (var item in array.OfType<JsonObject>())
            {
                foreach (var alias in item.DescendantContentProperties())
                    yield return alias;
            }
        }
    }
}
