using FluentAssertions;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Umbraco.Cms.Core.Events;
using Umbraco.Cms.Core.Models;
using Umbraco.Cms.Core.Models.PublishedContent;
using Umbraco.Cms.Core.Notifications;
using Umbraco.Community.SchemeWeaver.Graph;
using Umbraco.Community.SchemeWeaver.Models.Entities;
using Umbraco.Community.SchemeWeaver.Notifications;
using Umbraco.Community.SchemeWeaver.Persistence;
using Umbraco.Community.SchemeWeaver.Services;
using Xunit;

namespace Umbraco.Community.SchemeWeaver.Tests.Unit;

/// <summary>
/// Each notification handler evicts the affected content's own cache key, then ripples to
/// descendants with a single O(1) <see cref="IJsonLdBlocksProvider.InvalidateAll"/> ONLY when
/// they can depend on it — the published type is inherited, the site uses cross-node mappings,
/// or the event is a move. It no longer walks the subtree from the DB under the publish lock.
/// </summary>
public class JsonLdCacheInvalidationTests
{
    private readonly IJsonLdBlocksProvider _provider = Substitute.For<IJsonLdBlocksProvider>();
    private readonly ISchemaMappingRepository _repo = Substitute.For<ISchemaMappingRepository>();
    private readonly IOptions<SchemeWeaverOptions> _options = Options.Create(new SchemeWeaverOptions());

    public JsonLdCacheInvalidationTests()
    {
        // Default: no dependencies anywhere -> a publish should NOT ripple.
        _repo.GetByContentTypeAlias(Arg.Any<string>()).Returns((SchemaMapping?)null);
        _repo.GetInheritedMappings().Returns(Array.Empty<SchemaMapping>());
        _repo.GetAllPropertyMappingsByMappingId().Returns(new Dictionary<int, List<PropertyMapping>>());
    }

    private static IContent MakeContent(string alias = "article", Guid? key = null)
    {
        var content = Substitute.For<IContent>();
        content.Key.Returns(key ?? Guid.NewGuid());
        var ct = Substitute.For<ISimpleContentType>();
        ct.Alias.Returns(alias);
        content.ContentType.Returns(ct);
        return content;
    }

    [Fact]
    public void Publish_LeafWithNoDependencies_InvalidatesOnlyTarget_NoInvalidateAll()
    {
        var target = MakeContent("article");
        var handler = new InvalidateJsonLdCacheOnPublish(_provider, _repo, _options,
            NullLogger<InvalidateJsonLdCacheOnPublish>.Instance);

        using var messages = new EventMessages();
        handler.Handle(new ContentPublishedNotification(target, messages));

        _provider.Received(1).Invalidate(target.Key);
        _provider.DidNotReceive().InvalidateAll();
    }

    [Fact]
    public void Publish_InheritedType_RipplesWithInvalidateAll()
    {
        var target = MakeContent("homePage");
        _repo.GetByContentTypeAlias("homePage").Returns(new SchemaMapping { ContentTypeAlias = "homePage", IsInherited = true, IsEnabled = true });

        var handler = new InvalidateJsonLdCacheOnPublish(_provider, _repo, _options,
            NullLogger<InvalidateJsonLdCacheOnPublish>.Instance);

        using var messages = new EventMessages();
        handler.Handle(new ContentPublishedNotification(target, messages));

        _provider.Received(1).Invalidate(target.Key);
        _provider.Received(1).InvalidateAll();
    }

    [Fact]
    public void Publish_SiteUsesCrossNodeSource_RipplesWithInvalidateAll()
    {
        var target = MakeContent("article");
        // A blogArticle elsewhere pulls Publisher from an ancestor — any publish could affect it.
        _repo.GetAllPropertyMappingsByMappingId().Returns(new Dictionary<int, List<PropertyMapping>>
        {
            [1] = new() { new PropertyMapping { SchemaPropertyName = "Publisher", SourceType = "ancestor" } },
        });

        var handler = new InvalidateJsonLdCacheOnPublish(_provider, _repo, _options,
            NullLogger<InvalidateJsonLdCacheOnPublish>.Instance);

        using var messages = new EventMessages();
        handler.Handle(new ContentPublishedNotification(target, messages));

        _provider.Received(1).Invalidate(target.Key);
        _provider.Received(1).InvalidateAll();
    }

