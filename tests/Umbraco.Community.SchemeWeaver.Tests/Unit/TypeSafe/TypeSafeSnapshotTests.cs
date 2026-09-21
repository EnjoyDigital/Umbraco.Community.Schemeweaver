using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Umbraco.Cms.Core.Models;
using Umbraco.Cms.Core.Persistence.Querying;
using Umbraco.Cms.Core.Services;
using Umbraco.Community.SchemeWeaver.Models.Api;
using Umbraco.Community.SchemeWeaver.Services;
using Umbraco.Community.SchemeWeaver.Services.ValueSchemas;
using Umbraco.Community.SchemeWeaver.Tests.Unit.TypeSafe.TestSupport;
using Umbraco.Community.SchemeWeaver.TypeSafe.Configuration;
using Umbraco.Community.SchemeWeaver.TypeSafe.Services.Judgments;
using Xunit;
using static Umbraco.Community.SchemeWeaver.Tests.Unit.TypeSafe.TestSupport.TypeSafeTestContentTypes;

namespace Umbraco.Community.SchemeWeaver.Tests.Unit.TypeSafe;

/// <summary>
/// What the model is allowed to see (<see cref="ContentTypeSnapshot"/>) on the v2 axes: the
/// value schema per property, the opt-in sample values, the recursive nested block structure to
/// <see cref="TypeSafeOptions.MaxBlockRouteDepth"/>, and the neighbourhood node that is attached
/// only when a caller passes it. The v1 entry point is asserted alongside so the v1 state is
/// shown to be unchanged.
/// </summary>
public class TypeSafeSnapshotTests
{
    private readonly IContentTypeService _contentTypes = Substitute.For<IContentTypeService>();
    private readonly ISchemeWeaverService _schemeWeaver = Substitute.For<ISchemeWeaverService>();
    private readonly IPropertyValueSchemaService _valueSchemas = Substitute.For<IPropertyValueSchemaService>();
    private readonly IServiceProvider _provider = Substitute.For<IServiceProvider>();
    private readonly TypeSafeOptions _options = new() { ApiKey = "test-key", EnableCrossNodeSources = false };
    private readonly RecordingLogger<ContentTypeSnapshot> _logger = new();

    public TypeSafeSnapshotTests()
    {
        _provider.GetService(typeof(ISchemeWeaverService)).Returns(_schemeWeaver);
        _valueSchemas.GetDataTypeValueSchemaAsync(Arg.Any<Guid>()).Returns(Task.FromResult<string?>(null));
        _schemeWeaver.GetBlockElementTypesAsync(Arg.Any<string>(), Arg.Any<string>())
            .Returns(Task.FromResult<IEnumerable<BlockElementTypeInfo>>([]));
    }

    private void Register(IContentType contentType)
        => _contentTypes.Get(contentType.Alias).Returns(contentType);

    private void RegisterBlocks(string contentTypeAlias, string propertyAlias, params BlockElementTypeInfo[] blocks)
        => _schemeWeaver.GetBlockElementTypesAsync(contentTypeAlias, propertyAlias)
            .Returns(Task.FromResult<IEnumerable<BlockElementTypeInfo>>(blocks));

    private async Task<ContentTypeSnapshot> BuildV2(string alias)
        => (await ContentTypeSnapshot.BuildAsync(_contentTypes, _provider, _valueSchemas, _options, alias, _logger, CancellationToken.None))
           ?? throw new InvalidOperationException("snapshot not built");

    private async Task<ContentTypeSnapshot> BuildV1(string alias)
        => (await ContentTypeSnapshot.BuildAsync(_contentTypes, _provider, alias, _logger, CancellationToken.None))
           ?? throw new InvalidOperationException("snapshot not built");

    private static JsonElement StateOf(ContentTypeSnapshot snapshot, string? targetSchemaType = "WebPage", ContentTypeNeighbourhood? neighbourhood = null)
    {
        var state = neighbourhood is null ? snapshot.ToState(targetSchemaType) : snapshot.ToState(targetSchemaType, neighbourhood);
        return JsonDocument.Parse(JsonSerializer.Serialize(state)).RootElement.Clone();
    }

