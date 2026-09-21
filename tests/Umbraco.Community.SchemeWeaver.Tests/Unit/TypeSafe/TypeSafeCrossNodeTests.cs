using System.Text.Json;
using System.Text.Json.Nodes;
using FluentAssertions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Umbraco.Cms.Core.Models;
using Umbraco.Cms.Core.Models.Entities;
using Umbraco.Cms.Core.Persistence.Querying;
using Umbraco.Cms.Core.Services;
using Umbraco.Community.SchemeWeaver.Models.Api;
using Umbraco.Community.SchemeWeaver.Services;
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
/// The v2 cross-node round (<see cref="CrossNodeBinder"/> over <see cref="ContentTypeNeighbourhood"/>)
/// driven by the gold oracle: <c>parent</c>/<c>ancestor</c>/<c>sibling</c> rows for schema
/// properties the page itself does not supply, sourced from real neighbour types found either
/// in the document types' allowed-children structure or in the observed content tree. Every
/// assertion is about the mechanics code owns: which properties are asked, which options are
/// offered and in what order, how an answer decodes to a row, the confidence bar, and that the
/// neighbourhood's tokens are paid only on the cross-node request.
/// </summary>
public class TypeSafeCrossNodeTests
{
    private readonly IContentTypeService _contentTypes = Substitute.For<IContentTypeService>();
    private readonly ISchemeWeaverService _schemeWeaver = Substitute.For<ISchemeWeaverService>();
    private readonly IPropertyValueSchemaService _valueSchemas = Substitute.For<IPropertyValueSchemaService>();
    private readonly IServiceProvider _provider = Substitute.For<IServiceProvider>();
    private readonly TypeSafeOptions _options = new() { ApiKey = "test-key" };
    private readonly SchemaAutoMapperOptions _autoMapperOptions = new();
    private readonly RecordingLogger<TypeSafePropertyMapper> _logger = new();

