using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Umbraco.Cms.Core.Services;
using Umbraco.Community.SchemeWeaver.Services;
using Umbraco.Community.SchemeWeaver.Services.ValueSchemas;
using Umbraco.Community.SchemeWeaver.Tests.Integration.Fixtures;
using Umbraco.Community.SchemeWeaver.Tests.Unit.TypeSafe.TestSupport;
using Umbraco.Community.SchemeWeaver.TypeSafe.Client;
using Umbraco.Community.SchemeWeaver.TypeSafe.Configuration;
using Umbraco.Community.SchemeWeaver.TypeSafe.Services;
using Xunit;

namespace Umbraco.Community.SchemeWeaver.Tests.Integration;

/// <summary>
/// The decorated seam over the SHARED host's real services, answered by a canned in-process
/// client: proves the real property mapper, the real registry and type graph, the lazy
/// <c>ISchemeWeaverService</c> resolution and the priors merge all line up on a booted Umbraco,
/// and that the result is TypeSafe-shaped rather than heuristic-shaped.
/// </summary>
/// <remarks>
/// <para>
/// This deliberately boots NO second <c>WebApplicationFactory</c>. The first version of this
/// test did (a subclass of the collection fixture with TypeSafe enabled), and inside the full
/// suite its unattended-upgrade background service sat on a database write lock for exactly
/// five minutes and failed, every time, while passing in isolation: a second host alongside the
/// collection host is the one-host-per-suite conflict <see cref="SchemeWeaverIntegrationCollection"/>
/// exists to prevent. The shared host pins <c>SchemeWeaver:TypeSafe:Enabled=false</c>, so the
/// decorated stack is assembled here from the host's own scoped services with the fake client
/// and enabled options; the HTTP layer (endpoint to <c>SchemeWeaverService</c> to the seam to
/// the DTOs) is covered by <see cref="TypeSafeDisabledAutoMapTests"/> on the same host.
/// </para>
/// </remarks>
[Collection(SchemeWeaverIntegrationCollection.Name)]
public class TypeSafeDecoratedSeamTests : UmbracoIntegrationTestBase
{
    /// <summary>Its own alias: <see cref="ProbeContentTypes.EnsureAsync"/> is idempotent, so a probe shared with another class would keep that class's properties.</summary>
    private const string ProbeAlias = "typeSafeSeamProbePage";

    public TypeSafeDecoratedSeamTests(SchemeWeaverWebApplicationFactory factory)
        : base(factory)
    {
    }

    [Fact]
    public async Task DecoratedSeam_OverTheHostsRealServices_ReturnsATypeSafeShapedSuggestion()
    {
        await ProbeContentTypes.EnsureAsync(Factory.Services, ProbeAlias, "TypeSafe Seam Probe Page", ("title", "Title"));

        using var scope = CreateServiceScope();
        var services = scope.ServiceProvider;

        var client = new FakeTypeSafeClient(Answer) { IsConfigured = true };
        var options = Options.Create(new TypeSafeOptions { Enabled = true, ApiKey = "test-key" });
        var heuristic = services.GetRequiredService<SchemaAutoMapper>();

        var mapper = new TypeSafePropertyMapper(
            client,
            services.GetRequiredService<ISchemaTypeGraph>(),
            services.GetRequiredService<ISchemaTypeRegistry>(),
            services.GetRequiredService<IContentTypeService>(),
            services.GetRequiredService<IPropertyValueSchemaService>(),
            services,
            options,
            services.GetRequiredService<IOptions<SchemaAutoMapperOptions>>(),
            NullLogger<TypeSafePropertyMapper>.Instance);

        var seam = new TypeSafeSchemaAutoMapper(
            mapper, client, prior: heuristic, heuristic, options, NullLogger<TypeSafeSchemaAutoMapper>.Instance);

        var rows = (await seam.SuggestMappingsAsync(ProbeAlias, "BlogPosting")).ToList();

        var headline = rows
            .Should().ContainSingle(r => string.Equals(r.SchemaPropertyName, "headline", StringComparison.OrdinalIgnoreCase))
            .Subject;
        headline.SuggestedContentTypePropertyAlias.Should().Be("title");
        headline.Confidence.Should().Be(91,
            "the calibrated 0.91 from the canned model, not the heuristic's synonym tier of 80");
        headline.IsAutoMapped.Should().BeTrue();
        headline.SuggestedSourceType.Should().Be("property");
        headline.EditorAlias.Should().Be("Umbraco.TextBox", "the editor comes from the real content type");

        client.Requests.Should().NotBeEmpty("the seam must have consulted TypeSafe");
        client.AskedIds.Should().Contain("bind__title");
        rows.Should().OnlyContain(r => r.SuggestedContentTypePropertyAlias == null
            || r.SuggestedContentTypePropertyAlias == "title"
            || r.SuggestedContentTypePropertyAlias!.StartsWith("__"),
            "every alias must come from the real content type: nothing is invented");
    }

    /// <summary>
    /// The canned model: binds the content property <c>title</c> to <c>Headline</c> at a
    /// calibrated 0.91 (a value no heuristic tier produces), says "none" to every other
    /// binding, and "plain value" to every entity question.
    /// </summary>
    private static SystemOneAnswer Answer(string id, SystemOneQuestion question)
    {
        if (question.Type == "noul")
            return FakeTypeSafeClient.Noul(0.03);

        if (id.StartsWith("bind__title", StringComparison.Ordinal)
            && FakeTypeSafeClient.OptionNamed(question, "Headline") is { } headline)
            return FakeTypeSafeClient.Choice(headline, 0.91);

        var options = FakeTypeSafeClient.CriteriaOf(question).Keys.ToList();
        if (options.Contains("__none"))
            return FakeTypeSafeClient.Choice("__none", 0.9);
        if (options.Contains("__stop"))
            return FakeTypeSafeClient.Choice("__stop", 0.9);
        return FakeTypeSafeClient.Choice(options[0], 0.9);
    }
}
