using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;
using Umbraco.Cms.Core.Composing;
using Umbraco.Cms.Core.DependencyInjection;
using Umbraco.Cms.Core.Events;
using Umbraco.Cms.Core.Notifications;
using Umbraco.Cms.Core.Services;
using Umbraco.Community.SchemeWeaver.Models.Api;
using Umbraco.Community.SchemeWeaver.Services;
using Umbraco.Community.SchemeWeaver.Services.ValueSchemas;
using Umbraco.Community.SchemeWeaver.TypeSafe.Client;
using Umbraco.Community.SchemeWeaver.TypeSafe.Composing;
using Umbraco.Community.SchemeWeaver.TypeSafe.Configuration;
using Umbraco.Community.SchemeWeaver.TypeSafe.Notifications;
using Umbraco.Community.SchemeWeaver.TypeSafe.Services;
using Xunit;

namespace Umbraco.Community.SchemeWeaver.Tests.Unit.TypeSafe;

/// <summary>
/// <see cref="SchemeWeaverTypeSafeComposer"/> against a plain <see cref="ServiceCollection"/>
/// and an <see cref="UmbracoBuilder"/> built the way the Deploy composer tests build one — no
/// Umbraco boot. The point is the decoration: whatever <see cref="ISchemaAutoMapper"/> was
/// registered before the composer runs must come back wrapped, reachable as the prior, at its
/// original lifetime.
/// </summary>
public class SchemeWeaverTypeSafeComposerTests
{
    /// <summary>Stands in for the core heuristic (or the AI satellite's mapper) on the seam.</summary>
    private sealed class StubAutoMapper : ISchemaAutoMapper
    {
        public static readonly PropertyMappingSuggestion Marker = new() { SchemaPropertyName = "FromStub", Confidence = 100 };

        public IEnumerable<PropertyMappingSuggestion> SuggestMappings(string contentTypeAlias, string schemaTypeName) => [Marker];

        public Task<IEnumerable<PropertyMappingSuggestion>> SuggestMappingsAsync(string contentTypeAlias, string schemaTypeName)
            => Task.FromResult<IEnumerable<PropertyMappingSuggestion>>([Marker]);

        public IEnumerable<RankedSchemaPropertyInfo> RankSchemaProperties(string schemaTypeName) => [];
    }

    private static IServiceCollection Compose(Action<IServiceCollection> registerSeam, IReadOnlyDictionary<string, string?>? settings = null)
    {
        var services = new ServiceCollection();
        var config = new ConfigurationBuilder().AddInMemoryCollection(settings ?? new Dictionary<string, string?>()).Build();

        var builder = new UmbracoBuilder(
            services,
            config,
            new TypeLoader(Substitute.For<ITypeFinder>(), Substitute.For<ILogger<TypeLoader>>()));

        // What the host would have registered before any composer runs. These go in AFTER the
        // builder's own core registrations (which include real Umbraco services such as
        // ContentTypeService) so the test doubles are the ones the container resolves.
        services.AddSingleton<IConfiguration>(config);
        services.AddLogging();
        services.RemoveAll<ISchemaTypeRegistry>();
        services.AddSingleton(Substitute.For<ISchemaTypeRegistry>());
        services.RemoveAll<IContentTypeService>();
        services.AddSingleton(Substitute.For<IContentTypeService>());
        // The core composer (not run here) registers the value-schema service the v2 mapper injects.
        services.AddSingleton(Substitute.For<IPropertyValueSchemaService>());
        registerSeam(services);

        new SchemeWeaverTypeSafeComposer().Compose(builder);
        return services;
    }

    [Fact]
    public void Compose_WrapsATypeRegisteredSeam_KeepingItAsThePrior()
    {
        var services = Compose(s => s.AddScoped<ISchemaAutoMapper, StubAutoMapper>());

        services.Where(d => d.ServiceType == typeof(ISchemaAutoMapper)).Should().ContainSingle()
            .Which.Lifetime.Should().Be(ServiceLifetime.Scoped, "the original lifetime is preserved");

        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var mapper = scope.ServiceProvider.GetRequiredService<ISchemaAutoMapper>();

        var decorator = mapper.Should().BeOfType<TypeSafeSchemaAutoMapper>().Subject;
        decorator.Prior.Should().BeOfType<StubAutoMapper>();
        decorator.PriorTypeName.Should().Be(nameof(StubAutoMapper));
    }