    private static JsonElement PropertyOf(JsonElement state, string alias)
        => state.GetProperty("umbracoContentType").GetProperty("properties").EnumerateArray()
            .Single(p => p.GetProperty("alias").GetString() == alias);

    private void SetValueSchema(string propertyAlias, string? schema)
        => _valueSchemas.GetDataTypeValueSchemaAsync(KeyFor("dataType:" + propertyAlias)).Returns(Task.FromResult(schema));

    // -----------------------------------------------------------------------
    // Value schemas
    // -----------------------------------------------------------------------

    [Fact]
    public async Task ValueSchema_IsPresentWhenTheServiceReturnsOne_AndAbsentWhenItReturnsNull()
    {
        Register(ContentType("blogPost", "Blog Post", ("title", TextBox), ("summary", TextArea)));
        SetValueSchema("title", """{"type":"string","maxLength":120}""");
        SetValueSchema("summary", null);

        var v2 = StateOf(await BuildV2("blogPost"));
        PropertyOf(v2, "title").GetProperty("valueSchema").GetString().Should().Be("""{"type":"string","maxLength":120}""");
        PropertyOf(v2, "summary").TryGetProperty("valueSchema", out _).Should().BeFalse("an absent schema is omitted, not serialised as null or empty");
        PropertyOf(v2, "__name").TryGetProperty("valueSchema", out _).Should().BeFalse("built-ins have no data type");

        var v1 = StateOf(await BuildV1("blogPost"));
        PropertyOf(v1, "title").TryGetProperty("valueSchema", out _).Should().BeFalse("the v1 state never carries value schemas");
        await _valueSchemas.Received(1).GetDataTypeValueSchemaAsync(KeyFor("dataType:title"));
    }

    [Fact]
    public async Task ValueSchema_IsTruncatedToTheCapWithTheSharedMarker()
    {
        Register(ContentType("blogPost", "Blog Post", ("body", RichText)));
        var longSchema = new string('x', ContentTypeSnapshot.MaxValueSchemaChars + 400);
        SetValueSchema("body", longSchema);

        var state = StateOf(await BuildV2("blogPost"));

        var schema = PropertyOf(state, "body").GetProperty("valueSchema").GetString()!;
        schema.Should().HaveLength(ContentTypeSnapshot.MaxValueSchemaChars + " …(schema truncated)".Length);
        schema.Should().EndWith(" …(schema truncated)", "the same marker the AI satellite appends, so both models see the same shape");
        ContentTypeSnapshot.TruncateValueSchema("   ").Should().BeNull();
        ContentTypeSnapshot.TruncateValueSchema("{}").Should().Be("{}");
    }

    [Fact]
    public async Task ValueSchemaFailure_DegradesToEditorOnly_WithoutFailingTheSnapshot()
    {
        Register(ContentType("blogPost", "Blog Post", ("title", TextBox)));
        _valueSchemas.GetDataTypeValueSchemaAsync(KeyFor("dataType:title")).ThrowsAsync(new InvalidOperationException("boom"));

        var state = StateOf(await BuildV2("blogPost"));

        PropertyOf(state, "title").GetProperty("editor").GetString().Should().Be(TextBox);
        PropertyOf(state, "title").TryGetProperty("valueSchema", out _).Should().BeFalse();
    }

    // -----------------------------------------------------------------------
    // Sample values (opt-in)
    // -----------------------------------------------------------------------

    private IContentService RegisterSampleNode(string contentTypeAlias, params (string Alias, string Value)[] values)
    {
        var node = Substitute.For<IContent>();
        node.Id.Returns(5001);
        node.Published.Returns(true);
        foreach (var (alias, value) in values)
            node.GetValue(alias, null, null, true).Returns(value);

        var contentService = Substitute.For<IContentService>();
        contentService.GetPagedOfTypes(Arg.Any<int[]>(), Arg.Any<long>(), Arg.Any<int>(), out Arg.Any<long>(), Arg.Any<IQuery<IContent>?>(), Arg.Any<Ordering?>())
            .Returns([node]);
        _provider.GetService(typeof(IContentService)).Returns(contentService);
        return contentService;
    }