    [Fact]
    public void Unpublish_InheritedType_RipplesWithInvalidateAll()
    {
        var target = MakeContent("homePage");
        _repo.GetByContentTypeAlias("homePage").Returns(new SchemaMapping { IsInherited = true, IsEnabled = true });

        var handler = new InvalidateJsonLdCacheOnUnpublish(_provider, _repo, _options,
            NullLogger<InvalidateJsonLdCacheOnUnpublish>.Instance);

        using var messages = new EventMessages();
        handler.Handle(new ContentUnpublishedNotification(target, messages));

        _provider.Received(1).Invalidate(target.Key);
        _provider.Received(1).InvalidateAll();
    }

    [Fact]
    public void Delete_LeafWithNoDependencies_InvalidatesOnlyTarget()
    {
        var target = MakeContent("article");
        var handler = new InvalidateJsonLdCacheOnDelete(_provider, _repo, _options,
            NullLogger<InvalidateJsonLdCacheOnDelete>.Instance);

        using var messages = new EventMessages();
        handler.Handle(new ContentDeletedNotification(target, messages));

        _provider.Received(1).Invalidate(target.Key);
        _provider.DidNotReceive().InvalidateAll();
    }

    [Fact]
    public void Move_AlwaysRipples_EvenWithNoDependencies()
    {
        var target = MakeContent("article");
        // Use each major's non-obsolete MoveEventInfo overload.
#if UMBRACO18
        var moveInfo = new MoveEventInfo<IContent>(target, "-1,1", (Guid?)null);
#else
        var moveInfo = new MoveEventInfo<IContent>(target, "-1,1", 99);
#endif
        var handler = new InvalidateJsonLdCacheOnMove(_provider, _repo, _options,
            NullLogger<InvalidateJsonLdCacheOnMove>.Instance);

        using var messages = new EventMessages();
        handler.Handle(new ContentMovedNotification(moveInfo, messages));

        _provider.Received(1).Invalidate(target.Key);
        _provider.Received(1).InvalidateAll(); // moves change ancestry/breadcrumbs regardless
    }

    [Fact]
    public void MoveToRecycleBin_AlwaysRipples()
    {
        var target = MakeContent("article");
        var moveInfo = new MoveToRecycleBinEventInfo<IContent>(target, "-1,1");
        var handler = new InvalidateJsonLdCacheOnMove(_provider, _repo, _options,
            NullLogger<InvalidateJsonLdCacheOnMove>.Instance);

        using var messages = new EventMessages();
        handler.Handle(new ContentMovedToRecycleBinNotification(moveInfo, messages));

        _provider.Received(1).Invalidate(target.Key);
        _provider.Received(1).InvalidateAll();
    }

    [Fact]
    public void Publish_SiteSettingsNode_RipplesWithInvalidateAll()
    {
        // The site-scope graph (Organization/WebSite) is generated FROM the settings node but
        // cached under every ROUTED page's key. Publishing the settings node must therefore
        // ripple to InvalidateAll — per-key eviction alone evicts only entries keyed on the
        // (unrouted) settings node itself, leaving every page serving the old site graph until
        // absolute expiry or an application restart.
        var target = MakeContent("schemaSiteSettings"); // default SiteSettingsOptions.ContentTypeAlias
        var handler = new InvalidateJsonLdCacheOnPublish(_provider, _repo, _options,
            NullLogger<InvalidateJsonLdCacheOnPublish>.Instance);

        using var messages = new EventMessages();
        handler.Handle(new ContentPublishedNotification(target, messages));

        _provider.Received(1).Invalidate(target.Key);
        _provider.Received(1).InvalidateAll();
    }