    [Fact]
    public void Compose_WrapsAFactoryRegisteredSeam_TheAiSatelliteShape()
    {
        var services = Compose(s => s.AddScoped<ISchemaAutoMapper>(_ => new StubAutoMapper()));

        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var mapper = scope.ServiceProvider.GetRequiredService<ISchemaAutoMapper>();

        mapper.Should().BeOfType<TypeSafeSchemaAutoMapper>().Which.Prior.Should().BeOfType<StubAutoMapper>();
    }

    [Fact]
    public void Compose_WithoutASeam_WrapsTheHeuristicDirectly()
    {
        var services = Compose(_ => { });

        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var mapper = scope.ServiceProvider.GetRequiredService<ISchemaAutoMapper>();

        mapper.Should().BeOfType<TypeSafeSchemaAutoMapper>().Which.Prior.Should().BeOfType<SchemaAutoMapper>();
    }

    [Fact]
    public async Task ResolvedDecorator_WithoutAnApiKey_AnswersFromThePrior()
    {
        // End to end through the container: the decorator, the real TypeSafePropertyMapper, the
        // typed HttpClient and the options all resolve, and with no key the call never leaves
        // the prior.
        var services = Compose(s => s.AddScoped<ISchemaAutoMapper, StubAutoMapper>());

        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var mapper = scope.ServiceProvider.GetRequiredService<ISchemaAutoMapper>();

        scope.ServiceProvider.GetRequiredService<ITypeSafeClient>().IsConfigured.Should().BeFalse();
        var result = await mapper.SuggestMappingsAsync("blogPost", "BlogPosting");
        result.Should().ContainSingle().Which.Should().BeSameAs(StubAutoMapper.Marker);
    }

    [Fact]
    public void Compose_RegistersTheSatelliteServices()
    {
        var services = Compose(s => s.AddScoped<ISchemaAutoMapper, StubAutoMapper>());

        services.Should().Contain(d => d.ServiceType == typeof(ITypeSafeClient));
        services.Should().Contain(d => d.ServiceType == typeof(ISchemaTypeGraph) && d.ImplementationType == typeof(SchemaTypeGraph) && d.Lifetime == ServiceLifetime.Singleton);
        services.Should().Contain(d => d.ServiceType == typeof(ITypeSafePropertyMapper) && d.ImplementationType == typeof(TypeSafePropertyMapper) && d.Lifetime == ServiceLifetime.Scoped);
        services.Should().Contain(d => d.ServiceType == typeof(ITypeSafeSchemaTypeSuggester) && d.ImplementationType == typeof(TypeSafeSchemaTypeSuggester) && d.Lifetime == ServiceLifetime.Scoped);
        services.Should().Contain(d => d.ServiceType == typeof(SchemaAutoMapper) && d.Lifetime == ServiceLifetime.Scoped);
        services.Should().Contain(d =>
            d.ServiceType == typeof(INotificationHandler<UmbracoApplicationStartedNotification>) &&
            d.ImplementationType == typeof(LogTypeSafeStartupStatus));
    }

    [Fact]
    public void Compose_DoesNotDoubleRegisterTheConcreteHeuristic()
    {
        // The AI satellite registers SchemaAutoMapper itself; TryAdd must leave it alone.
        var services = Compose(s =>
        {
            s.AddScoped<SchemaAutoMapper>();
            s.AddScoped<ISchemaAutoMapper, StubAutoMapper>();
        });

        services.Where(d => d.ServiceType == typeof(SchemaAutoMapper)).Should().ContainSingle();
    }

    [Fact]
    public void Options_BindFromConfigurationAndValidateRanges()
    {
        var services = Compose(
            s => s.AddScoped<ISchemaAutoMapper, StubAutoMapper>(),
            new Dictionary<string, string?>
            {
                ["SchemeWeaver:TypeSafe:ApiKey"] = "key",
                ["SchemeWeaver:TypeSafe:Model"] = "jev-1.13.0",
                ["SchemeWeaver:TypeSafe:MaxRetries"] = "99",
            });

        using var provider = services.BuildServiceProvider();
        var act = () => provider.GetRequiredService<IOptions<TypeSafeOptions>>().Value;

        act.Should().Throw<OptionsValidationException>().WithMessage("*MaxRetries*");
    }

