using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Umbraco.Community.SchemeWeaver.Services;
using Xunit;

namespace Umbraco.Community.SchemeWeaver.Tests.Unit;

/// <summary>
/// <c>SchemeWeaver:PublicSiteUrl</c> — the origin override for headless sites where
/// Umbraco is reached on a different host (cms.example.com) than the one the public
/// front-end serves pages on (www.example.com).
/// </summary>
public class SiteOriginResolverTests
{
    private static IHttpContextAccessor Accessor(string? scheme = "https", string? host = "cms.example.com")
    {
        var accessor = Substitute.For<IHttpContextAccessor>();
        if (scheme is null || host is null)
        {
            accessor.HttpContext.Returns((HttpContext?)null);
            return accessor;
        }

        var context = new DefaultHttpContext();
        context.Request.Scheme = scheme;
        context.Request.Host = new HostString(host);
        accessor.HttpContext.Returns(context);
        return accessor;
    }

    private static SiteOriginResolver Build(IHttpContextAccessor accessor, string? publicSiteUrl = null) =>
        new(accessor,
            Options.Create(new SchemeWeaverOptions { PublicSiteUrl = publicSiteUrl }),
            NullLogger<SiteOriginResolver>.Instance);

    // --- ResolveOrigin ---

    [Fact]
    public void ResolveOrigin_NoOverride_UsesRequestHost()
    {
        var sut = Build(Accessor());

        sut.ResolveOrigin()!.GetLeftPart(UriPartial.Authority)
            .Should().Be("https://cms.example.com");
    }

    [Fact]
    public void ResolveOrigin_OverrideConfigured_UsesPublicSiteUrl()
    {
        var sut = Build(Accessor(), "https://www.example.com");

        sut.ResolveOrigin()!.GetLeftPart(UriPartial.Authority)
            .Should().Be("https://www.example.com");
    }

    [Fact]
    public void ResolveOrigin_OverrideConfiguredAndNoHttpContext_StillResolves()
    {
        // The Examine index handler generates JSON-LD with no request in flight.
        // Without an override that path has no origin at all; with one it does.
        var sut = Build(Accessor(scheme: null, host: null), "https://www.example.com");

        sut.ResolveOrigin()!.GetLeftPart(UriPartial.Authority)
            .Should().Be("https://www.example.com");
    }

    [Fact]
    public void ResolveOrigin_NoOverrideAndNoHttpContext_ReturnsNull()
    {
        var sut = Build(Accessor(scheme: null, host: null));

        sut.ResolveOrigin().Should().BeNull();
    }

    [Theory]
    [InlineData("not a url")]
    [InlineData("www.example.com")]        // no scheme — relative, not absolute
    [InlineData("ftp://www.example.com")]  // wrong scheme
    public void ResolveOrigin_InvalidOverride_FallsBackToRequestHost(string configured)
    {
        // A typo in appsettings must degrade to the historical behaviour, never throw
        // and never take structured data down with it.
        var sut = Build(Accessor(), configured);

        sut.ResolveOrigin()!.GetLeftPart(UriPartial.Authority)
            .Should().Be("https://cms.example.com");
    }

    [Theory]
    [InlineData("https://www.example.com/")]
    [InlineData("https://www.example.com/some/path")]
    [InlineData("https://www.example.com?utm=1")]
    [InlineData("https://www.example.com/#frag")]
    [InlineData("  https://www.example.com  ")]
    public void ResolveOrigin_OverrideCarryingPathOrWhitespace_NormalisesToOrigin(string configured)
    {
        var sut = Build(Accessor(), configured);

        sut.ResolveOrigin()!.GetLeftPart(UriPartial.Authority)
            .Should().Be("https://www.example.com");
    }

    [Fact]
    public void ResolveOrigin_OverrideWithPort_PreservesPort()
    {
        var sut = Build(Accessor(), "https://www.example.com:8443");

        sut.ResolveOrigin()!.GetLeftPart(UriPartial.Authority)
            .Should().Be("https://www.example.com:8443");
    }

    // --- RebaseToPublicOrigin ---