    [Fact]
    public async Task SampleValues_AreOffByDefault_AndTheContentServiceIsNeverTouched()
    {
        Register(ContentType("blogPost", "Blog Post", ("title", TextBox)));
        var contentService = RegisterSampleNode("blogPost", ("title", "Hello"));

        var state = StateOf(await BuildV2("blogPost"));

        PropertyOf(state, "title").TryGetProperty("sampleValue", out _).Should().BeFalse("sample values are customer content and leave the site: opt-in only");
        contentService.ReceivedCalls().Should().BeEmpty();
    }

    [Fact]
    public async Task SampleValues_WhenIncluded_AreHtmlStrippedTruncated_AndNeverForBlocksOrPickers()
    {
        _options.IncludeSampleValues = true;
        Register(ContentType("blogPost", "Blog Post",
            ("title", TextBox), ("body", RichText), ("sections", BlockList), ("related", ContentPicker), ("hero", MediaPicker3), ("status", Label)));
        RegisterBlocks("blogPost", "sections", Block("textBlock", "Text", ("text", TextArea)));
        var longText = new string('a', 300);
        RegisterSampleNode("blogPost",
            ("title", "  Hello <b>world</b>&nbsp;again  "),
            ("body", "<p>" + longText + "</p>"),
            ("sections", "[{\"contentData\":[]}]"),
            ("related", "umb://document/abc"),
            ("hero", "[{\"mediaKey\":\"abc\"}]"),
            ("status", "Draft"));

        var state = StateOf(await BuildV2("blogPost"));

        PropertyOf(state, "title").GetProperty("sampleValue").GetString().Should().Be("Hello world again", "tags stripped, entities decoded, whitespace collapsed");
        var body = PropertyOf(state, "body").GetProperty("sampleValue").GetString()!;
        body.Should().HaveLength(ContentTypeSnapshot.MaxSampleValueChars + 1).And.EndWith("…");
        PropertyOf(state, "sections").TryGetProperty("sampleValue", out _).Should().BeFalse("a block holds a structure the value schema already describes");
        PropertyOf(state, "related").TryGetProperty("sampleValue", out _).Should().BeFalse("a picker holds a reference, not a value");
        PropertyOf(state, "hero").TryGetProperty("sampleValue", out _).Should().BeFalse();
        PropertyOf(state, "status").TryGetProperty("sampleValue", out _).Should().BeFalse("a label holds nothing an editor typed");
        PropertyOf(state, "__name").TryGetProperty("sampleValue", out _).Should().BeFalse();

        var v1 = StateOf(await BuildV1("blogPost"));
        PropertyOf(v1, "title").TryGetProperty("sampleValue", out _).Should().BeFalse("the v1 state never carries sample values");
    }

    [Fact]
    public async Task SampleValues_WithNoContentService_DegradeToStructureOnly()
    {
        _options.IncludeSampleValues = true;
        Register(ContentType("blogPost", "Blog Post", ("title", TextBox)));

        var state = StateOf(await BuildV2("blogPost"));

        PropertyOf(state, "title").TryGetProperty("sampleValue", out _).Should().BeFalse();
        _provider.Received().GetService(typeof(IContentService));
    }

    private static IContent SampleNode(int id, bool published, params (string Alias, string Value)[] values)
    {
        var node = Substitute.For<IContent>();
        node.Id.Returns(id);
        node.Published.Returns(published);
        foreach (var (alias, value) in values)
        {
            // Stubbed for the published AND the draft read, so a regression that reads a
            // draft surfaces as a sample value rather than hiding behind a null.
            node.GetValue(alias, null, null, true).Returns(value);
            node.GetValue(alias, null, null, false).Returns(value);
        }

        return node;
    }

    private IContentService RegisterSampleNodes(params IContent[] nodes)
    {
        var contentService = Substitute.For<IContentService>();
        contentService.GetPagedOfTypes(Arg.Any<int[]>(), Arg.Any<long>(), Arg.Any<int>(), out Arg.Any<long>(), Arg.Any<IQuery<IContent>?>(), Arg.Any<Ordering?>())
            .Returns(nodes);
        _provider.GetService(typeof(IContentService)).Returns(contentService);
        return contentService;
    }