    [Fact]
    public void Options_ValidDefaults_Bind()
    {
        var services = Compose(
            s => s.AddScoped<ISchemaAutoMapper, StubAutoMapper>(),
            new Dictionary<string, string?> { ["SchemeWeaver:TypeSafe:Model"] = "jev-1.13.0" });

        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<TypeSafeOptions>>().Value;

        options.Model.Should().Be("jev-1.13.0");
        options.Enabled.Should().BeTrue();
        options.ApiKey.Should().BeNull("a missing key is not a validation failure: the package boots inert");
        options.BeamWidth.Should().Be(3);
    }

    /// <summary>
    /// The typed client's factory logging handlers write every request header at Trace level;
    /// the Authorization header carries the key, so it must be registered for redaction.
    /// </summary>
    [Fact]
    public void Compose_RedactsTheAuthorizationHeaderFromHttpClientLogging()
    {
        var services = Compose(s => s.AddScoped<ISchemaAutoMapper, StubAutoMapper>());

        using var provider = services.BuildServiceProvider();
        var factoryOptions = provider
            .GetRequiredService<IOptionsMonitor<Microsoft.Extensions.Http.HttpClientFactoryOptions>>()
            .Get(nameof(ITypeSafeClient));

        factoryOptions.ShouldRedactHeaderValue("Authorization").Should().BeTrue();
        factoryOptions.ShouldRedactHeaderValue("Content-Type").Should().BeFalse();
    }

    [Theory]
    [InlineData("https://api.typesafe.ai/v1/systemone", true)]
    [InlineData("http://localhost:5555/v1/systemone", true)]
    [InlineData("http://127.0.0.1:5555/v1/systemone", true)]
    [InlineData("http://example.com/v1/systemone", false)]
    [InlineData("not a url", false)]
    public void Options_Endpoint_AllowsHttpsOrLoopbackHttpOnly(string endpoint, bool valid)
    {
        var services = Compose(
            s => s.AddScoped<ISchemaAutoMapper, StubAutoMapper>(),
            new Dictionary<string, string?> { ["SchemeWeaver:TypeSafe:Endpoint"] = endpoint });

        using var provider = services.BuildServiceProvider();
        var act = () => provider.GetRequiredService<IOptions<TypeSafeOptions>>().Value;

        if (valid)
            act.Should().NotThrow("a local test double or dev proxy on loopback must not stop the site booting");
        else
            act.Should().Throw<OptionsValidationException>().WithMessage("*Endpoint*");
    }

    /// <summary>
    /// v2 option ranges. Each is one value outside its documented range (both ends where a lower
    /// bound exists) and the failure must name the option, so a misconfigured site fails at
    /// startup with a message that says which key to fix.
    /// </summary>
    [Theory]
    [InlineData("SecondaryBindingMinProbability", "1.5")]
    [InlineData("SecondaryBindingMinProbability", "-0.1")]
    [InlineData("MaxStateCharacters", "0")]
    [InlineData("MaxStateCharacters", "-1")]
    [InlineData("MaxAncestorDepth", "0")]
    [InlineData("MaxAncestorDepth", "7")]
    [InlineData("MaxNeighbourTypes", "0")]
    [InlineData("MaxNeighbourTypes", "41")]
    [InlineData("MaxPropertiesPerNeighbour", "0")]
    [InlineData("MaxPropertiesPerNeighbour", "101")]
    [InlineData("MaxNeighbourProperties", "0")]
    [InlineData("MaxNeighbourProperties", "255")]
    [InlineData("MaxSampledNodes", "0")]
    [InlineData("MaxSampledNodes", "201")]
    [InlineData("MinObservedShare", "-0.1")]
    [InlineData("MinObservedShare", "1.1")]
    [InlineData("MaxCrossNodeQuestions", "-1")]
    [InlineData("MaxCrossNodeQuestions", "101")]
    [InlineData("MinCrossNodeConfidence", "-1")]
    [InlineData("MinCrossNodeConfidence", "101")]
    [InlineData("MaxBlockRouteDepth", "0")]
    [InlineData("MaxBlockRouteDepth", "4")]
    public void Options_V2Ranges_AreValidated(string option, string value)
    {
        var services = Compose(
            s => s.AddScoped<ISchemaAutoMapper, StubAutoMapper>(),
            new Dictionary<string, string?> { [$"SchemeWeaver:TypeSafe:{option}"] = value });

        using var provider = services.BuildServiceProvider();
        var act = () => provider.GetRequiredService<IOptions<TypeSafeOptions>>().Value;

        act.Should().Throw<OptionsValidationException>().WithMessage($"*{option}*");
    }

