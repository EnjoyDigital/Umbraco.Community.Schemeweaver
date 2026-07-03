using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Umbraco.Cms.Core.Models.PublishedContent;
using Umbraco.Community.SchemeWeaver.Graph;
using Umbraco.Community.SchemeWeaver.Graph.Pieces;
using Xunit;

namespace Umbraco.Community.SchemeWeaver.Tests.Unit.Graph;

public class WebSitePieceTests
{
    private static GraphPieceContext Context(string? siteUrl = "https://example.com/") => new()
    {
        Content = Substitute.For<IPublishedContent>(),
        SiteUrl = siteUrl is null ? null : new Uri(siteUrl),
    };

    private static WebSitePiece Build(
        SiteSearchOptions? siteSearch = null,
        ILogger<WebSitePiece>? logger = null) => new(
        logger ?? NullLogger<WebSitePiece>.Instance,
        Options.Create(new SchemeWeaverOptions { SiteSearch = siteSearch ?? new SiteSearchOptions() }));

    private static JsonElement Serialise(Schema.NET.Thing thing) =>
        JsonDocument.Parse(thing.ToString()).RootElement;

    [Fact]
    public void Build_SiteSearchConfigured_EmitsSearchActionPotentialAction()
    {
        var sut = Build(new SiteSearchOptions
        {
            UrlTemplate = "https://example.com/search?q={search_term_string}",
        });

        var thing = sut.Build(Context());
        thing.Should().NotBeNull();

        var node = Serialise(thing!);
        var action = node.GetProperty("potentialAction");
        action.GetProperty("@type").GetString().Should().Be("SearchAction");

        var target = action.GetProperty("target");
        target.GetProperty("@type").GetString().Should().Be("EntryPoint");
        target.GetProperty("urlTemplate").GetString()
            .Should().Be("https://example.com/search?q={search_term_string}");

        action.GetProperty("query-input").GetString()
            .Should().Be("required name=search_term_string");
    }

    [Fact]
    public void Build_SiteSearchNotConfigured_OmitsPotentialAction()
    {
        var sut = Build();

        var thing = sut.Build(Context());
        thing.Should().NotBeNull();

        var node = Serialise(thing!);
        node.TryGetProperty("potentialAction", out _).Should().BeFalse(
            "no potentialAction may be emitted when SchemeWeaver:SiteSearch:UrlTemplate is unset");
    }

    [Fact]
    public void Build_SiteSearchCustomQueryInputName_UsesConfiguredName()
    {
        var sut = Build(new SiteSearchOptions
        {
            UrlTemplate = "https://example.com/find?term={term}",
            QueryInputName = "term",
        });

        var node = Serialise(sut.Build(Context())!);
        var action = node.GetProperty("potentialAction");
        action.GetProperty("target").GetProperty("urlTemplate").GetString()
            .Should().Be("https://example.com/find?term={term}");
        action.GetProperty("query-input").GetString().Should().Be("required name=term");
    }

    [Fact]
    public void Build_TemplateWithoutPlaceholder_StillEmits_ButLogsWarning()
    {
        // Google tolerates a template without the placeholder (some sites put it
        // in the path or add it client-side), so we warn rather than suppress.
        var logger = Substitute.For<ILogger<WebSitePiece>>();
        var sut = Build(new SiteSearchOptions
        {
            UrlTemplate = "https://example.com/search",
        }, logger);

        var node = Serialise(sut.Build(Context())!);
        node.GetProperty("potentialAction").GetProperty("@type").GetString()
            .Should().Be("SearchAction");

        logger.ReceivedCalls()
            .Where(c => c.GetMethodInfo().Name == nameof(ILogger.Log))
            .Select(c => (LogLevel)c.GetArguments()[0]!)
            .Should().Contain(LogLevel.Warning,
                "a template missing its {placeholder} deserves a warning even though it is still emitted");
    }

    [Fact]
    public void Build_NoSiteUrl_ReturnsNull_EvenWhenSearchConfigured()
    {
        var sut = Build(new SiteSearchOptions
        {
            UrlTemplate = "https://example.com/search?q={search_term_string}",
        });

        sut.Build(Context(siteUrl: null)).Should().BeNull();
    }
}
