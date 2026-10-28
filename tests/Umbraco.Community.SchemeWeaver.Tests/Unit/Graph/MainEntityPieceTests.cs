using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Schema.NET;
using Umbraco.Cms.Core.Models.PublishedContent;
using Umbraco.Community.SchemeWeaver.Graph;
using Umbraco.Community.SchemeWeaver.Graph.Pieces;
using Umbraco.Community.SchemeWeaver.Services;
using Xunit;

namespace Umbraco.Community.SchemeWeaver.Tests.Unit.Graph;

public class MainEntityPieceTests
{
    private const string OrganizationId = "https://example.com/#organization";

    private readonly IJsonLdGenerator _generator = Substitute.For<IJsonLdGenerator>();

    private MainEntityPiece Build(Thing? thing)
    {
        _generator
            .GenerateJsonLd(Arg.Any<IPublishedContent>(), Arg.Any<string?>(), Arg.Any<GraphPieceContext?>())
            .Returns(thing);
        return new MainEntityPiece(_generator, NullLogger<MainEntityPiece>.Instance);
    }

    private static IPublishedContent Node()
    {
        var content = Substitute.For<IPublishedContent>();
        content.Key.Returns(Guid.NewGuid());
        return content;
    }

    private static GraphPieceContext Context(
        IPublishedContent content, IPublishedContent? siteSettings, bool organizationPieceContributes = true) => new()
    {
        Content = content,
        SiteSettings = siteSettings,
        Ids = organizationPieceContributes
            ? new Dictionary<string, string> { ["organization"] = OrganizationId }
            : new Dictionary<string, string>(),
    };

    [Fact]
    public void SiteSettingsNodeMappedToOrganization_IsLeftToTheOrganizationPiece()
    {
        var home = Node();
        var sut = Build(new Organization { Id = new Uri(OrganizationId), Name = "Acme" });
        var ctx = Context(home, siteSettings: home);

        sut.ResolveId(ctx).Should().BeNull("the Organization piece already emits this node under the same @id");
        sut.Build(ctx).Should().BeNull();
    }

    [Fact]
    public void SiteSettingsNodeMappedToOrganizationSubtype_IsLeftToTheOrganizationPiece()
    {
        var home = Node();
        var sut = Build(new EducationalOrganization { Id = new Uri(OrganizationId) });
        var ctx = Context(home, siteSettings: home);

        sut.ResolveId(ctx).Should().BeNull();
        sut.Build(ctx).Should().BeNull();
    }

    [Fact]
    public void SiteSettingsNodeMappedToOrganization_OrganizationPieceNotContributing_StillEmitted()
    {
        var home = Node();
        var sut = Build(new Organization { Id = new Uri(OrganizationId) });
        var ctx = Context(home, siteSettings: home, organizationPieceContributes: false);

        sut.ResolveId(ctx).Should().Be(OrganizationId);
        sut.Build(ctx).Should().BeOfType<Organization>();
    }

    [Fact]
    public void SiteSettingsNodeMappedToNonOrganization_StillEmitted()
    {
        var home = Node();
        var sut = Build(new WebPage { Id = new Uri("https://example.com/#webpage") });
        var ctx = Context(home, siteSettings: home);

        sut.ResolveId(ctx).Should().Be("https://example.com/#webpage");
        sut.Build(ctx).Should().BeOfType<WebPage>();
    }

    [Fact]
    public void OtherPageMappedToOrganization_StillEmitted()
    {
        var sut = Build(new Organization { Id = new Uri("https://example.com/partners/acme/#organization") });
        var ctx = Context(Node(), siteSettings: Node());

        sut.ResolveId(ctx).Should().Be("https://example.com/partners/acme/#organization");
        sut.Build(ctx).Should().BeOfType<Organization>();
    }
}
