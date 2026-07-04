using FluentAssertions;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Schema.NET;
using Xunit;
using Umbraco.Cms.Core.Models.PublishedContent;
using Umbraco.Cms.Core.PublishedCache;
using Umbraco.Cms.Core.Routing;
using Umbraco.Cms.Core.Services.Navigation;
using Umbraco.Community.SchemeWeaver.Graph;
using Umbraco.Community.SchemeWeaver.Graph.Pieces;

namespace Umbraco.Community.SchemeWeaver.Tests.Unit.Graph;

public class BreadcrumbListPieceTests
{
    private readonly IDocumentNavigationQueryService _navigationQueryService = Substitute.For<IDocumentNavigationQueryService>();
    private readonly IPublishedContentStatusFilteringService _publishedStatusFilteringService = Substitute.For<IPublishedContentStatusFilteringService>();
    private readonly IPublishedUrlProvider _urlProvider = Substitute.For<IPublishedUrlProvider>();
    private readonly BreadcrumbListPiece _sut;

    public BreadcrumbListPieceTests()
    {
        _sut = new BreadcrumbListPiece(
            _navigationQueryService,
            _publishedStatusFilteringService,
            _urlProvider,
            Substitute.For<ILogger<BreadcrumbListPiece>>());
    }

    private static IPublishedContent Node(string name)
    {
        var node = Substitute.For<IPublishedContent>();
        node.Key.Returns(Guid.NewGuid());
        node.Name.Returns(name);
        return node;
    }

    // Wire `child.Parent<IPublishedContent>(nav, filtering)` to resolve to `parent`.
    // The Umbraco Parent<T> extension resolves the parent KEY via the navigation
    // service, then materialises it through the status-filtering service
    // (FilterAvailable on 17, Unfiltered on 18) — there is no other seam.
    private void WireParent(IPublishedContent child, IPublishedContent parent)
    {
        _navigationQueryService.TryGetParentKey(child.Key, out Arg.Any<Guid?>())
            .Returns(ci => { ci[1] = (Guid?)parent.Key; return true; });

        // Parent is the root: TryGetParentKey MUST return true with a null out-key.
        // Returning false makes GetParent throw KeyNotFoundException, which
        // WalkAncestors catches and degrades to a single-node (count-1) chain.
        _navigationQueryService.TryGetParentKey(parent.Key, out Arg.Any<Guid?>())
            .Returns(ci => { ci[1] = (Guid?)null; return true; });

        _publishedStatusFilteringService
            .FilterAvailable(Arg.Is<IEnumerable<Guid>>(keys => keys.Contains(parent.Key)), Arg.Any<string?>())
            .Returns(new[] { parent });
#if UMBRACO18
#pragma warning disable CS0618 // Unfiltered is Umbraco-internal-deprecated; mocked, not used for functionality
        _publishedStatusFilteringService
            .Unfiltered(Arg.Is<IEnumerable<Guid>>(keys => keys.Contains(parent.Key)))
            .Returns(new[] { parent });
#pragma warning restore CS0618
#endif
    }

    private GraphPieceContext Context(IPublishedContent content, Uri? pageUrl) =>
        new() { Content = content, PageUrl = pageUrl };

    [Fact]
    public void ResolveId_NullPageUrl_WithAncestors_StillEmits_UsingSyntheticId()
    {
        // Regression: the backoffice preview / a reverse proxy can leave PageUrl
        // (and even the relative URL) unresolved. The breadcrumb must NOT be
        // dropped just because no absolute URL exists — it falls back to a
        // synthetic @id so the trail still renders.
        var parent = Node("Home");
        var child = Node("Article");
        WireParent(child, parent);
        _urlProvider.GetUrl(child, UrlMode.Relative).Returns("#"); // relative unresolvable too

        var id = _sut.ResolveId(Context(child, pageUrl: null));

        id.Should().Be($"#breadcrumb-{child.Key}");
    }

    [Fact]
    public void ResolveId_NullPageUrl_RelativeResolves_UsesRelativeBasis()
    {
        var parent = Node("Home");
        var child = Node("Article");
        WireParent(child, parent);
        _urlProvider.GetUrl(child, UrlMode.Relative).Returns("/news/test/");

        var id = _sut.ResolveId(Context(child, pageUrl: null));

        id.Should().Be("/news/test/#breadcrumb");
    }

    [Fact]
    public void ResolveId_PageUrlResolves_UsesAbsolutePageUrl()
    {
        var parent = Node("Home");
        var child = Node("Article");
        WireParent(child, parent);
        var pageUrl = new Uri("https://example.com/news/test/");

        var id = _sut.ResolveId(Context(child, pageUrl));

        id.Should().Be("https://example.com/news/test/#breadcrumb");
    }

    [Fact]
    public void ResolveId_SingleNode_NoAncestors_ReturnsNull()
    {
        // Root/orphan page: only itself in the chain → a 1-item breadcrumb is
        // not meaningful, so the piece is still correctly skipped.
        var lone = Node("Home");

        var id = _sut.ResolveId(Context(lone, pageUrl: null));

        id.Should().BeNull();
    }

    [Fact]
    public void Build_NullPageUrl_EmitsBreadcrumbListWithAllAncestors()
    {
        var parent = Node("Home");
        var child = Node("Article");
        WireParent(child, parent);

        var thing = _sut.Build(Context(child, pageUrl: null));

        thing.Should().BeOfType<BreadcrumbList>();
        ((BreadcrumbList)thing!).ItemListElement.Count().Should().Be(2);
    }
}