    [Fact]
    public void Publish_SiteSettingsNodeMatchedByConfiguredContentKey_RipplesWithInvalidateAll()
    {
        // SiteSettingsOptions.ContentKey overrides the alias-based lookup in the resolver, so the
        // invalidator must recognise the settings node by key too — even when its content type
        // alias differs from the configured one.
        var key = Guid.NewGuid();
        var target = MakeContent("globalConfig", key);
        var options = Options.Create(new SchemeWeaverOptions
        {
            SiteSettings = new SiteSettingsOptions { ContentTypeAlias = "somethingElse", ContentKey = key },
        });
        var handler = new InvalidateJsonLdCacheOnPublish(_provider, _repo, options,
            NullLogger<InvalidateJsonLdCacheOnPublish>.Instance);

        using var messages = new EventMessages();
        handler.Handle(new ContentPublishedNotification(target, messages));

        _provider.Received(1).Invalidate(target.Key);
        _provider.Received(1).InvalidateAll();
    }

    [Fact]
    public void SiteScopeGraph_ReflectsSettingsChange_AfterSettingsPublish_WithoutRestart()
    {
        // End-to-end across the real seam: real provider + real MemoryCache, with a substituted
        // graph generator standing in for "the settings node's current published values".
        // Shape: generate site graph → publish the settings node → generate again WITHOUT
        // restarting → second render must reflect the new value.
        var graphGenerator = Substitute.For<IGraphGenerator>();
        var currentGraph = /*lang=json,strict*/ """{"@context":"https://schema.org","@graph":[{"@type":"Organization"}]}""";
        graphGenerator
            .GenerateGraphJson(Arg.Any<IPublishedContent>(), Arg.Any<string?>(), Arg.Any<PieceScopeFilter>())
            .Returns(_ => currentGraph);

        var services = new ServiceCollection();
        services.AddSingleton(graphGenerator);
        using var serviceProvider = services.BuildServiceProvider();

        using var provider = new JsonLdBlocksProvider(
            serviceProvider.GetRequiredService<IServiceScopeFactory>(),
            new MemoryCache(new MemoryCacheOptions()),
            Options.Create(new SchemeWeaverOptions()), // UseGraphModel = true (default)
            NullLogger<JsonLdBlocksProvider>.Instance);

        var routedPage = Substitute.For<IPublishedContent>();
        routedPage.Key.Returns(Guid.NewGuid());

        provider.GetBlocks(routedPage, culture: null, PieceScopeFilter.Site)
            .Should().ContainSingle().Which.Should().NotContain("logo");

        // An editor populates the logo on the site-settings node and publishes it.
        currentGraph = /*lang=json,strict*/ """{"@context":"https://schema.org","@graph":[{"@type":"Organization","logo":{"@type":"ImageObject"}}]}""";
        var settingsNode = MakeContent("schemaSiteSettings");
        var handler = new InvalidateJsonLdCacheOnPublish(provider, _repo, _options,
            NullLogger<InvalidateJsonLdCacheOnPublish>.Instance);
        using var messages = new EventMessages();
        handler.Handle(new ContentPublishedNotification(settingsNode, messages));

        provider.GetBlocks(routedPage, culture: null, PieceScopeFilter.Site)
            .Should().ContainSingle().Which.Should().Contain("logo");
    }

    [Fact]
    public void MappingLookupThrows_IsSwallowed_TargetStillEvicted_AndRipplesToBeSafe()
    {
        var target = MakeContent("article");
        _repo.GetByContentTypeAlias("article").Returns(_ => throw new InvalidOperationException("db unavailable"));

        var handler = new InvalidateJsonLdCacheOnPublish(_provider, _repo, _options,
            NullLogger<InvalidateJsonLdCacheOnPublish>.Instance);

        using var messages = new EventMessages();
        var act = () => handler.Handle(new ContentPublishedNotification(target, messages));

        act.Should().NotThrow();
        _provider.Received(1).Invalidate(target.Key);
        _provider.Received(1).InvalidateAll(); // over-invalidate rather than risk stale JSON-LD
    }
}
