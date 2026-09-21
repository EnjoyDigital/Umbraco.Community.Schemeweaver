using System.Net;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Umbraco.Community.SchemeWeaver.Services;
using Umbraco.Community.SchemeWeaver.Tests.Integration.Fixtures;
using Umbraco.Community.SchemeWeaver.TypeSafe.Client;
using Umbraco.Community.SchemeWeaver.TypeSafe.Configuration;
using Umbraco.Community.SchemeWeaver.TypeSafe.Services;
using Xunit;

namespace Umbraco.Community.SchemeWeaver.Tests.Integration;

/// <summary>
/// The never-breaks guarantee on the shared integration host: the TypeSafe satellite is
/// composed into the TestHost but disabled by the fixture, so auto-map must still answer
/// 200 with the heuristic's suggestions, exactly as it did before the satellite existed.
/// </summary>
[Collection(SchemeWeaverIntegrationCollection.Name)]
public class TypeSafeDisabledAutoMapTests : UmbracoIntegrationTestBase
{
    private const string BaseRoute = "/umbraco/management/api/v1/schemeweaver";
    private const string ProbeAlias = "typeSafeProbePage";

    public TypeSafeDisabledAutoMapTests(SchemeWeaverWebApplicationFactory factory)
        : base(factory)
    {
    }

    [Fact]
    public void SharedHost_HasTypeSafeComposedButInert()
    {
        using var scope = CreateServiceScope();

        scope.ServiceProvider.GetRequiredService<IOptions<TypeSafeOptions>>().Value.Enabled.Should().BeFalse(
            "the fixture pins SchemeWeaver:TypeSafe:Enabled=false so a developer's user-secrets key can never leak in");
        scope.ServiceProvider.GetRequiredService<ITypeSafeClient>().IsConfigured.Should().BeFalse();
    }

    /// <summary>
    /// Composer ordering, proven on a booted host. The TestHost composes BOTH satellites, and the
    /// AI composer replaces the seam outright, so without an ordering rule which one ended up on
    /// the seam depended on type-scan order (and TypeSafe lost, silently). The AI composer now
    /// implements the core's <c>ISchemaAutoMapperReplacingComposer</c> marker and the TypeSafe
    /// composer declares <c>[ComposeAfter]</c> on that interface, so the seam must be the TypeSafe
    /// decorator wrapping the AI mapper as its prior: the cascade TypeSafe, then AI, then heuristic.
    /// </summary>
    [Fact]
    public void SharedHost_SeamIsTypeSafeWrappingTheAiMapper()
    {
        using var scope = CreateServiceScope();

        var seam = scope.ServiceProvider.GetRequiredService<ISchemaAutoMapper>();

        var decorator = seam.Should().BeOfType<TypeSafeSchemaAutoMapper>(
            "the TypeSafe composer is ordered after every seam-replacing composer, so it wraps the AI mapper").Subject;
        decorator.PriorTypeName.Should().Be("AiSchemaAutoMapper",
            "with both satellites installed the AI mapper is the fallback, and the heuristic sits inside it");
    }

    [Fact]
    public async Task AutoMap_WithTypeSafeDisabled_StillReturnsHeuristicSuggestions()
    {
        await ProbeContentTypes.EnsureAsync(Factory.Services, ProbeAlias, "TypeSafe Probe Page", ("headline", "Headline"));

        var response = await Client.PostAsync(
            $"{BaseRoute}/mappings/{ProbeAlias}/auto-map?schemaTypeName=BlogPosting",
            content: null);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        doc.RootElement.ValueKind.Should().Be(JsonValueKind.Array);

        var headline = doc.RootElement.EnumerateArray()
            .Should().ContainSingle(e => string.Equals(e.GetProperty("schemaPropertyName").GetString(), "headline", StringComparison.OrdinalIgnoreCase))
            .Subject;
        headline.GetProperty("suggestedContentTypePropertyAlias").GetString().Should().Be("headline");
        headline.GetProperty("confidence").GetInt32().Should().Be(100, "an exact alias match is the heuristic's top tier");
        headline.GetProperty("isAutoMapped").GetBoolean().Should().BeTrue();
        headline.GetProperty("suggestedSourceType").GetString().Should().Be("property");
    }
}