    [Fact]
    public async Task SampleValues_ComeFromAPublishedNode_NeverFromADraftAheadOfIt()
    {
        _options.IncludeSampleValues = true;
        Register(ContentType("blogPost", "Blog Post", ("title", TextBox)));
        RegisterSampleNodes(
            SampleNode(5001, published: false, ("title", "Embargoed draft")),
            SampleNode(5002, published: true, ("title", "Live title")));

        var state = StateOf(await BuildV2("blogPost"));

        PropertyOf(state, "title").GetProperty("sampleValue").GetString().Should().Be("Live title",
            "the first node in tree order is a draft, and only a published node may be sampled");
    }

    [Fact]
    public async Task SampleValues_WhenTheTypeHasNoPublishedNode_AreAbsentEverywhere()
    {
        _options.IncludeSampleValues = true;
        Register(ContentType("blogPost", "Blog Post", ("title", TextBox), ("summary", TextArea)));
        var draft = SampleNode(5001, published: false, ("title", "Embargoed draft"), ("summary", "Not yet public"));
        var contentService = RegisterSampleNodes(draft, SampleNode(5002, published: false, ("title", "Another draft")));

        var state = StateOf(await BuildV2("blogPost"));

        contentService.ReceivedCalls().Should().NotBeEmpty("the sample path ran and found no published node to read");
        foreach (var property in state.GetProperty("umbracoContentType").GetProperty("properties").EnumerateArray())
        {
            property.TryGetProperty("sampleValue", out _).Should().BeFalse(
                "a draft may be embargoed content and the option promises one published node, so a type with only drafts yields no sample values");
        }

        draft.DidNotReceive().GetValue(Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<bool>());
        JsonSerializer.Serialize(state).Should().NotContain("Embargoed").And.NotContain("Not yet public");
    }

    // -----------------------------------------------------------------------
    // Nested block structure
    // -----------------------------------------------------------------------

    private void RegisterThreeLevels()
    {
        Register(ContentType("deepPage", "Deep Page", ("sections", BlockList)));
        RegisterBlocks("deepPage", "sections",
            BlockOf("faqSection", "FAQ Section",
                Field("heading", TextBox, """{"type":"string"}"""),
                NestedField("questions", BlockList,
                    BlockOf("faqItem", "FAQ Item",
                        Field("question", TextBox),
                        NestedField("followUps", BlockList,
                            Block("followUp", "Follow-up", ("text", TextArea)))))));
    }

    private static JsonElement FieldOf(JsonElement blockType, string alias)
        => blockType.GetProperty("fields").EnumerateArray().Single(f => f.GetProperty("alias").GetString() == alias);

    [Fact]
    public async Task NestedBlockStructure_IsInTheState_ToMaxBlockRouteDepth()
    {
        RegisterThreeLevels();

        _options.MaxBlockRouteDepth = 1;
        var depthOne = StateOf(await BuildV2("deepPage"));
        var section = PropertyOf(depthOne, "sections").GetProperty("blockTypes").EnumerateArray().Single();
        var questions = FieldOf(section, "questions");
        questions.GetProperty("editor").GetString().Should().Be(BlockList);
        var item = questions.GetProperty("blockTypes").EnumerateArray().Single();
        item.GetProperty("blockType").GetString().Should().Be("faqItem");
        FieldOf(item, "question").GetProperty("editor").GetString().Should().Be(TextBox);
        FieldOf(item, "followUps").TryGetProperty("blockTypes", out _).Should().BeFalse("level 2 is beyond a depth of 1 and reads as a plain block field");

        _options.MaxBlockRouteDepth = 2;
        var depthTwo = StateOf(await BuildV2("deepPage"));
        var followUps = FieldOf(FieldOf(PropertyOf(depthTwo, "sections").GetProperty("blockTypes")[0], "questions").GetProperty("blockTypes")[0], "followUps");
        followUps.GetProperty("blockTypes")[0].GetProperty("blockType").GetString().Should().Be("followUp");

        var v1 = StateOf(await BuildV1("deepPage"));
        var v1Questions = FieldOf(PropertyOf(v1, "sections").GetProperty("blockTypes")[0], "questions");
        v1Questions.TryGetProperty("blockTypes", out _).Should().BeFalse("the v1 state flattens a nested Block List field to its editor alias");
        v1Questions.TryGetProperty("valueSchema", out _).Should().BeFalse();
    }