    [Theory]
    [InlineData("MaxNeighbourProperties", "254")]
    [InlineData("MaxBlockRouteDepth", "3")]
    [InlineData("MaxAncestorDepth", "6")]
    [InlineData("MaxCrossNodeQuestions", "0")]
    [InlineData("MinObservedShare", "0")]
    [InlineData("SecondaryBindingMinProbability", "1")]
    [InlineData("MaxStateCharacters", "1")]
    public void Options_V2Ranges_AcceptTheirBoundaries(string option, string value)
    {
        var services = Compose(
            s => s.AddScoped<ISchemaAutoMapper, StubAutoMapper>(),
            new Dictionary<string, string?> { [$"SchemeWeaver:TypeSafe:{option}"] = value });

        using var provider = services.BuildServiceProvider();
        var act = () => provider.GetRequiredService<IOptions<TypeSafeOptions>>().Value;

        act.Should().NotThrow();
    }

    [Fact]
    public void Options_V2Defaults_BindAndEnumsParse()
    {
        var services = Compose(
            s => s.AddScoped<ISchemaAutoMapper, StubAutoMapper>(),
            new Dictionary<string, string?>
            {
                ["SchemeWeaver:TypeSafe:RoutesMode"] = "Always",
                ["SchemeWeaver:TypeSafe:NeighbourhoodDiscovery"] = "Observed",
            });

        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<TypeSafeOptions>>().Value;

        options.RoutesMode.Should().Be(TypeSafeRoutesMode.Always);
        options.NeighbourhoodDiscovery.Should().Be(TypeSafeNeighbourhoodDiscovery.Observed);

        var defaults = new TypeSafeOptions();
        defaults.SecondaryBindingMinProbability.Should().Be(0.30);
        defaults.IncludeSampleValues.Should().BeFalse("sample values are customer content and leave the site: opt-in");
        defaults.MaxStateCharacters.Should().Be(100_000, "roughly 25k tokens at four characters a token, under the 32k state cap");
        defaults.EnableCrossNodeSources.Should().BeTrue();
        defaults.NeighbourhoodDiscovery.Should().Be(TypeSafeNeighbourhoodDiscovery.Both);
        defaults.MaxAncestorDepth.Should().Be(3);
        defaults.MaxNeighbourTypes.Should().Be(12);
        defaults.MaxPropertiesPerNeighbour.Should().Be(30);
        defaults.MaxNeighbourProperties.Should().Be(120);
        defaults.MaxSampledNodes.Should().Be(25);
        defaults.MinObservedShare.Should().Be(0.5);
        defaults.MaxCrossNodeQuestions.Should().Be(12);
        defaults.MinCrossNodeConfidence.Should().Be(60, "the core's show bar: cross-node rows are offered like any other suggestion");
        defaults.RoutesMode.Should().Be(TypeSafeRoutesMode.Auto);
        defaults.MaxBlockRouteDepth.Should().Be(3);
        defaults.ToString().Should().Contain("RoutesMode=Auto").And.Contain("MinCrossNodeConfidence=60");
    }

    [Fact]
    public void Options_NeverSerialiseOrPrintTheApiKey()
    {
        var options = new TypeSafeOptions { ApiKey = "sk-very-secret-key", Model = "jev-1.13.0" };

        System.Text.Json.JsonSerializer.Serialize(options).Should().NotContain("very-secret");
        options.ToString().Should().NotContain("very-secret").And.Contain("ApiKey=set");
        new TypeSafeOptions().ToString().Should().Contain("ApiKey=unset");
    }
}
