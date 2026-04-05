using Xunit;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Umbraco.Community.SchemeWeaver.AI.Models;
using Umbraco.Community.SchemeWeaver.AI.Services;
using Umbraco.Community.SchemeWeaver.AI.Tools;
using Umbraco.Community.SchemeWeaver.Models.Api;
using Umbraco.Community.SchemeWeaver.Services;

namespace Umbraco.Community.SchemeWeaver.Tests.Unit;

public class AIToolTests
{
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

    [Fact]
    public async Task SuggestSchemaTypeTool_ReturnsSuccessResult()
    {
        var mapper = Substitute.For<IAISchemaMapper>();
        mapper.SuggestSchemaTypesAsync("blogPost", Arg.Any<CancellationToken>())
            .Returns(new[]
            {
                new SchemaTypeSuggestion { SchemaTypeName = "BlogPosting", Confidence = 95, Reasoning = "Matches blog structure" }
            });

        var factory = CreateScopeFactory(sp =>
            sp.GetService(typeof(IAISchemaMapper)).Returns(mapper));

        var tool = new SuggestSchemaTypeTool(factory);
        var result = await ((Umbraco.AI.Core.Tools.IAITool)tool).ExecuteAsync(
            System.Text.Json.JsonSerializer.SerializeToElement(new { ContentTypeAlias = "blogPost" }));

        result.Should().BeOfType<SuggestSchemaTypeResult>();
        var typed = (SuggestSchemaTypeResult)result;
        typed.Success.Should().BeTrue();
        typed.Suggestions.Should().HaveCount(1);
        typed.Suggestions![0].SchemaTypeName.Should().Be("BlogPosting");
    }

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
                }
            }
        });

        var factory = CreateScopeFactory(sp =>
            sp.GetService(typeof(ISchemeWeaverService)).Returns(service));

        var tool = new ListSchemaMappingsTool(factory);
        var result = await ((Umbraco.AI.Core.Tools.IAITool)tool).ExecuteAsync(null);

        result.Should().BeOfType<ListSchemaMappingsResult>();
        var typed = (ListSchemaMappingsResult)result;
        typed.Success.Should().BeTrue();
        typed.Mappings.Should().HaveCount(1);
        typed.Mappings![0].ContentTypeAlias.Should().Be("blogPost");
        typed.Mappings![0].PropertyCount.Should().Be(2);
    }

    [Fact]
    public async Task MapSchemaPropertiesTool_DelegatesToMapper()
    {
        var mapper = Substitute.For<IAISchemaMapper>();
        mapper.SuggestPropertyMappingsAsync("blogPost", "BlogPosting", Arg.Any<CancellationToken>())
            .Returns(new[]
            {
                new PropertyMappingSuggestion { SchemaPropertyName = "headline", Confidence = 90 }
            });

        var factory = CreateScopeFactory(sp =>
            sp.GetService(typeof(IAISchemaMapper)).Returns(mapper));

        var tool = new MapSchemaPropertiesTool(factory);
        var result = await ((Umbraco.AI.Core.Tools.IAITool)tool).ExecuteAsync(
            System.Text.Json.JsonSerializer.SerializeToElement(
                new { ContentTypeAlias = "blogPost", SchemaTypeName = "BlogPosting" }));

        result.Should().BeOfType<MapSchemaPropertiesResult>();
        var typed = (MapSchemaPropertiesResult)result;
        typed.Success.Should().BeTrue();
        typed.Suggestions.Should().Contain(s => s.SchemaPropertyName == "headline");
    }
}