    [Fact]
    public async Task BlockFieldValueSchemas_AreInTheV2StateOnly()
    {
        RegisterThreeLevels();

        var v2 = StateOf(await BuildV2("deepPage"));
        var v1 = StateOf(await BuildV1("deepPage"));

        FieldOf(PropertyOf(v2, "sections").GetProperty("blockTypes")[0], "heading").GetProperty("valueSchema").GetString().Should().Be("""{"type":"string"}""");
        FieldOf(PropertyOf(v1, "sections").GetProperty("blockTypes")[0], "heading").TryGetProperty("valueSchema", out _).Should().BeFalse();
    }

    [Fact]
    public async Task SummariseBlocks_DescribesNestedLists_OnlyWithDepth()
    {
        RegisterThreeLevels();

        var v2 = await BuildV2("deepPage");
        v2.SummariseBlocks("sections").Should().Contain("questions: a nested list of faqItem(question, followUps: a nested list of followUp(text))",
            "the shape question must be able to see that a block carries a list");

        var v1 = await BuildV1("deepPage");
        v1.SummariseBlocks("sections").Should().Contain("(fields: heading, questions)").And.NotContain("nested list");
    }

    [Fact]
    public async Task BlockElementTypes_ReturnsTheRawElementTypes_ForBlockPropertiesOnly()
    {
        RegisterThreeLevels();

        var snapshot = await BuildV2("deepPage");

        var elements = snapshot.BlockElementTypes("sections");
        elements.Should().ContainSingle().Which.PropertyInfos.Single(p => p.Alias == "questions").NestedBlockElementTypes
            .Should().ContainSingle().Which.Alias.Should().Be("faqItem");
        snapshot.BlockElementTypes("__name").Should().BeEmpty();
        snapshot.BlockElementTypes("missing").Should().BeEmpty();
    }

    // -----------------------------------------------------------------------
    // The neighbourhood node
    // -----------------------------------------------------------------------

    [Fact]
    public async Task NeighbourhoodNode_IsAttachedOnlyWhenGiven_AndOnlyWhenNotEmpty()
    {
        Register(ContentType("blogPost", "Blog Post", ("title", TextBox)));
        var snapshot = await BuildV2("blogPost");
        var neighbourhood = new ContentTypeNeighbourhood(
            [new NeighbourType(NeighbourRelation.Parent, "blogListing", "Blog Listing", 1, [new NeighbourProperty("title", "Title", TextBox)])],
            [],
            [],
            allowedAsRoot: true);

        StateOf(snapshot).TryGetProperty("neighbourhood", out _).Should().BeFalse("the single-argument overload never attaches it");
        StateOf(snapshot, neighbourhood: ContentTypeNeighbourhood.Empty).TryGetProperty("neighbourhood", out _).Should().BeFalse("an empty neighbourhood is not worth its tokens");

        var withNode = StateOf(snapshot, neighbourhood: neighbourhood).GetProperty("neighbourhood");
        withNode.GetProperty("allowedAsRoot").GetBoolean().Should().BeTrue();
        var parent = withNode.GetProperty("parents").EnumerateArray().Single();
        parent.GetProperty("alias").GetString().Should().Be("blogListing");
        parent.GetProperty("depth").GetInt32().Should().Be(1);
        parent.GetProperty("properties").EnumerateArray().Single().GetProperty("editor").GetString().Should().Be(TextBox);
        withNode.GetProperty("note").GetString().Should().Contain("nearest ancestor");
        StateOf(snapshot, neighbourhood: neighbourhood).GetProperty("targetSchemaType").GetString().Should().Be("WebPage");
    }

    // -----------------------------------------------------------------------
    // The state budget (MaxStateCharacters)
    // -----------------------------------------------------------------------

    /// <summary>A value schema at the per-field cap, so every field costs the most a field can.</summary>
    private static readonly string LongSchema = new('s', ContentTypeSnapshot.MaxValueSchemaChars);

    private const string SamplePrefix = "Sample value for ";

    /// <summary>What the state still carries, read back from its JSON.</summary>
    private sealed record Detail(bool BlockFieldSchemas, int NestedLevels, bool PropertySchemas, bool SampleValues);