    [Fact]
    public void RebaseToPublicOrigin_RewritesRequestOriginUrls()
    {
        var sut = Build(Accessor(), "https://www.example.com");

        var json = sut.RebaseToPublicOrigin(
            """{"@id":"https://cms.example.com/about/#webpage","url":"https://cms.example.com/about/"}""");

        json.Should().Be(
            """{"@id":"https://www.example.com/about/#webpage","url":"https://www.example.com/about/"}""");
    }

    [Fact]
    public void RebaseToPublicOrigin_LeavesForeignHostsUntouched()
    {
        var sut = Build(Accessor(), "https://www.example.com");

        var json = sut.RebaseToPublicOrigin(
            """{"url":"https://cms.example.com/a/","sameAs":"https://twitter.com/acme","logo":"https://cdn.acme.net/logo.png"}""");

        json.Should().Be(
            """{"url":"https://www.example.com/a/","sameAs":"https://twitter.com/acme","logo":"https://cdn.acme.net/logo.png"}""");
    }

    [Fact]
    public void RebaseToPublicOrigin_HostIsPrefixOfAnotherHost_DoesNotRewrite()
    {
        // "https://cms.example.com" must not match inside "https://cms.example.com.evil.net".
        var sut = Build(Accessor(), "https://www.example.com");

        var json = sut.RebaseToPublicOrigin("""{"url":"https://cms.example.com.evil.net/phish"}""");

        json.Should().Be("""{"url":"https://cms.example.com.evil.net/phish"}""");
    }

    [Fact]
    public void RebaseToPublicOrigin_BareOriginWithNoTrailingPath_IsRewritten()
    {
        // The WebSite node's url is the bare origin — the closing quote is the boundary.
        var sut = Build(Accessor(), "https://www.example.com");

        sut.RebaseToPublicOrigin("""{"url":"https://cms.example.com"}""")
            .Should().Be("""{"url":"https://www.example.com"}""");
    }

    [Fact]
    public void RebaseToPublicOrigin_HostCasingDiffers_StillRewrites()
    {
        var sut = Build(Accessor(host: "CMS.Example.COM"), "https://www.example.com");

        sut.RebaseToPublicOrigin("""{"url":"https://cms.example.com/a/"}""")
            .Should().Be("""{"url":"https://www.example.com/a/"}""");
    }

    [Fact]
    public void RebaseToPublicOrigin_NoOverride_ReturnsInputUnchanged()
    {
        var sut = Build(Accessor());
        const string json = """{"url":"https://cms.example.com/a/"}""";

        sut.RebaseToPublicOrigin(json).Should().Be(json);
    }

    [Fact]
    public void RebaseToPublicOrigin_RequestAlreadyOnPublicOrigin_ReturnsInputUnchanged()
    {
        var sut = Build(Accessor(host: "www.example.com"), "https://www.example.com");
        const string json = """{"url":"https://www.example.com/a/"}""";

        sut.RebaseToPublicOrigin(json).Should().Be(json);
    }

    [Fact]
    public void RebaseToPublicOrigin_NoHttpContext_ReturnsInputUnchanged()
    {
        // Nothing to rebase FROM: URLs built with no request already used the
        // public origin via ResolveOrigin, so a blind rewrite would be wrong.
        var sut = Build(Accessor(scheme: null, host: null), "https://www.example.com");
        const string json = """{"url":"https://other.example.com/a/"}""";

        sut.RebaseToPublicOrigin(json).Should().Be(json);
    }

    [Fact]
    public void RebaseToPublicOrigin_EmptyInput_ReturnsInput()
    {
        var sut = Build(Accessor(), "https://www.example.com");

        sut.RebaseToPublicOrigin(string.Empty).Should().BeEmpty();
    }

    [Fact]
    public void RebaseToPublicOrigin_RequestOnNonStandardPort_RewritesIncludingPort()
    {
        var sut = Build(Accessor(host: "localhost:44346"), "https://www.example.com");

        sut.RebaseToPublicOrigin("""{"url":"https://localhost:44346/a/"}""")
            .Should().Be("""{"url":"https://www.example.com/a/"}""");
    }
}
