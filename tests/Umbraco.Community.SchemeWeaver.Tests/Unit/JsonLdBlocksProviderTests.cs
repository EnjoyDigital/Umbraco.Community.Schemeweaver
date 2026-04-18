using FluentAssertions;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Umbraco.Cms.Core.Models.PublishedContent;
using Umbraco.Community.SchemeWeaver.Services;
using Xunit;

namespace Umbraco.Community.SchemeWeaver.Tests.Unit;

/// <summary>
/// Exercises the ordering, caching and invalidation semantics of
/// <see cref="JsonLdBlocksProvider"/>. Generation logic lives in
/// <see cref="IJsonLdGenerator"/>; here we substitute it and verify the provider faithfully
/// stitches the four inputs together.
/// </summary>
public class JsonLdBlocksProviderTests
{
    private readonly IJsonLdGenerator _generator = Substitute.For<IJsonLdGenerator>();

    private JsonLdBlocksProvider CreateSut(
        SchemeWeaverOptions? options = null,
        IMemoryCache? cache = null)
    {
        var services = new ServiceCollection();
        services.AddSingleton(_generator);
        var provider = services.BuildServiceProvider();
        var scopeFactory = provider.GetRequiredService<IServiceScopeFactory>();
        return new JsonLdBlocksProvider(
            scopeFactory,
            cache ?? new MemoryCache(new MemoryCacheOptions()),
            Options.Create(options ?? new SchemeWeaverOptions()),
            NullLogger<JsonLdBlocksProvider>.Instance);
    }

    private static IPublishedContent FakeContent(Guid? key = null)
    {
        var content = Substitute.For<IPublishedContent>();
        content.Key.Returns(key ?? Guid.NewGuid());
        return content;
    }

    [Fact]
    public void GetBlocks_DefaultOptions_OrdersInheritedThenBreadcrumbThenMainThenBlocks()
    {
        var sut = CreateSut();
        var content = FakeContent();
        _generator.GenerateInheritedJsonLdStrings(content, Arg.Any<string?>())
            .Returns(["{\"@type\":\"WebSite\"}"]);
        _generator.GenerateBreadcrumbJsonLd(content, Arg.Any<string?>())
            .Returns("{\"@type\":\"BreadcrumbList\"}");
        _generator.GenerateJsonLdString(content, Arg.Any<string?>())
            .Returns("{\"@type\":\"Article\"}");
        _generator.GenerateBlockElementJsonLdStrings(content, Arg.Any<string?>())
            .Returns(["{\"@type\":\"FAQPage\"}"]);

        var blocks = sut.GetBlocks(content, culture: null);

        blocks.Should().ContainInOrder(
            "{\"@type\":\"WebSite\"}",
            "{\"@type\":\"BreadcrumbList\"}",
            "{\"@type\":\"Article\"}",
            "{\"@type\":\"FAQPage\"}");
    }

    [Fact]
    public void GetBlocks_BreadcrumbOptOut_SkipsBreadcrumbAndDoesNotCallGenerator()
    {
        var sut = CreateSut(new SchemeWeaverOptions { EmitBreadcrumbsInDeliveryApi = false });
        var content = FakeContent();
        _generator.GenerateInheritedJsonLdStrings(content, Arg.Any<string?>())
            .Returns(["{\"@type\":\"WebSite\"}"]);
        _generator.GenerateJsonLdString(content, Arg.Any<string?>())
            .Returns("{\"@type\":\"Article\"}");
        _generator.GenerateBlockElementJsonLdStrings(content, Arg.Any<string?>())
            .Returns([]);

        var blocks = sut.GetBlocks(content, culture: null);

        blocks.Should().BeEquivalentTo(new[]
        {
            "{\"@type\":\"WebSite\"}",
            "{\"@type\":\"Article\"}",
        });
        // Opting out must short-circuit the generator call — otherwise a stale breadcrumb
        // could slip in from a cached upstream layer.
        _generator.DidNotReceiveWithAnyArgs().GenerateBreadcrumbJsonLd(default!, default);
    }

    [Fact]
    public void GetBlocks_CachesPerContentKeyAndCulture()
    {
        var sut = CreateSut();
        var content = FakeContent();
        _generator.GenerateInheritedJsonLdStrings(content, Arg.Any<string?>()).Returns([]);
        _generator.GenerateBreadcrumbJsonLd(content, Arg.Any<string?>()).Returns((string?)null);
        _generator.GenerateJsonLdString(content, Arg.Any<string?>()).Returns("{\"@type\":\"WebPage\"}");
        _generator.GenerateBlockElementJsonLdStrings(content, Arg.Any<string?>()).Returns([]);

        _ = sut.GetBlocks(content, culture: "en-gb");
        _ = sut.GetBlocks(content, culture: "en-gb");

        _generator.Received(1).GenerateJsonLdString(content, Arg.Any<string?>());
    }

    [Fact]
    public void GetBlocks_DifferentCulturesMissCacheIndependently()
    {
        var sut = CreateSut();
        var content = FakeContent();
        _generator.GenerateJsonLdString(content, Arg.Any<string?>()).Returns("{\"@type\":\"WebPage\"}");

        _ = sut.GetBlocks(content, culture: "en-gb");
        _ = sut.GetBlocks(content, culture: "de-de");

        _generator.Received(2).GenerateJsonLdString(content, Arg.Any<string?>());
    }

    [Fact]
    public void Invalidate_EvictsSubsequentCalls()
    {
        var sut = CreateSut();
        var content = FakeContent();
        _generator.GenerateJsonLdString(content, Arg.Any<string?>()).Returns("{\"@type\":\"WebPage\"}");

        _ = sut.GetBlocks(content, culture: null);
        sut.Invalidate(content.Key);
        _ = sut.GetBlocks(content, culture: null);

        _generator.Received(2).GenerateJsonLdString(content, Arg.Any<string?>());
    }

    [Fact]
    public void InvalidateAll_EvictsEveryContent()
    {
        var sut = CreateSut();
        var a = FakeContent();
        var b = FakeContent();
        _generator.GenerateJsonLdString(Arg.Any<IPublishedContent>(), Arg.Any<string?>())
            .Returns("{\"@type\":\"WebPage\"}");

        _ = sut.GetBlocks(a, culture: null);
        _ = sut.GetBlocks(b, culture: null);
        sut.InvalidateAll();
        _ = sut.GetBlocks(a, culture: null);
        _ = sut.GetBlocks(b, culture: null);

        _generator.Received(2).GenerateJsonLdString(a, Arg.Any<string?>());
        _generator.Received(2).GenerateJsonLdString(b, Arg.Any<string?>());
    }

    [Fact]
    public void GetBlocks_GeneratorThrows_ReturnsEmptyAndSwallows()
    {
        var sut = CreateSut();
        var content = FakeContent();
        _generator.GenerateInheritedJsonLdStrings(content, Arg.Any<string?>())
            .Throws(new InvalidOperationException("generator went bang"));

        var blocks = sut.GetBlocks(content, culture: null);

        blocks.Should().BeEmpty();
    }
}