    private static Detail DetailOf(JsonElement state)
    {
        var properties = state.GetProperty("umbracoContentType").GetProperty("properties").EnumerateArray().ToList();
        var blockFieldSchemas = false;
        var nestedLevels = 0;

        void Walk(JsonElement blockTypes, int level)
        {
            foreach (var field in blockTypes.EnumerateArray().SelectMany(bt => bt.GetProperty("fields").EnumerateArray()))
            {
                blockFieldSchemas |= field.TryGetProperty("valueSchema", out _);
                if (field.TryGetProperty("blockTypes", out var nested))
                {
                    nestedLevels = Math.Max(nestedLevels, level + 1);
                    Walk(nested, level + 1);
                }
            }
        }

        foreach (var property in properties)
        {
            if (property.TryGetProperty("blockTypes", out var blockTypes))
                Walk(blockTypes, 0);
        }

        return new Detail(
            blockFieldSchemas,
            nestedLevels,
            properties.Any(p => p.TryGetProperty("valueSchema", out _)),
            properties.Any(p => p.TryGetProperty("sampleValue", out _)));
    }

    private static int Measure(ContentTypeSnapshot snapshot)
        => ContentTypeSnapshot.CountStateCharacters(snapshot.ToState(targetSchemaType: null));

    /// <summary>
    /// Wider than the default budget: eight scalar properties, each with a value schema and a
    /// sample value, and four Block Lists whose two element types each carry three fields plus
    /// a nested list, expanded three levels deep, every field with a value schema at the cap.
    /// </summary>
    private void RegisterWidePage()
    {
        _options.IncludeSampleValues = true;
        var scalars = Enumerable.Range(1, 8).Select(i => ($"text{i}", TextBox)).ToList();
        var blocks = Enumerable.Range(1, 4).Select(i => ($"sections{i}", BlockList)).ToList();
        Register(ContentType("widePage", "Wide Page", [.. scalars, .. blocks]));

        foreach (var (alias, _) in scalars)
            SetValueSchema(alias, LongSchema);
        foreach (var (alias, _) in blocks)
            RegisterBlocks("widePage", alias, WideElements(alias, level: 0));

        RegisterSampleNodes(SampleNode(5001, published: true, scalars.Select(s => (s.Item1, SamplePrefix + s.Item1)).ToArray()));
    }

    private static BlockElementTypeInfo[] WideElements(string prefix, int level)
        => Enumerable.Range(1, 2).Select(i =>
        {
            var alias = $"{prefix}El{level}{i}";
            var fields = Enumerable.Range(1, 3).Select(f => Field($"{alias}Field{f}", TextBox, LongSchema)).ToList();
            if (level < 3)
            {
                var nested = NestedField($"{alias}Items", BlockList, WideElements(alias, level + 1));
                nested.ValueSchema = LongSchema;
                fields.Add(nested);
            }

            return BlockOf(alias, alias, fields.ToArray());
        }).ToArray();

    private async Task<(int Size, Detail Detail, List<string> Dropped, bool StillOver)> BuildWideWithBudget(int budget)
    {
        _options.MaxStateCharacters = budget;
        _logger.Entries.Clear();
        var snapshot = await BuildV2("widePage");
        var dropped = _logger.Entries.Where(e => e.Values.ContainsKey("Detail")).Select(e => (string)e.Values["Detail"]!).ToList();
        var stillOver = _logger.Entries.Any(e => e.Message.Contains("with every reduction applied"));
        return (Measure(snapshot), DetailOf(StateOf(snapshot)), dropped, stillOver);
    }

