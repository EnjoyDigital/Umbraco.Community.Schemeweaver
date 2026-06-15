#if !UMBRACO18
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Umbraco.AI.Core.Tools;
using Umbraco.Community.SchemeWeaver.AI.Models;
using Umbraco.Community.SchemeWeaver.AI.Services;
using Umbraco.Community.SchemeWeaver.AI.Tools;
using Umbraco.Community.SchemeWeaver.Models.Api;
using Umbraco.Community.SchemeWeaver.Services;
using Xunit;

namespace Umbraco.Community.SchemeWeaver.Tests.Unit;

/// <summary>
/// Unit tests for the Umbraco.AI Copilot tools exposed by SchemeWeaver.AI.
///
/// Each tool resolves its service dependencies via an <see cref="IServiceScopeFactory"/>,
/// which is mocked here to return substituted services without requiring a full DI container.
///
/// These tests are 17-only because <c>Umbraco.AI.Core</c> has no Umbraco 18 build.
/// </summary>
public class AIToolTests
{
    // -----------------------------------------------------------------------
    // Helper: build a mock IServiceScopeFactory that resolves one service
    // -----------------------------------------------------------------------

    private static IServiceScopeFactory CreateScopeFactory(Action<IServiceProvider> configure)
    {
        var sp = Substitute.For<IServiceProvider>();
        configure(sp);
        var scope = Substitute.For<IServiceScope>();
        scope.ServiceProvider.Returns(sp);
        var factory = Substitute.For<IServiceScopeFactory>();
        factory.CreateScope().Returns(scope);
        return factory;
    }

    // -----------------------------------------------------------------------
    // SuggestSchemaTypeTool
    // -----------------------------------------------------------------------

    [Fact]
    public async Task SuggestSchemaTypeTool_ReturnsSuccessResult()
    {
        var mapper = Substitute.For<IAISchemaMapper>();
        mapper.SuggestSchemaTypesAsync("blogPost", Arg.Any<CancellationToken>())
            .Returns(new[]
            {
                new SchemaTypeSuggestion
                {
                    SchemaTypeName = "BlogPosting",
                    Confidence = 95,
                    Reasoning = "Matches blog structure",
                },
            });

        var factory = CreateScopeFactory(sp =>
            sp.GetService(typeof(IAISchemaMapper)).Returns(mapper));

        var tool = new SuggestSchemaTypeTool(factory);

        // Cast to IAITool and invoke via the explicit interface method.
        // The generic base deserialises the JsonElement arg into SuggestSchemaTypeArgs.
        var args = JsonSerializer.SerializeToElement(new { ContentTypeAlias = "blogPost" });
        var result = await ((IAITool)tool).ExecuteAsync(args, CancellationToken.None);

        result.Should().BeOfType<SuggestSchemaTypeResult>();
        var typed = (SuggestSchemaTypeResult)result;
        typed.Success.Should().BeTrue();
        typed.Suggestions.Should().HaveCount(1);
        typed.Suggestions![0].SchemaTypeName.Should().Be("BlogPosting");
    }

    // -----------------------------------------------------------------------
    // ListSchemaMappingsTool
    // -----------------------------------------------------------------------

    [Fact]
    public async Task ListSchemaMappingsTool_ReturnsExistingMappings()
    {
        var service = Substitute.For<ISchemeWeaverService>();
        service.GetAllMappings().Returns(new[]
        {
            new SchemaMappingDto
            {
                ContentTypeAlias = "blogPost",
                SchemaTypeName = "BlogPosting",
                IsEnabled = true,
                PropertyMappings = new List<PropertyMappingDto>
                {
                    new() { SchemaPropertyName = "headline" },
                    new() { SchemaPropertyName = "articleBody" },
                },
            },
        });

        var factory = CreateScopeFactory(sp =>
            sp.GetService(typeof(ISchemeWeaverService)).Returns(service));

        var tool = new ListSchemaMappingsTool(factory);

        // ListSchemaMappingsTool extends the non-generic AIToolBase, which ignores
        // the args parameter — passing null is the correct call for argument-less tools.
        var result = await ((IAITool)tool).ExecuteAsync(null!, CancellationToken.None);

        result.Should().BeOfType<ListSchemaMappingsResult>();
        var typed = (ListSchemaMappingsResult)result;
        typed.Success.Should().BeTrue();
        typed.Mappings.Should().HaveCount(1);
        typed.Mappings![0].ContentTypeAlias.Should().Be("blogPost");
        typed.Mappings![0].PropertyCount.Should().Be(2);
    }

    // -----------------------------------------------------------------------
    // MapSchemaPropertiesTool
    // -----------------------------------------------------------------------

    [Fact]
    public async Task MapSchemaPropertiesTool_DelegatesToMapper()
    {
        var mapper = Substitute.For<IAISchemaMapper>();
        mapper.SuggestPropertyMappingsAsync("blogPost", "BlogPosting", Arg.Any<CancellationToken>())
            .Returns(new[]
            {
                new PropertyMappingSuggestion
                {
                    SchemaPropertyName = "headline",
                    Confidence = 90,
                },
            });

        var factory = CreateScopeFactory(sp =>
            sp.GetService(typeof(IAISchemaMapper)).Returns(mapper));

        var tool = new MapSchemaPropertiesTool(factory);

        var args = JsonSerializer.SerializeToElement(
            new { ContentTypeAlias = "blogPost", SchemaTypeName = "BlogPosting" });
        var result = await ((IAITool)tool).ExecuteAsync(args, CancellationToken.None);

        result.Should().BeOfType<MapSchemaPropertiesResult>();
        var typed = (MapSchemaPropertiesResult)result;
        typed.Success.Should().BeTrue();
        typed.Suggestions.Should().Contain(s => s.SchemaPropertyName == "headline");
    }
}
#endif
