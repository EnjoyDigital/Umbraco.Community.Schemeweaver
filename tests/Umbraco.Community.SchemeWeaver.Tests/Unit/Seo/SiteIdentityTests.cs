using FluentAssertions;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Umbraco.Cms.Core.Routing;
using Umbraco.Cms.Core.Services;
using Umbraco.Cms.Core.Web;
using Umbraco.Community.SchemeWeaver.Graph;
using Umbraco.Community.SchemeWeaver.Seo.SiteIdentity;
using Umbraco.Community.SchemeWeaver.Services;
using Xunit;

namespace Umbraco.Community.SchemeWeaver.Tests.Unit.Seo;

public class SiteIdentityStoreTests
{
    private readonly IKeyValueService _keyValues = Substitute.For<IKeyValueService>();
    private readonly IJsonLdBlocksProvider _blocks = Substitute.For<IJsonLdBlocksProvider>();
    private readonly SiteIdentityStore _sut;

    public SiteIdentityStoreTests()
    {
        _sut = new SiteIdentityStore(_keyValues, new MemoryCache(new MemoryCacheOptions()), _blocks, SeoTestSupport.Registry, NullLogger<SiteIdentityStore>.Instance);
    }

    [Fact]
    public void Get_NothingStored_ReturnsNull()
    {
        _keyValues.GetValue(Arg.Any<string>()).Returns((string?)null);
        _sut.Get().Should().BeNull();
    }

    [Fact]
    public void Save_Valid_StoresJson_FlushesJsonLdCache_AndReadsBack()
    {
        string? stored = null;
        _keyValues.When(k => k.SetValue(SchemeWeaver.Seo.SchemeWeaverSeoConstants.KeyValueKeys.SiteIdentity, Arg.Any<string>()))
            .Do(c => stored = c.ArgAt<string>(1));
        _keyValues.GetValue(Arg.Any<string>()).Returns(_ => stored);

        var errors = _sut.Save(new SiteIdentityDto { Name = " Acme ", SameAs = ["https://x.com/acme", " "] }, Guid.NewGuid());

        errors.Should().BeEmpty();
        stored.Should().Contain("\"name\":\"Acme\"");
        _blocks.Received(1).InvalidateAll();
        var back = _sut.Get()!;
        back.IsConfigured.Should().BeTrue();
        back.SameAs.Should().Equal("https://x.com/acme");
        back.UpdatedDate.Should().NotBeNull();
    }

    [Fact]
    public void Save_Invalid_ReturnsMessages_AndStoresNothing()
    {
        var errors = _sut.Save(new SiteIdentityDto
        {
            Name = "",
            Kind = "robot",
            Url = "not a url",
            SameAs = ["nope"],
            BusinessType = "Recipe",
            SearchUrlTemplate = "https://example.com/search",
        }, null);

        errors.Should().HaveCount(6);
        _keyValues.DidNotReceiveWithAnyArgs().SetValue(default!, default!);
    }

    [Fact]
    public void Get_CorruptJson_TreatsAsNotConfigured()
    {
        _keyValues.GetValue(Arg.Any<string>()).Returns("{not json");
        _sut.Get().Should().BeNull();
    }
}

public class SeoSiteIdentityProviderTests
{
    [Fact]
    public void Build_MapsEveryField()
    {
        var store = Substitute.For<ISiteIdentityStore>();
        var sut = new SeoSiteIdentityProvider(store, Substitute.For<IUmbracoContextAccessor>(), Substitute.For<IPublishedUrlProvider>(), NullLogger<SeoSiteIdentityProvider>.Instance);

        var identity = sut.Build(new SiteIdentityDto
        {
            Kind = "organisation",
            Name = "Acme Dental",
            AlternateName = "Acme",
            Url = "https://example.com",
            SameAs = ["https://x.com/acme", "bad"],
            Telephone = "0113 000 0000",
            Email = "hi@example.com",
            BusinessType = "Dentist",
            Address = new SiteIdentityAddressDto { StreetAddress = "1 High St", AddressLocality = "Leeds", PostalCode = "LS1 1AA", AddressCountry = "GB" },
            Geo = new SiteIdentityGeoDto { Latitude = 53.8, Longitude = -1.5 },
            OpeningHours = [new SiteIdentityOpeningHoursDto { DayOfWeek = ["Monday", "Tuesday", "Funday"], Opens = "09:00", Closes = "17:30" }],
            PriceRange = "££",
            SearchUrlTemplate = "https://example.com/s?q={q}",
            SearchQueryInputName = "q",
        });

        identity.Kind.Should().Be(SiteIdentityKind.Organisation);
        identity.Name.Should().Be("Acme Dental");
        identity.Url.Should().Be(new Uri("https://example.com"));
        identity.SameAs.Should().ContainSingle();
        identity.BusinessType.Should().Be("Dentist");
        identity.Address!.ToString().Should().Contain("Leeds");
        identity.Geo.Should().NotBeNull();
        identity.OpeningHours.Should().ContainSingle();
        identity.PriceRange.Should().Be("££");
        identity.SearchUrlTemplate.Should().Be("https://example.com/s?q={q}");
        identity.SearchQueryInputName.Should().Be("q");
    }

    [Fact]
    public void Resolve_NothingStored_ReturnsNull()
    {
        var store = Substitute.For<ISiteIdentityStore>();
        store.Get().Returns((SiteIdentityDto?)null);
        var sut = new SeoSiteIdentityProvider(store, Substitute.For<IUmbracoContextAccessor>(), Substitute.For<IPublishedUrlProvider>(), NullLogger<SeoSiteIdentityProvider>.Instance);
        sut.Resolve().Should().BeNull();
    }

    [Fact]
    public void Resolve_PersonKind_MapsToPerson()
    {
        var store = Substitute.For<ISiteIdentityStore>();
        store.Get().Returns(new SiteIdentityDto { Kind = "person", Name = "Jane", JobTitle = "Consultant", IsConfigured = true });
        var sut = new SeoSiteIdentityProvider(store, Substitute.For<IUmbracoContextAccessor>(), Substitute.For<IPublishedUrlProvider>(), NullLogger<SeoSiteIdentityProvider>.Instance);
        var identity = sut.Resolve()!;
        identity.Kind.Should().Be(SiteIdentityKind.Person);
        identity.JobTitle.Should().Be("Consultant");
    }
}