    [Fact]
    public async Task StateBudget_ThinsAWideTypeInTheDocumentedOrder_OneStepAtATime()
    {
        RegisterWidePage();

        var full = await BuildWideWithBudget(int.MaxValue);
        full.Size.Should().BeGreaterThan(new TypeSafeOptions().MaxStateCharacters, "the fixture must be wider than the default budget for the guard to have anything to do");
        full.Detail.Should().Be(new Detail(true, 3, true, true), "an unlimited budget drops nothing");
        full.Dropped.Should().BeEmpty();

        // A budget one under the previous stage's size forces exactly the next reduction: each
        // stage re-measures, and nothing beyond what was needed is touched.
        var a = await BuildWideWithBudget(full.Size - 1);
        a.Dropped.Should().Equal(new[] { "block-field value schemas" }, "the block-field schemas go first, and a budget just under the full size needs only them");
        a.Detail.Should().Be(new Detail(false, 3, true, true), "property value schemas and sample values survive when dropping the block-field schemas was enough");
        a.Size.Should().BeLessThanOrEqualTo(full.Size - 1);
        a.StillOver.Should().BeFalse();

        var b = await BuildWideWithBudget(a.Size - 1);
        b.Dropped.Should().Equal("block-field value schemas", "nested block levels beyond the first");
        b.Detail.Should().Be(new Detail(false, 1, true, true), "the first nested level is kept: it is what the shape question reads");
        b.Size.Should().BeLessThanOrEqualTo(a.Size - 1);

        var c = await BuildWideWithBudget(b.Size - 1);
        c.Dropped.Should().Equal("block-field value schemas", "nested block levels beyond the first", "property value schemas");
        c.Detail.Should().Be(new Detail(false, 1, false, true), "sample values are the last detail to go");
        c.Size.Should().BeLessThanOrEqualTo(b.Size - 1);

        var d = await BuildWideWithBudget(c.Size - 1);
        d.Dropped.Should().Equal("block-field value schemas", "nested block levels beyond the first", "property value schemas", "sample values");
        d.Detail.Should().Be(new Detail(false, 1, false, false));
        d.Size.Should().BeLessThanOrEqualTo(c.Size - 1);
        d.StillOver.Should().BeFalse();

        var e = await BuildWideWithBudget(d.Size - 1);
        e.Size.Should().Be(d.Size, "with every reduction applied there is nothing left to drop");
        e.Detail.Should().Be(new Detail(false, 1, false, false));
        e.StillOver.Should().BeTrue("the remaining excess is reported, so an operator can see why the request was refused and raise the budget");
    }

    [Fact]
    public async Task StateBudget_DefaultKeepsAWideTypeUnderTheStateCap_AndLogsSizesOnly()
    {
        RegisterWidePage();
        _options.MaxStateCharacters.Should().Be(100_000, "roughly 25k tokens at four characters a token, under the 32k state cap with headroom for the questions");

        var snapshot = await BuildV2("widePage");

        ContentTypeSnapshot.CountStateCharacters(snapshot.ToState("WebPage")).Should().BeLessThanOrEqualTo(_options.MaxStateCharacters,
            "the state every round sends, target type included, must fit the budget");
        var steps = _logger.Entries.Where(e => e.Values.ContainsKey("Detail")).ToList();
        steps.Should().NotBeEmpty("a type this wide cannot fit at full detail").And.OnlyContain(e => e.Level == LogLevel.Debug, "a reduction is routine, not a warning");
        var schemaExcerpt = LongSchema[..40];
        foreach (var entry in steps)
        {
            entry.Message.Should().NotContain(SamplePrefix).And.NotContain(schemaExcerpt, "sizes only, never content");
            entry.Values.Values.OfType<string>().Should().NotContain(v => v.Contains(SamplePrefix) || v.Contains(schemaExcerpt));
        }
    }

    [Fact]
    public async Task StateBudget_LeavesASmallTypeUntouched()
    {
        _options.IncludeSampleValues = true;
        Register(ContentType("smallPage", "Small Page", ("title", TextBox), ("sections", BlockList)));
        SetValueSchema("title", """{"type":"string"}""");
        RegisterBlocks("smallPage", "sections",
            BlockOf("faqSection", "FAQ Section",
                Field("heading", TextBox, """{"type":"string"}"""),
                NestedField("questions", BlockList,
                    BlockOf("faqItem", "FAQ Item",
                        Field("question", TextBox, """{"type":"string"}"""),
                        NestedField("followUps", BlockList,
                            Block("followUp", "Follow-up", ("text", TextArea)))))));
        RegisterSampleNodes(SampleNode(5001, published: true, ("title", "Hello")));

        var snapshot = await BuildV2("smallPage");

        Measure(snapshot).Should().BeLessThan(_options.MaxStateCharacters);
        DetailOf(StateOf(snapshot)).Should().Be(new Detail(true, 2, true, true), "nothing is dropped from a state that fits");
        _logger.Entries.Should().NotContain(e => e.Values.ContainsKey("Detail") || e.Message.Contains("over budget") || e.Message.Contains("with every reduction applied"));
    }
}