    public TypeSafeCrossNodeTests()
    {
        _provider.GetService(typeof(ISchemeWeaverService)).Returns(_schemeWeaver);
        _valueSchemas.GetDataTypeValueSchemaAsync(Arg.Any<Guid>()).Returns(Task.FromResult<string?>(null));
        _schemeWeaver.GetBlockElementTypesAsync(Arg.Any<string>(), Arg.Any<string>())
            .Returns(Task.FromResult<IEnumerable<BlockElementTypeInfo>>([]));
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

    private static bool HasNeighbourhood(object state)
    {
        using var doc = JsonDocument.Parse(JsonSerializer.Serialize(state));
        return doc.RootElement.TryGetProperty("neighbourhood", out _);
    }

    private static IReadOnlyList<string> OptionKeys(SystemOneQuestion question)
        => FakeTypeSafeClient.CriteriaOf(question).Keys.ToList();

    // -----------------------------------------------------------------------
    // A small site, declared structurally: a home page over three listings
    // -----------------------------------------------------------------------

    /// <summary>
    /// homePage (root) allows productListing, blogListing and departmentsHub; productListing allows
    /// productPage and promoPage; blogListing allows blogArticle only; departmentsHub allows
    /// departmentPage and officePage. Block editors and content pickers on the neighbours exist
    /// precisely so the tests can prove they are never offered.
    /// </summary>
    private void SetUpSite()
    {
        Structure(_contentTypes,
            Type("homePage", "Home Page")
                .Properties(("organisationName", TextBox), ("siteName", TextBox), ("logo", MediaPicker3), ("footerBlocks", BlockList))
                .AllowedAsRoot()
                .Allows("productListing", "blogListing", "departmentsHub")
                .Build(),
            Type("productListing", "Product Listing")
                .Properties(("title", TextBox), ("intro", TextArea), ("featured", ContentPicker))
                .Allows("productPage", "promoPage")
                .Build(),
            Type("productPage", "Product Page")
                .Properties(("productName", TextBox), ("description", TextArea))
                .Build(),
            Type("promoPage", "Promo Page")
                .Properties(("promoText", TextBox))
                .Build(),
            Type("blogListing", "Blog Listing")
                .Properties(("title", TextBox), ("description", TextArea))
                .Allows("blogArticle")
                .Build(),
            Type("blogArticle", "Blog Article")
                .Properties(("title", TextBox), ("body", RichText), ("publishDate", DateTimeEditor), ("publisherName", TextBox))
                .Build(),
            Type("departmentsHub", "Departments Hub")
                .Properties(("hubTitle", TextBox))
                .Allows("departmentPage", "officePage")
                .Build(),
            Type("departmentPage", "Department Page")
                .Properties(("deptName", TextBox), ("deptDescription", TextArea))
                .Build(),
            Type("officePage", "Office Page")
                .Properties(("officeName", TextBox), ("officeAddress", TextBox))
                .Build());
    }

    private OracleTypeSafeClient ProductOracle(double categoryConfidence = 0.9) => new(SharedSchemaRegistry.Graph,
        ExpectedMapping.Property("Name", "productName"),
        ExpectedMapping.Property("Description", "description"),
        ExpectedMapping.CrossNode("Category", "parent", "productListing", "title", categoryConfidence));

    [Fact]
    public async Task Product_Category_ComesFromTheParentListingsTitle()
    {
        SetUpSite();

        var rows = await CreateMapper(ProductOracle()).MapAsync("productPage", "Product", []);

        var category = Row(rows, "Category");
        category.SuggestedSourceType.Should().Be(SchemeWeaverConstants.SourceTypes.Parent);
        category.SuggestedContentTypePropertyAlias.Should().Be("title");
        category.SuggestedSourceContentTypeAlias.Should().Be("productListing", "the row names the neighbour type it reads from");
        category.EditorAlias.Should().Be(TextBox, "the editor comes from the neighbour's real property");
        category.Confidence.Should().Be(90, "the calibrated cross-node answer, not a heuristic tier");
        category.IsAutoMapped.Should().BeTrue();
        category.SuggestedNestedSchemaTypeName.Should().BeNull();
        category.SuggestedResolverConfig.Should().BeNull();
        Row(rows, "Name").SuggestedSourceType.Should().Be("property", "local rows are untouched by the cross-node round");
    }

    [Fact]
    public async Task BlogPosting_Publisher_ComesFromAnAncestorAtDepthTwo()
    {
        SetUpSite();
        var oracle = new OracleTypeSafeClient(SharedSchemaRegistry.Graph,
            ExpectedMapping.Property("Headline", "title", 0.91),
            ExpectedMapping.CrossNode("Publisher", "ancestor", "homePage", "organisationName", 0.88));

        var rows = await CreateMapper(oracle).MapAsync("blogArticle", "BlogPosting", []);

        var publisher = Row(rows, "Publisher");
        publisher.SuggestedSourceType.Should().Be(SchemeWeaverConstants.SourceTypes.Ancestor);
        publisher.SuggestedSourceContentTypeAlias.Should().Be("homePage");
        publisher.SuggestedContentTypePropertyAlias.Should().Be("organisationName");
        publisher.Confidence.Should().Be(88);

        // The home page is two levels up (blogListing is the parent), so it is offered as an
        // ancestor and the option text says so: the core's ancestor resolver walks up to the
        // nearest node of that alias, which is what the model was told it would do.
        var question = oracle.Inner.AskedQuestions["cross__Publisher"];
        var criteria = FakeTypeSafeClient.CriteriaOf(question);
        criteria.Should().ContainKey("ancestor__homePage__organisationName")
            .WhoseValue.Should().Contain("depth 2");
        criteria.Keys.Should().NotContain("parent__homePage__organisationName", "a type is presented under one relation only");
    }

    [Fact]
    public async Task Organization_Location_ComesFromASibling_AlwaysCarryingTheAlias()
    {
        SetUpSite();
        var oracle = new OracleTypeSafeClient(SharedSchemaRegistry.Graph,
            ExpectedMapping.Property("Name", "deptName"),
            ExpectedMapping.CrossNode("Location", "sibling", "officePage", "officeAddress", 0.86));

        var rows = await CreateMapper(oracle).MapAsync("departmentPage", "Organization", []);

        var location = Row(rows, "Location");
        location.SuggestedSourceType.Should().Be(SchemeWeaverConstants.SourceTypes.Sibling);
        location.SuggestedContentTypePropertyAlias.Should().Be("officeAddress");
        // A sibling row without the alias would read the FIRST sibling of any type at render
        // time; the alias is what makes "the office page beside this department" resolvable.
        location.SuggestedSourceContentTypeAlias.Should().Be("officePage");
        location.Confidence.Should().Be(86);
    }

    // -----------------------------------------------------------------------
    // Which properties are asked is a rule, not a judgment
    // -----------------------------------------------------------------------

    [Fact]
    public async Task SelfOnlyProperties_AreNeverAskedCrossNode()
    {
        SetUpSite();
        // Headline is bound locally; description, datePublished, name and url are NOT, and a
        // neighbour has text and date properties that could plausibly feed them.
        var oracle = new OracleTypeSafeClient(SharedSchemaRegistry.Graph,
            ExpectedMapping.Property("Headline", "title", 0.9));

        await CreateMapper(oracle).MapAsync("blogArticle", "BlogPosting", []);

        var crossIds = oracle.Inner.AskedIds.Where(id => id.StartsWith("cross__", StringComparison.Ordinal)).ToList();
        crossIds.Should().NotBeEmpty("the site has neighbours, so the round runs");
        foreach (var selfOnly in CrossNodeBinder.SelfOnlySchemaProperties)
            crossIds.Should().NotContain(id => string.Equals(id, "cross__" + selfOnly, StringComparison.OrdinalIgnoreCase),
                $"{selfOnly} must come from the page itself, never from a related page");
        crossIds.Should().NotContain("cross__DatePublished").And.NotContain("cross__Description").And.NotContain("cross__Name");
    }

    [Fact]
    public async Task LocallyClaimedProperties_EvenBelowTheBindingFloor_AreNeverAskedCrossNode()
    {
        SetUpSite();
        _options.MinBindingConfidence = 75;
        // The page's own publisherName claims Publisher at 0.5, under the floor: the row is
        // dropped, but the page DOES supply a publisher, so the round must not offer the home
        // page's organisation name as a substitute (the oracle would say yes if asked).
        var oracle = new OracleTypeSafeClient(SharedSchemaRegistry.Graph,
            ExpectedMapping.Property("Headline", "title", 0.9),
            ExpectedMapping.Property("Publisher", "publisherName", 0.5),
            ExpectedMapping.CrossNode("Publisher", "ancestor", "homePage", "organisationName", 0.95));

        var rows = await CreateMapper(oracle).MapAsync("blogArticle", "BlogPosting", []);

        rows.Should().NotContain(r => r.SchemaPropertyName == "Publisher", "the local claim was under the floor and no cross-node substitute is made");
        oracle.Inner.AskedIds.Should().NotContain("cross__Publisher");
        oracle.Inner.AskedIds.Should().Contain(id => id.StartsWith("cross__", StringComparison.Ordinal), "other unbound properties are still asked");
    }

    // -----------------------------------------------------------------------
    // Which options are offered, and in what order
    // -----------------------------------------------------------------------

    [Fact]
    public async Task Options_DecodeToRealNeighbourProperties_NoneFirst_UnderTheCap()
    {
        SetUpSite();
        var oracle = ProductOracle();

        await CreateMapper(oracle).MapAsync("productPage", "Product", []);

        var question = oracle.Inner.AskedQuestions["cross__Category"];
        var keys = OptionKeys(question);

        keys[0].Should().Be(JudgmentSession.None, "the default option is listed first when the default is \"no\"");
        keys.Count.Should().BeLessThanOrEqualTo(255, "the API allows at most 255 options per Choice");
        keys.Count.Should().BeLessThanOrEqualTo(_options.MaxNeighbourProperties + 1);

        // Every offered option is a real (relation, type, property) triple of the fixture.
        var legal = new HashSet<string>(StringComparer.Ordinal);
        void Neighbour(string relation, string alias, params string[] properties)
        {
            foreach (var p in properties.Concat(["__name", "__url"]))
                legal.Add(JudgmentSession.Id(relation, alias, p));
        }

        Neighbour("parent", "productListing", "title", "intro");
        Neighbour("ancestor", "homePage", "organisationName", "siteName", "logo");
        Neighbour("sibling", "promoPage", "promoText");
        keys.Skip(1).Should().OnlyContain(k => legal.Contains(k), "no option may name a property that does not exist on that neighbour");

        keys.Should().Contain("parent__productListing__title")
            .And.Contain("ancestor__homePage__organisationName")
            .And.Contain("sibling__promoPage__promoText");
        keys.Should().NotContain(k => k.EndsWith("__featured", StringComparison.Ordinal), "a content picker holds a reference, not a scalar");
        keys.Should().NotContain(k => k.EndsWith("__footerBlocks", StringComparison.Ordinal), "a block editor on a related page is never one value of this page");
        keys.Should().NotContain(k => k.Contains("__productPage__", StringComparison.Ordinal), "the type itself is never its own neighbour");

        // Option order is the neighbourhood's order: parents, then ancestors, then siblings.
        var parentIndex = keys.IndexOf("parent__productListing__title");
        var ancestorIndex = keys.IndexOf("ancestor__homePage__organisationName");
        var siblingIndex = keys.IndexOf("sibling__promoPage__promoText");
        parentIndex.Should().BeLessThan(ancestorIndex);
        ancestorIndex.Should().BeLessThan(siblingIndex);
    }

    // -----------------------------------------------------------------------
    // The bar
    // -----------------------------------------------------------------------

    [Fact]
    public async Task RowsBelowMinCrossNodeConfidence_AreDropped()
    {
        SetUpSite();

        var rows = await CreateMapper(ProductOracle(categoryConfidence: 0.55)).MapAsync("productPage", "Product", []);

        rows.Should().NotContain(r => r.SchemaPropertyName == "Category",
            "55 is under the default cross-node bar of 60, the core's show bar: a cross-node row is offered like any other suggestion");
    }

    [Fact]
    public async Task RowsBelowARaisedMinCrossNodeConfidence_AreDropped()
    {
        SetUpSite();
        _options.MinCrossNodeConfidence = 80;

        var rows = await CreateMapper(ProductOracle(categoryConfidence: 0.7)).MapAsync("productPage", "Product", []);

        rows.Should().NotContain(r => r.SchemaPropertyName == "Category",
            "a site that would rather see fewer cross-node rows raises the bar, and 70 is under 80");
    }

    [Theory]
    [InlineData(0.7, false)]
    [InlineData(0.85, true)]
    public async Task CrossNodeRows_AboveTheBar_FollowTheCoreThresholdsForIsAutoMapped(double confidence, bool expectedAutoMapped)
    {
        SetUpSite();
        _options.MinCrossNodeConfidence = 60;

        var rows = await CreateMapper(ProductOracle(confidence)).MapAsync("productPage", "Product", []);

        var category = Row(rows, "Category");
        category.Confidence.Should().Be((int)Math.Round(confidence * 100));
        category.IsAutoMapped.Should().Be(expectedAutoMapped, "pre-ticking follows the core auto-apply bar of 80, exactly like a local row");
    }

    // -----------------------------------------------------------------------
    // Discovery: nothing, structure, observed tree
    // -----------------------------------------------------------------------

    [Fact]
    public async Task EmptyStructure_WithNoObservedServices_AsksNothing_AndTheStateCarriesNoNeighbourhood()
    {
        Structure(_contentTypes, Type("landingPage", "Landing Page").Properties(("title", TextBox)).Build());
        var oracle = new OracleTypeSafeClient(SharedSchemaRegistry.Graph,
            ExpectedMapping.Property("Headline", "title", 0.9),
            ExpectedMapping.CrossNode("Publisher", "ancestor", "homePage", "organisationName"));

        var rows = await CreateMapper(oracle).MapAsync("landingPage", "BlogPosting", []);

        Row(rows, "Headline").Confidence.Should().Be(90);
        rows.Should().NotContain(r => r.SchemaPropertyName == "Publisher");
        oracle.Inner.AskedIds.Should().NotContain(id => id.StartsWith("cross__", StringComparison.Ordinal));
        oracle.Inner.Requests.Should().NotBeEmpty().And.OnlyContain(r => !HasNeighbourhood(r.State),
            "with nothing to offer, no request pays for a neighbourhood node");
        _provider.Received().GetService(typeof(IContentService));
        _provider.Received().GetService(typeof(IEntityService));
    }

    [Fact]
    public async Task ObservedTree_FindsParentsAncestorsAndSiblings_WhenTheStructureIsEmpty()
    {
        // The same types as the site, but with no allowed-children declarations at all: a
        // loosely modelled site. Only the real tree can say what sits around a product page.
        Structure(_contentTypes,
            Type("homePage", "Home Page").Properties(("organisationName", TextBox), ("siteName", TextBox)).Build(),
            Type("productListing", "Product Listing").Properties(("title", TextBox), ("intro", TextArea)).Build(),
            Type("productPage", "Product Page").Properties(("productName", TextBox), ("description", TextArea)).Build(),
            Type("promoPage", "Promo Page").Properties(("promoText", TextBox)).Build());

        const int homeId = 1400, listingId = 1500, productAId = 2001, productBId = 2002, promoId = 2003;
        var nodes = new[]
        {
            Node(productAId, listingId, $"-1,{homeId},{listingId},{productAId}"),
            Node(productBId, listingId, $"-1,{homeId},{listingId},{productBId}"),
        };
        var contentService = Substitute.For<IContentService>();
        contentService.GetPagedOfTypes(Arg.Any<int[]>(), Arg.Any<long>(), Arg.Any<int>(), out Arg.Any<long>(), Arg.Any<IQuery<IContent>?>(), Arg.Any<Ordering?>())
            .Returns(nodes);
        // Doubles are built before the stubs that return them: Slim() configures substitutes
        // itself and would otherwise steal NSubstitute's "last call".
        var home = Slim(homeId, "homePage");
        var listing = Slim(listingId, "productListing");
        var productA = Slim(productAId, "productPage");
        var productB = Slim(productBId, "productPage");
        var promo = Slim(promoId, "promoPage");
        var entityService = Substitute.For<IEntityService>();
        entityService.GetChildren(listingId, UmbracoObjectTypes.Document).Returns([productA, productB, promo]);
        entityService.GetAll(UmbracoObjectTypes.Document, Arg.Any<int[]>()).Returns([home, listing, productA, productB, promo]);
        _provider.GetService(typeof(IContentService)).Returns(contentService);
        _provider.GetService(typeof(IEntityService)).Returns(entityService);

        var oracle = ProductOracle();
        var rows = await CreateMapper(oracle).MapAsync("productPage", "Product", []);

        var keys = OptionKeys(oracle.Inner.AskedQuestions["cross__Category"]);
        keys.Should().Contain("parent__productListing__title", "the sampled nodes' actual parent")
            .And.Contain("ancestor__homePage__organisationName", "the node above the parent, from the path")
            .And.Contain("sibling__promoPage__promoText", "the other child of the parent");
        keys.Should().NotContain(k => k.Contains("__productPage__", StringComparison.Ordinal), "the type itself is never its own neighbour, even when it sits beside itself");

        var category = Row(rows, "Category");
        category.SuggestedSourceType.Should().Be(SchemeWeaverConstants.SourceTypes.Parent);
        category.SuggestedSourceContentTypeAlias.Should().Be("productListing");
        contentService.Received(1).GetPagedOfTypes(
            Arg.Is<int[]>(ids => ids.Single() == IdFor("productPage")), 0, _options.MaxSampledNodes, out Arg.Any<long>(), Arg.Any<IQuery<IContent>?>(), Arg.Any<Ordering?>());
    }

    [Fact]
    public async Task Discovery_ExcludesSelfElementTypesAndCycles_UnderTheDepthCap()
    {
        // homePage -> sectionPage -> sectionPage (self-nesting) / productPage / faqItem (an
        // element type); productPage -> sectionPage closes a cycle. A naive walk never ends.
        var homePage = Type("homePage", "Home Page").Properties(("organisationName", TextBox)).AllowedAsRoot().Allows("sectionPage").Build();
        var sectionPage = Type("sectionPage", "Section Page").Properties(("sectionTitle", TextBox)).Allows("sectionPage", "productPage", "faqItem").Build();
        var productPage = Type("productPage", "Product Page").Properties(("productName", TextBox)).Allows("sectionPage").Build();
        var faqItem = Type("faqItem", "FAQ Item").Properties(("question", TextBox)).IsElement().Build();
        Structure(_contentTypes, homePage, sectionPage, productPage, faqItem);
        _options.NeighbourhoodDiscovery = TypeSafeNeighbourhoodDiscovery.Structure;
        _options.MaxAncestorDepth = 2;

        var neighbourhood = await ContentTypeNeighbourhood.DiscoverAsync(
            productPage, _contentTypes, _provider, _options, _logger, CancellationToken.None);

        neighbourhood.IsEmpty.Should().BeFalse();
        neighbourhood.Parents.Select(p => p.Alias).Should().Equal("sectionPage");
        neighbourhood.Ancestors.Select(a => (a.Alias, a.Depth)).Should().Equal(("homePage", 2));
        neighbourhood.Siblings.Should().BeEmpty("sectionPage is already the parent and faqItem is an element type");
        neighbourhood.All.Select(t => t.Alias).Should().NotContain("productPage").And.NotContain("faqItem");
        neighbourhood.AllowedAsRoot.Should().BeFalse();
        neighbourhood.Parents[0].Properties.Select(p => p.Alias).Should().Equal(["sectionTitle", "__name", "__url"],
            "a parent's name is the most common cross-node source there is and option order was measured to matter, so __name leads __url");
        neighbourhood.Parents[0].SourceType.Should().Be(SchemeWeaverConstants.SourceTypes.Parent);
        neighbourhood.Ancestors[0].SourceType.Should().Be(SchemeWeaverConstants.SourceTypes.Ancestor);

        // Depth 1 collects parents only; the cap is honoured rather than the cycle followed.
        _options.MaxAncestorDepth = 1;
        var shallow = await ContentTypeNeighbourhood.DiscoverAsync(productPage, _contentTypes, _provider, _options, _logger, CancellationToken.None);
        shallow.Parents.Select(p => p.Alias).Should().Equal("sectionPage");
        shallow.Ancestors.Should().BeEmpty();
    }

    // -----------------------------------------------------------------------
    // The neighbourhood is paid for once
    // -----------------------------------------------------------------------

    [Fact]
    public async Task NeighbourhoodNode_AppearsOnlyOnCrossNodeRequests()
    {
        SetUpSite();
        var oracle = ProductOracle();

        await CreateMapper(oracle).MapAsync("productPage", "Product", []);

        var crossRequests = oracle.Inner.Requests.Where(r => r.Questions.Keys.All(k => k.StartsWith("cross__", StringComparison.Ordinal))).ToList();
        var localRequests = oracle.Inner.Requests.Except(crossRequests).ToList();
        crossRequests.Should().NotBeEmpty();
        localRequests.Should().NotBeEmpty();
        crossRequests.Should().OnlyContain(r => HasNeighbourhood(r.State), "the cross-node round needs the neighbourhood in its state");
        localRequests.Should().OnlyContain(r => !HasNeighbourhood(r.State), "bind, shape, descent and inner requests must not pay for it");

        using var doc = JsonDocument.Parse(JsonSerializer.Serialize(crossRequests[0].State));
        var neighbourhood = doc.RootElement.GetProperty("neighbourhood");
        neighbourhood.GetProperty("parents").EnumerateArray().Select(p => p.GetProperty("alias").GetString()).Should().Equal("productListing");
        neighbourhood.GetProperty("ancestors").EnumerateArray().Select(p => p.GetProperty("alias").GetString()).Should().Equal("homePage");
        neighbourhood.GetProperty("siblings").EnumerateArray().Select(p => p.GetProperty("alias").GetString()).Should().Equal("promoPage");
        neighbourhood.GetProperty("allowedAsRoot").GetBoolean().Should().BeFalse();
    }

    [Fact]
    public async Task EnableCrossNodeSourcesFalse_ProducesV1RequestIds_AndSkipsDiscovery()
    {
        SetUpSite();
        _options.EnableCrossNodeSources = false;
        var oracle = ProductOracle();

        var rows = await CreateMapper(oracle).MapAsync("productPage", "Product", []);

        rows.Should().NotContain(r => r.SchemaPropertyName == "Category");
        oracle.Inner.AskedIds.Should().NotBeEmpty().And.OnlyContain(id =>
            id.StartsWith("bind__", StringComparison.Ordinal) || id.StartsWith("shape__", StringComparison.Ordinal)
            || id.StartsWith("entity__", StringComparison.Ordinal) || id.StartsWith("root__", StringComparison.Ordinal)
            || id.StartsWith("desc__", StringComparison.Ordinal) || id.StartsWith("inner__", StringComparison.Ordinal)
            || id.StartsWith("strlist__", StringComparison.Ordinal));
        oracle.Inner.Requests.Should().OnlyContain(r => !HasNeighbourhood(r.State));
        _contentTypes.DidNotReceive().GetAll();
        _provider.DidNotReceive().GetService(typeof(IContentService));
    }

    // -----------------------------------------------------------------------
    // Never invents
    // -----------------------------------------------------------------------

    [Fact]
    public async Task EveryEmittedAlias_IsReal_IncludingNeighbourAliases()
    {
        SetUpSite();
        var oracle = new OracleTypeSafeClient(SharedSchemaRegistry.Graph,
            ExpectedMapping.Property("Headline", "title", 0.9),
            ExpectedMapping.CrossNode("Publisher", "ancestor", "homePage", "organisationName", 0.9),
            ExpectedMapping.CrossNode("IsPartOf", "parent", "blogListing", "__name", 0.9));
        var local = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "title", "body", "publishDate", "publisherName", "__url", "__name", "__createDate", "__updateDate",
        };
        var neighbours = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase)
        {
            ["blogListing"] = new(["title", "description", "__name", "__url"], StringComparer.OrdinalIgnoreCase),
            ["homePage"] = new(["organisationName", "siteName", "logo", "__name", "__url"], StringComparer.OrdinalIgnoreCase),
        };

