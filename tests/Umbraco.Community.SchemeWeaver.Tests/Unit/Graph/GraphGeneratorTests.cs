using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Schema.NET;
using Umbraco.Cms.Core.Models.PublishedContent;
using Umbraco.Cms.Core.Routing;
using Umbraco.Community.SchemeWeaver.Graph;
using Xunit;

namespace Umbraco.Community.SchemeWeaver.Tests.Unit.Graph;

public class GraphGeneratorTests
{
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly IPublishedUrlProvider _urlProvider = Substitute.For<IPublishedUrlProvider>();
    private readonly ISiteSettingsResolver _siteSettingsResolver = Substitute.For<ISiteSettingsResolver>();

    public GraphGeneratorTests()
    {
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Scheme = "https";
        httpContext.Request.Host = new HostString("example.com");
        _httpContextAccessor = Substitute.For<IHttpContextAccessor>();
        _httpContextAccessor.HttpContext.Returns(httpContext);
    }

    private GraphGenerator Build(params IGraphPiece[] pieces) => new(
        pieces,
        _siteSettingsResolver,
        _httpContextAccessor,
        _urlProvider,
        NullLogger<GraphGenerator>.Instance);

    private IPublishedContent Page(string absoluteUrl = "https://example.com/about/")
    {
        var content = Substitute.For<IPublishedContent>();
        var type = Substitute.For<IPublishedContentType>();
        type.Alias.Returns("aboutPage");
        content.ContentType.Returns(type);
        content.Id.Returns(1);
        content.Key.Returns(Guid.NewGuid());
        _urlProvider.GetUrl(content, UrlMode.Absolute).Returns(absoluteUrl);
        return content;
    }

    [Fact]
    public void GenerateGraphJson_NoPieces_ReturnsNull()
    {
        var sut = Build();
        var result = sut.GenerateGraphJson(Page());
        result.Should().BeNull();
    }

    [Fact]
    public void GenerateGraphJson_NoNeededPieces_ReturnsNull()
    {
        var sut = Build(new StubPiece("alpha", 0, id: null, thing: new Thing()));
        var result = sut.GenerateGraphJson(Page());
        result.Should().BeNull();
    }

    [Fact]
    public void GenerateGraphJson_EmitsGraphWithContextAtTopLevel_AndStripsPerNodeContext()
    {
        var sut = Build(
            new StubPiece("organization", 100,
                id: "https://example.com/#organization",
                thing: new Organization { Name = "Acme" }));

        var json = sut.GenerateGraphJson(Page());
        json.Should().NotBeNull();

        using var doc = JsonDocument.Parse(json!);
        var root = doc.RootElement;
        root.GetProperty("@context").GetString().Should().Be("https://schema.org");
        var graph = root.GetProperty("@graph");
        graph.GetArrayLength().Should().Be(1);
        var node = graph[0];
        node.TryGetProperty("@context", out _).Should().BeFalse("per-node @context must be stripped");
        node.GetProperty("@type").GetString().Should().Be("Organization");
        node.GetProperty("@id").GetString().Should().Be("https://example.com/#organization");
        node.GetProperty("name").GetString().Should().Be("Acme");
    }

    [Fact]
    public void GenerateGraphJson_OrdersByOrderThenKey()
    {
        var sut = Build(
            new StubPiece("zebra", 200, "https://example.com/#z", new Thing()),
            new StubPiece("alpha", 100, "https://example.com/#a", new Thing()),
            new StubPiece("beta", 100, "https://example.com/#b", new Thing()));

        var json = sut.GenerateGraphJson(Page());
        using var doc = JsonDocument.Parse(json!);
        var ids = doc.RootElement.GetProperty("@graph").EnumerateArray()
            .Select(n => n.GetProperty("@id").GetString())
            .ToArray();

        ids.Should().Equal(
            "https://example.com/#a",
            "https://example.com/#b",
            "https://example.com/#z");
    }

    [Fact]
    public void GenerateGraphJson_ExposesIdsDictionary_SoPiecesCanCrossReference()
    {
        // 'webpage' piece builds its Thing reading ctx.IdFor("organization").
        var captured = "";
        var webpage = new StubPiece("webpage", 200,
            id: "https://example.com/about/#webpage",
            thing: new WebPage(),
            onBuild: ctx =>
            {
                captured = ctx.IdFor("organization") ?? "";
            });

        var organization = new StubPiece("organization", 100,
            id: "https://example.com/#organization",
            thing: new Organization());

        var sut = Build(organization, webpage);
        sut.GenerateGraphJson(Page()).Should().NotBeNull();

        captured.Should().Be("https://example.com/#organization",
            "the Ids dictionary must be populated before any Build call");
    }

