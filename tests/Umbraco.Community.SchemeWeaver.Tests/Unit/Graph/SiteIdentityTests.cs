using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Schema.NET;
using Umbraco.Cms.Core.Models.PublishedContent;
using Umbraco.Community.SchemeWeaver.Graph;
using Umbraco.Community.SchemeWeaver.Graph.Pieces;
using Umbraco.Community.SchemeWeaver.Services;
using Xunit;

namespace Umbraco.Community.SchemeWeaver.Tests.Unit.Graph;

/// <summary>
/// The ISiteIdentityProvider seam: a backoffice/code-managed identity feeds the Organization and
/// WebSite pieces ahead of the schemaSiteSettings node, and never changes behaviour when absent.
/// </summary>
public class SiteIdentityTests
{
    private static readonly Uri SiteUrl = new("https://example.com/");
    private readonly ISchemaTypeRegistry _registry = new SchemaTypeRegistry();
    private readonly IJsonLdGenerator _generator = Substitute.For<IJsonLdGenerator>();

    private static GraphPieceContext Context(SiteIdentity? identity, IPublishedContent? settings = null, Dictionary<string, string>? ids = null) => new()
    {
        Content = Substitute.For<IPublishedContent>(),
        SiteSettings = settings,
        SiteIdentity = identity,
        SiteUrl = SiteUrl,
        Ids = ids ?? new Dictionary<string, string>(),
    };

    private OrganizationPiece OrganisationPiece() =>
        new(_generator, _registry, NullLogger<OrganizationPiece>.Instance);

    private static JsonElement Serialise(Thing thing) => JsonDocument.Parse(thing.ToString()).RootElement;

    [Fact]
    public void OrganizationPiece_IdentityPresent_BuildsOrganizationWithConventionalId_WithoutTouchingSettingsNode()
    {
        var identity = new SiteIdentity
        {
            Name = "Acme Ltd",
            AlternateName = "Acme",
            Logo = new ImageObject { Url = new Uri("https://example.com/media/logo.png") },
            SameAs = [new Uri("https://www.linkedin.com/company/acme")],
            Telephone = "+44 20 7946 0000",
            Email = "hello@example.com",
        };
        var sut = OrganisationPiece();

        var ctx = Context(identity);
        sut.ResolveId(ctx).Should().Be("https://example.com/#organization");
        var node = Serialise(sut.Build(ctx)!);

        node.GetProperty("@type").GetString().Should().Be("Organization");
        node.GetProperty("name").GetString().Should().Be("Acme Ltd");
        node.GetProperty("alternateName").GetString().Should().Be("Acme");
        node.GetProperty("logo").GetProperty("url").GetString().Should().Be("https://example.com/media/logo.png");
        node.GetProperty("sameAs").GetString().Should().Be("https://www.linkedin.com/company/acme");
        node.GetProperty("telephone").GetString().Should().Be("+44 20 7946 0000");
        node.GetProperty("url").GetString().Should().Be("https://example.com/");
        _generator.DidNotReceiveWithAnyArgs().GenerateJsonLd(default!, default, default);
    }

    [Fact]
    public void OrganizationPiece_LocalBusinessIdentity_EmitsSubtypeWithPlaceProperties()
    {
        var identity = new SiteIdentity
        {
            Name = "Acme Dental",
            BusinessType = "Dentist",
            Address = new PostalAddress { StreetAddress = "1 High St", AddressLocality = "Leeds", PostalCode = "LS1 1AA", AddressCountry = "GB" },
            Geo = new GeoCoordinates { Latitude = 53.8, Longitude = -1.55 },
            OpeningHours = [new OpeningHoursSpecification { DayOfWeek = Schema.NET.DayOfWeek.Monday, Opens = new TimeSpan(9, 0, 0), Closes = new TimeSpan(17, 0, 0) }],
            PriceRange = "££",
        };

        var node = Serialise(OrganisationPiece().Build(Context(identity))!);

        node.GetProperty("@type").GetString().Should().Be("Dentist");
        node.GetProperty("@id").GetString().Should().Be("https://example.com/#organization");
        node.GetProperty("address").GetProperty("addressLocality").GetString().Should().Be("Leeds");
        node.GetProperty("geo").GetProperty("latitude").GetDouble().Should().Be(53.8);
        node.GetProperty("openingHoursSpecification").GetProperty("dayOfWeek").GetString().Should().Contain("Monday");
        node.GetProperty("priceRange").GetString().Should().Be("££");
    }

    [Fact]
    public void OrganizationPiece_UnknownBusinessType_DegradesToOrganization()
    {
        var identity = new SiteIdentity { Name = "Acme", BusinessType = "NotARealType" };
        var node = Serialise(OrganisationPiece().Build(Context(identity))!);
        node.GetProperty("@type").GetString().Should().Be("Organization");
    }

    [Fact]
    public void OrganizationPiece_PersonIdentity_EmitsPersonUnderTheOrganizationKey()
    {
        var identity = new SiteIdentity { Kind = SiteIdentityKind.Person, Name = "Jane Doe", JobTitle = "Consultant" };
        var sut = OrganisationPiece();
        var ctx = Context(identity);

        sut.ResolveId(ctx).Should().Be("https://example.com/#organization", "cross-references keep working whatever the publisher type");
        var node = Serialise(sut.Build(ctx)!);
        node.GetProperty("@type").GetString().Should().Be("Person");
        node.GetProperty("jobTitle").GetString().Should().Be("Consultant");
    }

