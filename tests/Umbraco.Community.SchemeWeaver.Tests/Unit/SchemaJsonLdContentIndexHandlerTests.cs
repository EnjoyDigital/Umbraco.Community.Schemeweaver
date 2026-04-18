using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;
using Umbraco.Cms.Core.Models;
using Umbraco.Cms.Core.Models.PublishedContent;
using Umbraco.Cms.Core.PublishedCache;
using Umbraco.Cms.Core.Web;
using Umbraco.Community.SchemeWeaver.DeliveryApi;
using Umbraco.Community.SchemeWeaver.Services;
using Xunit;

namespace Umbraco.Community.SchemeWeaver.Tests.Unit;

public class SchemaJsonLdContentIndexHandlerTests
{
    private readonly IJsonLdGenerator _generator = Substitute.For<IJsonLdGenerator>();
    private readonly IUmbracoContextAccessor _umbracoContextAccessor = Substitute.For<IUmbracoContextAccessor>();
    private readonly ILogger<SchemaJsonLdContentIndexHandler> _logger = Substitute.For<ILogger<SchemaJsonLdContentIndexHandler>>();

    private SchemaJsonLdContentIndexHandler CreateHandler(SchemeWeaverOptions? options = null)
    {
        var serviceProvider = Substitute.For<IServiceProvider>();
        serviceProvider.GetService(typeof(IJsonLdGenerator)).Returns(_generator);

        var scope = Substitute.For<IServiceScope>();
        scope.ServiceProvider.Returns(serviceProvider);

        var scopeFactory = Substitute.For<IServiceScopeFactory>();
        scopeFactory.CreateScope().Returns(scope);

        return new SchemaJsonLdContentIndexHandler(
            scopeFactory,
            _umbracoContextAccessor,
            _logger,
            Options.Create(options ?? new SchemeWeaverOptions()));
    }

    private (IContent content, IPublishedContent published) WireContent(Guid? key = null)
    {
        var contentKey = key ?? Guid.NewGuid();
        var content = Substitute.For<IContent>();
        content.Key.Returns(contentKey);

        var published = Substitute.For<IPublishedContent>();

        var contentCache = Substitute.For<IPublishedContentCache>();
        contentCache.GetById(contentKey).Returns(published);

        var umbracoContext = Substitute.For<IUmbracoContext>();
        umbracoContext.Content.Returns(contentCache);

        _umbracoContextAccessor.TryGetUmbracoContext(out Arg.Any<IUmbracoContext?>())
            .Returns(call =>
            {
                call[0] = umbracoContext;
                return true;
            });

        return (content, published);
    }

    [Fact]
    public void GetFieldValues_DefaultOptions_EmitsBreadcrumbBetweenInheritedAndMain()
    {
        var handler = CreateHandler();
        var (content, published) = WireContent();

        _generator.GenerateInheritedJsonLdStrings(published, Arg.Any<string?>())
            .Returns(["{\"@type\":\"WebSite\"}"]);
        _generator.GenerateBreadcrumbJsonLd(published, Arg.Any<string?>())
            .Returns("{\"@type\":\"BreadcrumbList\"}");
        _generator.GenerateJsonLdString(published, Arg.Any<string?>())
            .Returns("{\"@type\":\"Article\"}");
        _generator.GenerateBlockElementJsonLdStrings(published, Arg.Any<string?>())
            .Returns([]);

        var result = handler.GetFieldValues(content, culture: null).ToList();

        result.Should().HaveCount(1);
        result[0].FieldName.Should().Be("schemaOrg");
        result[0].Values.Should().ContainInOrder(
            "{\"@type\":\"WebSite\"}",
            "{\"@type\":\"BreadcrumbList\"}",
            "{\"@type\":\"Article\"}");
    }

    [Fact]
    public void GetFieldValues_OptOut_SkipsBreadcrumbButKeepsEverythingElse()
    {
        var handler = CreateHandler(new SchemeWeaverOptions { EmitBreadcrumbsInDeliveryApi = false });
        var (content, published) = WireContent();

        _generator.GenerateInheritedJsonLdStrings(published, Arg.Any<string?>())
            .Returns(["{\"@type\":\"WebSite\"}"]);
        _generator.GenerateJsonLdString(published, Arg.Any<string?>())
            .Returns("{\"@type\":\"Article\"}");
        _generator.GenerateBlockElementJsonLdStrings(published, Arg.Any<string?>())
            .Returns([]);

        var result = handler.GetFieldValues(content, culture: null).ToList();

        result.Should().HaveCount(1);
        result[0].Values.Should().BeEquivalentTo(new[]
        {
            "{\"@type\":\"WebSite\"}",
            "{\"@type\":\"Article\"}",
        });

        // Asserting the generator is *not* called is the real opt-out guarantee — a
        // buggy implementation could still pick up a breadcrumb via a stale cache.
        _generator.DidNotReceiveWithAnyArgs().GenerateBreadcrumbJsonLd(default!, default);
    }

    [Fact]
    public void GetFieldValues_RootNode_NoBreadcrumbStringEvenWithOptOn()
    {
        var handler = CreateHandler();
        var (content, published) = WireContent();

        // A root node legitimately produces no breadcrumb (fewer than 2 ancestors).
        _generator.GenerateInheritedJsonLdStrings(published, Arg.Any<string?>())
            .Returns([]);
        _generator.GenerateBreadcrumbJsonLd(published, Arg.Any<string?>())
            .Returns((string?)null);
        _generator.GenerateJsonLdString(published, Arg.Any<string?>())
            .Returns("{\"@type\":\"WebPage\"}");
        _generator.GenerateBlockElementJsonLdStrings(published, Arg.Any<string?>())
            .Returns([]);

        var result = handler.GetFieldValues(content, culture: null).ToList();

        result.Should().HaveCount(1);
        result[0].Values.Should().ContainSingle().Which.Should().Be("{\"@type\":\"WebPage\"}");
    }

    [Fact]
    public void GetFieldValues_NoPublishedContent_YieldsNothing()
    {
        var handler = CreateHandler();

        var content = Substitute.For<IContent>();
        content.Key.Returns(Guid.NewGuid());

        var contentCache = Substitute.For<IPublishedContentCache>();
        contentCache.GetById(Arg.Any<Guid>()).Returns((IPublishedContent?)null);

        var umbracoContext = Substitute.For<IUmbracoContext>();
        umbracoContext.Content.Returns(contentCache);
        _umbracoContextAccessor.TryGetUmbracoContext(out Arg.Any<IUmbracoContext?>())
            .Returns(call =>
            {
                call[0] = umbracoContext;
                return true;
            });

        var result = handler.GetFieldValues(content, culture: null).ToList();

        result.Should().BeEmpty();
    }
}