    [Fact]
    public void GenerateGraphJson_ThrowingPiece_DoesNotAbortOtherPieces()
    {
        var sut = Build(
            new StubPiece("explodes", 100, id: "https://example.com/#x", thing: null,
                onResolveId: _ => throw new InvalidOperationException("boom")),
            new StubPiece("healthy", 200,
                id: "https://example.com/#healthy",
                thing: new Organization { Name = "Still here" }));

        var json = sut.GenerateGraphJson(Page());
        using var doc = JsonDocument.Parse(json!);
        var ids = doc.RootElement.GetProperty("@graph").EnumerateArray()
            .Select(n => n.GetProperty("@id").GetString())
            .ToArray();
        ids.Should().Equal("https://example.com/#healthy");
    }

    [Fact]
    public void GenerateGraphJson_CollapsesPureRefsToIdOnly()
    {
        // A piece that emits an Organization with a WebSite reference set via
        // Schema.NET (so the reference serialises as {"@type": "WebSite", "@id": "..."}).
        // GraphGenerator should post-process that to {"@id": "..."} — Yoast-parity.
        var org = new Organization
        {
            Name = "Acme",
            ParentOrganization = new Organization { Id = new Uri("https://example.com/#parent") }
        };
        var sut = Build(new StubPiece("organization", 100,
            id: "https://example.com/#organization",
            thing: org));

        var json = sut.GenerateGraphJson(Page());
        using var doc = JsonDocument.Parse(json!);
        var parent = doc.RootElement.GetProperty("@graph")[0].GetProperty("parentOrganization");
        parent.TryGetProperty("@type", out _).Should().BeFalse("pure refs must collapse");
        parent.GetProperty("@id").GetString().Should().Be("https://example.com/#parent");
    }

    [Fact]
    public void GenerateGraphJson_DoesNotCollapseEmbeddedTypedThings()
    {
        // An embedded PostalAddress with actual fields shouldn't be collapsed —
        // it's content, not a reference.
        var org = new Organization
        {
            Name = "Acme",
            Address = new PostalAddress
            {
                StreetAddress = "1 Example Street",
                AddressLocality = "Leeds"
            }
        };
        var sut = Build(new StubPiece("organization", 100,
            id: "https://example.com/#organization",
            thing: org));

        var json = sut.GenerateGraphJson(Page());
        using var doc = JsonDocument.Parse(json!);
        var address = doc.RootElement.GetProperty("@graph")[0].GetProperty("address");
        address.GetProperty("@type").GetString().Should().Be("PostalAddress");
        address.GetProperty("streetAddress").GetString().Should().Be("1 Example Street");
    }

    [Fact]
    public void GenerateGraphJson_SetsThingIdFromDeclaredId_WhenPieceForgets()
    {
        var thing = new Organization { Name = "NoIdSet" };
        // thing.Id is null when the piece builds — the generator must fill it
        // from the piece's declared ResolveId value so cross-refs work.
        var sut = Build(new StubPiece("organization", 100,
            id: "https://example.com/#organization",
            thing: thing));

        var json = sut.GenerateGraphJson(Page());
        using var doc = JsonDocument.Parse(json!);
        doc.RootElement.GetProperty("@graph")[0]
            .GetProperty("@id").GetString().Should().Be("https://example.com/#organization");
    }

    private sealed class StubPiece : IGraphPiece
    {
        private readonly string? _id;
        private readonly Thing? _thing;
        private readonly Action<GraphPieceContext>? _onBuild;
        private readonly Action<GraphPieceContext>? _onResolveId;

        public StubPiece(
            string key,
            int order,
            string? id,
            Thing? thing,
            Action<GraphPieceContext>? onBuild = null,
            Action<GraphPieceContext>? onResolveId = null)
        {
            Key = key;
            Order = order;
            _id = id;
            _thing = thing;
            _onBuild = onBuild;
            _onResolveId = onResolveId;
        }

        public string Key { get; }
        public int Order { get; }

        public string? ResolveId(GraphPieceContext ctx)
        {
            _onResolveId?.Invoke(ctx);
            return _id;
        }

        public Thing? Build(GraphPieceContext ctx)
        {
            _onBuild?.Invoke(ctx);
            return _thing;
        }
    }
}