    [Fact]
    public void OrganizationPiece_NoIdentity_NoSettingsNode_StillSkips()
    {
        var sut = OrganisationPiece();
        sut.ResolveId(Context(identity: null)).Should().BeNull();
    }

    [Fact]
    public void OrganizationPiece_IdentityWithEmptyName_FallsBackToSettingsNodeMapping()
    {
        var settings = Substitute.For<IPublishedContent>();
        _generator.GenerateJsonLd(settings, Arg.Any<string?>(), Arg.Any<GraphPieceContext?>())
            .Returns(new Organization { Name = "From node" });
        var identity = new SiteIdentity { Name = "   " };

        var node = Serialise(OrganisationPiece().Build(Context(identity, settings))!);
        node.GetProperty("name").GetString().Should().Be("From node");
    }

    [Fact]
    public void WebSitePiece_IdentityPresent_UsesItsNameAndSearchTemplate_OverSettingsAndOptions()
    {
        var settings = Substitute.For<IPublishedContent>();
        settings.Name.Returns("Settings Node");
        var sut = new WebSitePiece(
            NullLogger<WebSitePiece>.Instance,
            Options.Create(new SchemeWeaverOptions
            {
                SiteSearch = new SiteSearchOptions { UrlTemplate = "https://example.com/old?q={search_term_string}" }
            }));
        var identity = new SiteIdentity
        {
            Name = "Identity Name",
            AlternateName = "Alt",
            SearchUrlTemplate = "https://example.com/find?term={q}",
            SearchQueryInputName = "q",
        };
        var ids = new Dictionary<string, string> { ["organization"] = "https://example.com/#organization" };

        var node = Serialise(sut.Build(Context(identity, settings, ids))!);

        node.GetProperty("name").GetString().Should().Be("Identity Name");
        node.GetProperty("alternateName").GetString().Should().Be("Alt");
        node.GetProperty("potentialAction").GetProperty("target").GetProperty("urlTemplate").GetString()
            .Should().Be("https://example.com/find?term={q}");
        node.GetProperty("potentialAction").GetProperty("query-input").GetString().Should().Be("required name=q");
        node.GetProperty("publisher").GetProperty("@id").GetString().Should().Be("https://example.com/#organization");
    }

    [Fact]
    public void WebSitePiece_PersonIdentity_PublisherRefIsPerson()
    {
        var sut = new WebSitePiece(NullLogger<WebSitePiece>.Instance, Options.Create(new SchemeWeaverOptions()));
        var identity = new SiteIdentity { Kind = SiteIdentityKind.Person, Name = "Jane" };
        var ids = new Dictionary<string, string> { ["organization"] = "https://example.com/#organization" };

        var node = Serialise(sut.Build(Context(identity, ids: ids))!);
        node.GetProperty("publisher").GetProperty("@type").GetString().Should().Be("Person");
    }

    [Fact]
    public void WebSitePiece_IdentityWithoutSearchTemplate_StillUsesOptionsTemplate()
    {
        var sut = new WebSitePiece(
            NullLogger<WebSitePiece>.Instance,
            Options.Create(new SchemeWeaverOptions
            {
                SiteSearch = new SiteSearchOptions { UrlTemplate = "https://example.com/search?q={search_term_string}" }
            }));

        var node = Serialise(sut.Build(Context(new SiteIdentity { Name = "Acme" }))!);
        node.GetProperty("potentialAction").GetProperty("target").GetProperty("urlTemplate").GetString()
            .Should().Be("https://example.com/search?q={search_term_string}");
    }

    [Fact]
    public void GraphGenerator_ProviderThrows_GraphStillRenders()
    {
        var provider = Substitute.For<ISiteIdentityProvider>();
        provider.Resolve().Returns(_ => throw new InvalidOperationException("boom"));
        var piece = Substitute.For<IGraphPiece>();
        piece.Key.Returns("stub");
        piece.ResolveId(Arg.Any<GraphPieceContext>()).Returns("https://example.com/#stub");
        piece.Build(Arg.Any<GraphPieceContext>()).Returns(new Thing { Name = "ok" });
        var httpContextAccessor = Substitute.For<Microsoft.AspNetCore.Http.IHttpContextAccessor>();
        var http = new Microsoft.AspNetCore.Http.DefaultHttpContext();
        http.Request.Scheme = "https";
        http.Request.Host = new Microsoft.AspNetCore.Http.HostString("example.com");
        httpContextAccessor.HttpContext.Returns(http);

        var sut = new GraphGenerator(
            [piece],
            Substitute.For<ISiteSettingsResolver>(),
            new SiteOriginResolver(httpContextAccessor, Options.Create(new SchemeWeaverOptions()), NullLogger<SiteOriginResolver>.Instance),
            Substitute.For<Umbraco.Cms.Core.Routing.IPublishedUrlProvider>(),
            NullLogger<GraphGenerator>.Instance,
            provider);

        var content = Substitute.For<IPublishedContent>();
        content.Key.Returns(Guid.NewGuid());
        sut.GenerateGraphJson(content).Should().Contain("\"ok\"");
        piece.Received().Build(Arg.Is<GraphPieceContext>(c => c.SiteIdentity == null));
    }
}