        var rows = await CreateMapper(oracle).MapAsync("blogArticle", "BlogPosting", []);

        rows.Should().Contain(r => r.SchemaPropertyName == "Publisher").And.Contain(r => r.SchemaPropertyName == "IsPartOf");
        foreach (var row in rows)
        {
            if (SchemeWeaverConstants.SourceTypes.IsCrossNode(row.SuggestedSourceType))
            {
                row.SuggestedSourceContentTypeAlias.Should().NotBeNullOrEmpty();
                neighbours.Should().ContainKey(row.SuggestedSourceContentTypeAlias!);
                neighbours[row.SuggestedSourceContentTypeAlias!].Should().Contain(row.SuggestedContentTypePropertyAlias!);
            }
            else
            {
                row.SuggestedSourceContentTypeAlias.Should().BeNull("only cross-node rows name a related type");
                local.Should().Contain(row.SuggestedContentTypePropertyAlias!);
            }

            if (row.SuggestedResolverConfig is null)
                continue;

            var config = JsonNode.Parse(row.SuggestedResolverConfig)!.AsObject();
            foreach (var inner in config["complexTypeMappings"]?.AsArray() ?? [])
                local.Should().Contain(inner!["contentTypePropertyAlias"]!.GetValue<string>());
        }
    }

    // -----------------------------------------------------------------------
    // Observed discovery stays cheap on a large site
    // -----------------------------------------------------------------------

    [Fact]
    public async Task ObservedTree_ReadsSiblingTypesOffTheChildren_AndResolvesOnlyParentAndAncestorIds()
    {
        Structure(_contentTypes,
            Type("homePage", "Home Page").Properties(("organisationName", TextBox)).Build(),
            Type("productListing", "Product Listing").Properties(("title", TextBox)).Build(),
            Type("productPage", "Product Page").Properties(("productName", TextBox)).Build(),
            Type("promoPage", "Promo Page").Properties(("promoText", TextBox)).Build());
        _options.NeighbourhoodDiscovery = TypeSafeNeighbourhoodDiscovery.Observed;
        const int homeId = 1400, listingId = 1500, productAId = 2001, productBId = 2002, promoId = 2003;
        var (_, entityService) = ObservedTree(
            Node(productAId, listingId, $"-1,{homeId},{listingId},{productAId}"),
            Node(productBId, listingId, $"-1,{homeId},{listingId},{productBId}"));
        var children = new[] { Slim(productAId, "productPage"), Slim(productBId, "productPage"), Slim(promoId, "promoPage") };
        // The lookup can only answer for the parent and the ancestor: the promo page's type has
        // to come from the child entity GetChildren returned, never from a second round trip.
        var relatives = new[] { Slim(homeId, "homePage"), Slim(listingId, "productListing") };
        entityService.GetChildren(listingId, UmbracoObjectTypes.Document).Returns(children);
        entityService.GetAll(UmbracoObjectTypes.Document, Arg.Any<int[]>()).Returns(relatives);

        var neighbourhood = await Discover("productPage");

        neighbourhood.Parents.Select(p => p.Alias).Should().Equal("productListing");
        neighbourhood.Ancestors.Select(a => (a.Alias, a.Depth)).Should().Equal(("homePage", 2));
        neighbourhood.Siblings.Select(s => s.Alias).Should().Equal(["promoPage"], "a sibling's type is read off the child entity itself");
        // Two sampled nodes under one parent share one child read.
        entityService.Received(1).GetChildren(listingId, UmbracoObjectTypes.Document);
        // Only the parent and the ancestor are looked up; a listing's children never become SQL parameters.
        entityService.Received(1).GetAll(UmbracoObjectTypes.Document,
            Arg.Is<int[]>(ids => ids.Length == 2 && ids.Contains(listingId) && ids.Contains(homeId)));
        entityService.DidNotReceive().GetAll(UmbracoObjectTypes.Document,
            Arg.Is<int[]>(ids => ids.Contains(promoId) || ids.Contains(productAId) || ids.Contains(productBId)));
    }

    [Fact]
    public async Task ObservedTree_ResolvesRelativeIdsInGroupsNoWiderThanTheSqlParameterLimit()
    {
        Structure(_contentTypes,
            Type("sectionPage", "Section Page").Properties(("sectionTitle", TextBox)).Build(),
            Type("productPage", "Product Page").Properties(("productName", TextBox)).Build());
        _options.NeighbourhoodDiscovery = TypeSafeNeighbourhoodDiscovery.Observed;
        _options.MaxAncestorDepth = 3;
        // 667 sampled pages, each under its own parent, grandparent and great-grandparent: 2001
        // relative ids, one more than EntityRepository.GetAll may bind in a single query on SQL
        // Server (it binds one parameter per id and does not group them itself).
        const int limit = Umbraco.Cms.Core.Constants.Sql.MaxParameterCount;
        const int sampled = 667;
        _options.MaxSampledNodes = sampled;
        var nodes = Enumerable.Range(0, sampled)
            .Select(i => Node(100_000 + i, 200_000 + i, $"-1,{400_000 + i},{300_000 + i},{200_000 + i},{100_000 + i}"))
            .ToArray();
        var (_, entityService) = ObservedTree(nodes);
        entityService.GetChildren(Arg.Any<int>(), UmbracoObjectTypes.Document).Returns(Array.Empty<IEntitySlim>());
        entityService.GetAll(UmbracoObjectTypes.Document, Arg.Any<int[]>()).Returns(Array.Empty<IEntitySlim>());

        await Discover("productPage");

        var lookups = entityService.ReceivedCalls()
            .Where(c => c.GetMethodInfo().Name == nameof(IEntityService.GetAll))
            .Select(c => (int[])c.GetArguments()[1]!)
            .ToList();
        lookups.Should().HaveCount(2, "2001 ids need two groups of at most {0}", limit);
        lookups.Should().OnlyContain(ids => ids.Length <= limit,
            "a group wider than the parameter limit throws on SQL Server, and the boundary catch would then silently drop every observed neighbour");
        lookups.SelectMany(ids => ids).Should().OnlyHaveUniqueItems().And.HaveCount(3 * sampled,
            "every parent, grandparent and great-grandparent is resolved exactly once");
    }

    // -----------------------------------------------------------------------
    // Observed siblings: singletons that enough sampled nodes have beside them
    // -----------------------------------------------------------------------

    /// <summary>The blog types with no allowed-children declarations: only the tree can say what sits beside an article.</summary>
    private void SetUpLooseBlogTypes()
        => Structure(_contentTypes,
            Type("homePage", "Home Page").Properties(("organisationName", TextBox)).Build(),
            Type("blogListing", "Blog Listing").Properties(("title", TextBox)).Build(),
            Type("blogArticle", "Blog Article").Properties(("title", TextBox)).Build(),
            Type("newsArticle", "News Article").Properties(("authorName", TextBox)).Build(),
            Type("contactPage", "Contact Page").Properties(("address", TextBox)).Build(),
            Type("officePage", "Office Page").Properties(("officeName", TextBox)).Build());

    [Fact]
    public async Task ObservedSiblings_RepeatedUnderASampledParent_AreNotOffered_ASingletonIs()
    {
        SetUpLooseBlogTypes();
        _options.NeighbourhoodDiscovery = TypeSafeNeighbourhoodDiscovery.Observed;
        const int homeId = 1400, listingId = 1500, articleAId = 2001, articleBId = 2002, newsAId = 2003, newsBId = 2004, contactId = 2005;
        var (_, entityService) = ObservedTree(
            Node(articleAId, listingId, $"-1,{homeId},{listingId},{articleAId}"),
            Node(articleBId, listingId, $"-1,{homeId},{listingId},{articleBId}"));
        var children = new[]
        {
            Slim(articleAId, "blogArticle"), Slim(articleBId, "blogArticle"),
            Slim(newsAId, "newsArticle"), Slim(newsBId, "newsArticle"), Slim(contactId, "contactPage"),
        };
        var relatives = new[] { Slim(homeId, "homePage"), Slim(listingId, "blogListing") };
        entityService.GetChildren(listingId, UmbracoObjectTypes.Document).Returns(children);
        entityService.GetAll(UmbracoObjectTypes.Document, Arg.Any<int[]>()).Returns(relatives);

        var neighbourhood = await Discover("blogArticle");

        neighbourhood.Siblings.Select(s => s.Alias).Should().Equal(["contactPage"],
            "a sibling row reads the FIRST sibling of its type at render time, which names a page only when there is exactly one: "
            + "with two news articles beside every post, newsArticle.authorName would read an arbitrary article");
        neighbourhood.Parents.Select(p => p.Alias).Should().Equal(["blogListing"], "parents are unaffected by the sibling rule");
    }

    [Fact]
    public async Task ObservedSiblings_RepeatedUnderAnyOneSampledParent_AreNotOffered()
    {
        SetUpLooseBlogTypes();
        _options.NeighbourhoodDiscovery = TypeSafeNeighbourhoodDiscovery.Observed;
        // One news article beside article A (under listing 1), two beside article B (under listing 2).
        const int homeId = 1400, listing1Id = 1501, listing2Id = 1502, articleAId = 2001, articleBId = 2002,
            news1Id = 2003, news2Id = 2004, news3Id = 2005, contact1Id = 2006, contact2Id = 2007;
        var (_, entityService) = ObservedTree(
            Node(articleAId, listing1Id, $"-1,{homeId},{listing1Id},{articleAId}"),
            Node(articleBId, listing2Id, $"-1,{homeId},{listing2Id},{articleBId}"));
        var children1 = new[] { Slim(articleAId, "blogArticle"), Slim(news1Id, "newsArticle"), Slim(contact1Id, "contactPage") };
        var children2 = new[] { Slim(articleBId, "blogArticle"), Slim(news2Id, "newsArticle"), Slim(news3Id, "newsArticle"), Slim(contact2Id, "contactPage") };
        var relatives = new[] { Slim(homeId, "homePage"), Slim(listing1Id, "blogListing"), Slim(listing2Id, "blogListing") };
        entityService.GetChildren(listing1Id, UmbracoObjectTypes.Document).Returns(children1);
        entityService.GetChildren(listing2Id, UmbracoObjectTypes.Document).Returns(children2);
        entityService.GetAll(UmbracoObjectTypes.Document, Arg.Any<int[]>()).Returns(relatives);

        var neighbourhood = await Discover("blogArticle");

        neighbourhood.Siblings.Select(s => s.Alias).Should().Equal(["contactPage"],
            "the rule is at most once under EVERY sampled parent: one listing holding two news articles is enough to veto the type");
    }

    [Fact]
    public async Task ObservedSiblings_MustClearMinObservedShare_LikeParentsAndAncestors()
    {
        SetUpLooseBlogTypes();
        _options.NeighbourhoodDiscovery = TypeSafeNeighbourhoodDiscovery.Observed;
        _options.MinObservedShare = 0.5;
        // Three articles under listing 1 with a contact page beside them, one under listing 2
        // with an office page beside it: the office page is a one-off neighbour of one article.
        const int homeId = 1400, listing1Id = 1501, listing2Id = 1502, articleAId = 2001, articleBId = 2002, articleCId = 2003, articleDId = 2004,
            contactId = 2005, officeId = 2006;
        var (_, entityService) = ObservedTree(
            Node(articleAId, listing1Id, $"-1,{homeId},{listing1Id},{articleAId}"),
            Node(articleBId, listing1Id, $"-1,{homeId},{listing1Id},{articleBId}"),
            Node(articleCId, listing1Id, $"-1,{homeId},{listing1Id},{articleCId}"),
            Node(articleDId, listing2Id, $"-1,{homeId},{listing2Id},{articleDId}"));
        var children1 = new[] { Slim(articleAId, "blogArticle"), Slim(articleBId, "blogArticle"), Slim(articleCId, "blogArticle"), Slim(contactId, "contactPage") };
        var children2 = new[] { Slim(articleDId, "blogArticle"), Slim(officeId, "officePage") };
        var relatives = new[] { Slim(homeId, "homePage"), Slim(listing1Id, "blogListing"), Slim(listing2Id, "blogListing") };
        entityService.GetChildren(listing1Id, UmbracoObjectTypes.Document).Returns(children1);
        entityService.GetChildren(listing2Id, UmbracoObjectTypes.Document).Returns(children2);
        entityService.GetAll(UmbracoObjectTypes.Document, Arg.Any<int[]>()).Returns(relatives);

        var neighbourhood = await Discover("blogArticle");

        neighbourhood.Siblings.Select(s => s.Alias).Should().Equal(["contactPage"],
            "a type beside 1 of 4 sampled nodes is under the 0.5 share, exactly as a parent type seen on 1 of 4 would be");
        // Three sampled nodes under listing 1 still read its children once.
        entityService.Received(1).GetChildren(listing1Id, UmbracoObjectTypes.Document);

        // Lower the bar and the one-off neighbour is offered too, after the better-supported one.
        _options.MinObservedShare = 0.25;
        var lenient = await Discover("blogArticle");
        lenient.Siblings.Select(s => s.Alias).Should().Equal(["contactPage", "officePage"],
            "at a 0.25 share one node in four is enough, and the most-observed type leads");
    }

    [Fact]
    public async Task DeclaredSiblings_StayOffered_WhateverTheObservedTreeShows()
    {
        // blogListing declares newsArticle beside blogArticle; the tree shows two per listing.
        // Structure cannot know how many there will be, so under Both the declaration stands.
        Structure(_contentTypes,
            Type("blogListing", "Blog Listing").Properties(("title", TextBox)).Allows("blogArticle", "newsArticle").Build(),
            Type("blogArticle", "Blog Article").Properties(("title", TextBox)).Build(),
            Type("newsArticle", "News Article").Properties(("authorName", TextBox)).Build());
        _options.NeighbourhoodDiscovery = TypeSafeNeighbourhoodDiscovery.Both;
        const int listingId = 1500, articleId = 2001, newsAId = 2002, newsBId = 2003;
        var (_, entityService) = ObservedTree(Node(articleId, listingId, $"-1,{listingId},{articleId}"));
        var children = new[] { Slim(articleId, "blogArticle"), Slim(newsAId, "newsArticle"), Slim(newsBId, "newsArticle") };
        var relatives = new[] { Slim(listingId, "blogListing") };
        entityService.GetChildren(listingId, UmbracoObjectTypes.Document).Returns(children);
        entityService.GetAll(UmbracoObjectTypes.Document, Arg.Any<int[]>()).Returns(relatives);

        var neighbourhood = await Discover("blogArticle");

        neighbourhood.Siblings.Select(s => s.Alias).Should().Equal(["newsArticle"],
            "the multiplicity rule gates what observation alone may offer; a declared sibling is the site's own statement of intent");
    }

    // -----------------------------------------------------------------------
    // A parent row is only a parent row when the parent type is unique
    // -----------------------------------------------------------------------

    [Fact]
    public async Task ParentRow_WhenSeveralParentTypesAreOffered_IsEmittedAsAncestorNamingTheChosenType()
    {
        // productPage is allowed under both productListing and saleListing. A `parent` row reads
        // whichever page is the actual parent and the core ignores the type alias on it.
        Structure(_contentTypes,
            Type("productListing", "Product Listing").Properties(("title", TextBox)).Allows("productPage").Build(),
            Type("saleListing", "Sale Listing").Properties(("saleName", TextBox)).Allows("productPage").Build(),
            Type("productPage", "Product Page").Properties(("productName", TextBox), ("description", TextArea)).Build());
        var oracle = ProductOracle();

        var rows = await CreateMapper(oracle).MapAsync("productPage", "Product", []);

        var category = Row(rows, "Category");
        category.SuggestedSourceType.Should().Be(SchemeWeaverConstants.SourceTypes.Ancestor,
            "an ancestor row honours the type alias and Umbraco walks ancestors nearest-first, so it reads the actual parent only when that parent is a product listing and never a sale listing's title");
        category.SuggestedSourceContentTypeAlias.Should().Be("productListing");
        category.SuggestedContentTypePropertyAlias.Should().Be("title");
        category.Confidence.Should().Be(90);
        OptionKeys(oracle.Inner.AskedQuestions["cross__Category"]).Should().Contain("parent__productListing__title").And.Contain("parent__saleListing__saleName",
            "the model is still shown both as parents, which is what they are; only the emitted row changes");
    }

    [Fact]
    public async Task ParentRow_WhenTheParentTypeIsUnique_StaysParent()
    {
        SetUpSite();

        var rows = await CreateMapper(ProductOracle()).MapAsync("productPage", "Product", []);

        Row(rows, "Category").SuggestedSourceType.Should().Be(SchemeWeaverConstants.SourceTypes.Parent,
            "with one parent type the actual parent is always that type, and a parent row is the cheaper read");
    }

    // -----------------------------------------------------------------------
    // Readable editors
    // -----------------------------------------------------------------------

    [Fact]
    public async Task EmailAddressAndOtherScalarEditors_OnAParentType_AreOffered()
    {
        Structure(_contentTypes,
            Type("departmentsHub", "Departments Hub")
                .Properties(("contactEmail", "Umbraco.EmailAddress"), ("region", "Umbraco.RadioButtonList"), ("staffCount", "Umbraco.Integer"), ("rating", "Umbraco.Decimal"), ("notes", Label))
                .Allows("departmentPage")
                .Build(),
            Type("departmentPage", "Department Page").Properties(("deptName", TextBox)).Build());
        var oracle = new OracleTypeSafeClient(SharedSchemaRegistry.Graph,
            ExpectedMapping.Property("Name", "deptName"),
            ExpectedMapping.CrossNode("Email", "parent", "departmentsHub", "contactEmail", 0.9));

        var rows = await CreateMapper(oracle).MapAsync("departmentPage", "Organization", []);

        var keys = OptionKeys(oracle.Inner.AskedQuestions["cross__Email"]);
        keys.Should().Contain("parent__departmentsHub__contactEmail", "an e-mail address editor holds exactly the scalar Organization.email wants")
            .And.Contain("parent__departmentsHub__region", "a radio-button selection arrives as a usable string")
            .And.Contain("parent__departmentsHub__staffCount", "email accepts text and a number is text at worst")
            .And.Contain("parent__departmentsHub__rating")
            .And.NotContain("parent__departmentsHub__notes", "a label holds nothing");
        var email = Row(rows, "Email");
        email.SuggestedSourceType.Should().Be(SchemeWeaverConstants.SourceTypes.Parent);
        email.SuggestedContentTypePropertyAlias.Should().Be("contactEmail");
        email.EditorAlias.Should().Be("Umbraco.EmailAddress");
    }

    [Fact]
    public async Task NumericSources_LandOnlyOnRangesThatAcceptANumberOrText()
    {
        Structure(_contentTypes,
            Type("newsHub", "News Hub").Properties(("editionNumber", "Umbraco.Integer"), ("editionName", TextBox)).Allows("newsItem").Build(),
            Type("newsItem", "News Item").Properties(("title", TextBox)).Build());
        _options.MaxCrossNodeQuestions = 500;
        var oracle = new OracleTypeSafeClient(SharedSchemaRegistry.Graph, ExpectedMapping.Property("Headline", "title", 0.9));

        await CreateMapper(oracle).MapAsync("newsItem", "Article", []);

        OptionKeys(oracle.Inner.AskedQuestions["cross__WordCount"]).Should().Contain("parent__newsHub__editionNumber", "wordCount accepts an Integer");
        OptionKeys(oracle.Inner.AskedQuestions["cross__Publisher"]).Should()
            .NotContain("parent__newsHub__editionNumber", "publisher accepts an Organization or a Person, which a number can never be")
            .And.Contain("parent__newsHub__editionName", "text goes anywhere, as before");
    }

    // -----------------------------------------------------------------------
    // Doubles for the observed tree
    // -----------------------------------------------------------------------

    private static IContent Node(int id, int parentId, string path)
    {
        var node = Substitute.For<IContent>();
        node.Id.Returns(id);
        node.ParentId.Returns(parentId);
        node.Path.Returns(path);
        node.Published.Returns(true);
        return node;
    }

    private static IEntitySlim Slim(int id, string contentTypeAlias)
    {
        var slim = Substitute.For<IDocumentEntitySlim>();
        slim.Id.Returns(id);
        slim.ContentTypeAlias.Returns(contentTypeAlias);
        return slim;
    }

    /// <summary>
    /// An observed tree whose sample is <paramref name="nodes"/>: the content and entity services
    /// are registered on the provider, and the caller stubs the entity service's children and
    /// lookups (doubles first, then stubs; see <see cref="Slim"/>).
    /// </summary>
    private (IContentService ContentService, IEntityService EntityService) ObservedTree(params IContent[] nodes)
    {
        var contentService = Substitute.For<IContentService>();
        contentService.GetPagedOfTypes(Arg.Any<int[]>(), Arg.Any<long>(), Arg.Any<int>(), out Arg.Any<long>(), Arg.Any<IQuery<IContent>?>(), Arg.Any<Ordering?>())
            .Returns(nodes);
        var entityService = Substitute.For<IEntityService>();
        _provider.GetService(typeof(IContentService)).Returns(contentService);
        _provider.GetService(typeof(IEntityService)).Returns(entityService);
        return (contentService, entityService);
    }

    /// <summary>Discovers the neighbourhood of the registered type <paramref name="alias"/> under the current options.</summary>
    private Task<ContentTypeNeighbourhood> Discover(string alias)
        => ContentTypeNeighbourhood.DiscoverAsync(_contentTypes.Get(alias)!, _contentTypes, _provider, _options, _logger, CancellationToken.None);
}
