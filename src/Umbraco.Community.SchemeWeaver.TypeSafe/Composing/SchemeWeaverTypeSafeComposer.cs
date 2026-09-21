using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Umbraco.Cms.Core.Composing;
using Umbraco.Cms.Core.DependencyInjection;
using Umbraco.Cms.Core.Notifications;
using Umbraco.Community.SchemeWeaver.Composing;
using Umbraco.Community.SchemeWeaver.Services;
using Umbraco.Community.SchemeWeaver.TypeSafe.Client;
using Umbraco.Community.SchemeWeaver.TypeSafe.Configuration;
using Umbraco.Community.SchemeWeaver.TypeSafe.HealthChecks;
using Umbraco.Community.SchemeWeaver.TypeSafe.Notifications;
using Umbraco.Community.SchemeWeaver.TypeSafe.Services;

namespace Umbraco.Community.SchemeWeaver.TypeSafe.Composing;

/// <summary>
/// Registers the TypeSafe satellite. Runs after <see cref="SchemeWeaverComposer"/> so its
/// <see cref="ISchemaAutoMapper"/> decoration wraps whatever was registered before it.
/// </summary>
/// <remarks>
/// <para>
/// <b>The seam.</b> Core registers <c>ISchemaAutoMapper -&gt; SchemaAutoMapper</c> (scoped) and
/// the request path awaits <c>SuggestMappingsAsync</c>. The AI satellite overrides that by
/// registering the interface again, last wins. This composer does it differently: it finds the
/// last <see cref="ISchemaAutoMapper"/> descriptor, removes it, and re-registers the interface
/// at the same lifetime as a factory that materialises the original registration and hands it
/// to <see cref="TypeSafeSchemaAutoMapper"/> as the <em>prior</em>. Whatever was there before
/// stays reachable as the fallback instead of being discarded.
/// </para>
/// <para>
/// <b>With the AI satellite installed too</b>, this composer always runs after it: the AI
/// composer replaces the seam outright and implements the core's
/// <see cref="ISchemaAutoMapperReplacingComposer"/> marker, and the
/// <c>[ComposeAfter]</c> on that interface (weak, so it is satisfied when no such composer
/// exists) orders this one after every composer that does. The result is a fixed cascade:
/// TypeSafe, then the AI mapper as its fallback, then the heuristic inside the AI mapper.
/// A third-party composer that replaces the seam without the marker would still take it, and
/// the startup log line names whatever mapper won. <c>SchemeWeaver:TypeSafe:Enabled=false</c>
/// is the explicit off switch in either arrangement.
/// </para>
/// <para>
/// <b>Booting inert is not an error.</b> Options are validated at startup for their numeric
/// ranges and the endpoint, but a missing API key is not a validation failure: the package must
/// boot without one and fall through to the prior on every call.
/// </para>
/// </remarks>
[ComposeAfter(typeof(SchemeWeaverComposer))]
[ComposeAfter(typeof(ISchemaAutoMapperReplacingComposer))]
public class SchemeWeaverTypeSafeComposer : IComposer
{
    public void Compose(IUmbracoBuilder builder)
    {
        builder.Services.AddOptions<TypeSafeOptions>()
            .BindConfiguration(TypeSafeOptions.SectionName)
            .Validate(
                o => o.MaxRetries is >= 0 and <= 10,
                "SchemeWeaver:TypeSafe:MaxRetries must be between 0 and 10.")
            .Validate(
                o => o.MaxQuestionsPerRequest is >= 1 and <= 50,
                "SchemeWeaver:TypeSafe:MaxQuestionsPerRequest must be between 1 and 50.")
            .Validate(
                o => o.BeamWidth is >= 1 and <= 10,
                "SchemeWeaver:TypeSafe:BeamWidth must be between 1 and 10.")
            .Validate(
                o => o.MaxOptionsPerChoice is >= 2 and <= 254,
                "SchemeWeaver:TypeSafe:MaxOptionsPerChoice must be between 2 and 254 (the API allows 255 and one slot is reserved for the \"none of these\" option).")
            .Validate(
                o => o.MaxDescentDepth is >= 1 and <= 10,
                "SchemeWeaver:TypeSafe:MaxDescentDepth must be between 1 and 10.")
            .Validate(
                o => o.MinBindingConfidence is >= 0 and <= 100,
                "SchemeWeaver:TypeSafe:MinBindingConfidence must be between 0 and 100.")
            .Validate(
                o => o.Timeout > TimeSpan.Zero,
                "SchemeWeaver:TypeSafe:Timeout must be a positive duration.")
            // https, or plain http on the loopback interface only: the option's own summary
            // invites pointing it at a local test double (WireMock, a dev proxy), and a
            // mapping-assist satellite must not refuse to boot the whole site over that.
            .Validate(
                o => Uri.TryCreate(o.Endpoint, UriKind.Absolute, out var endpoint)
                    && (endpoint.Scheme == Uri.UriSchemeHttps
                        || (endpoint.Scheme == Uri.UriSchemeHttp && endpoint.IsLoopback)),
                "SchemeWeaver:TypeSafe:Endpoint must be an absolute https URL (plain http is allowed on localhost only).")
            .ValidateOnStart();

        // Typed client over IHttpClientFactory: pooled handlers, a fresh HttpClient per
        // resolution, base address and timeout from options. The Bearer header is set per
        // request inside the client so the key never sits on a shared DefaultRequestHeaders,
        // and the factory's own Trace-level request logging is told to redact it: without
        // RedactLoggedHeaders a site that raises the System.Net.Http category to Trace would
        // write "Authorization: Bearer <key>" into its log verbatim.
        builder.Services.AddHttpClient<ITypeSafeClient, TypeSafeClient>((provider, http) =>
            {
                var options = provider.GetRequiredService<IOptions<TypeSafeOptions>>().Value;
                if (Uri.TryCreate(options.Endpoint, UriKind.Absolute, out var endpoint))
                {
                    http.BaseAddress = endpoint;
                }

                if (options.Timeout > TimeSpan.Zero)
                {
                    http.Timeout = options.Timeout;
                }
            })
            .RedactLoggedHeaders(["Authorization"]);

        // The type graph is derived once from the (singleton) registry and is immutable after
        // that, so it is a singleton too. The mapper and suggester depend on the scoped
        // IContentTypeService and are scoped to match, exactly like the core's own mapper.
        builder.Services.AddSingleton<ISchemaTypeGraph, SchemaTypeGraph>();
        builder.Services.AddScoped<ITypeSafePropertyMapper, TypeSafePropertyMapper>();
        builder.Services.AddScoped<ITypeSafeSchemaTypeSuggester, TypeSafeSchemaTypeSuggester>();

        // The concrete heuristic is needed as a PRIOR source whatever the seam holds: the
        // mapper keeps the heuristic's rule-driven rows (its popular-defaults shapes, its
        // cross-piece references, its exact-alias matches) and lets TypeSafe supply the rest.
        // The AI satellite registers the same concrete type; TryAdd keeps them from
        // double-registering.
        builder.Services.TryAddScoped<SchemaAutoMapper>();

        DecorateSchemaAutoMapper(builder.Services);

        // Health checks are also type-scanned (HealthCheck is IDiscoverable) and the lazy
        // collection builder de-duplicates, so the explicit registration is belt and braces.
        builder.HealthChecks().Add<TypeSafeHealthCheck>();

        builder.AddNotificationHandler<UmbracoApplicationStartedNotification, LogTypeSafeStartupStatus>();
    }

    /// <summary>
    /// Wraps the current <see cref="ISchemaAutoMapper"/> registration in
    /// <see cref="TypeSafeSchemaAutoMapper"/>, preserving its lifetime and keeping the original
    /// reachable as the prior. Handles all three descriptor shapes (type, factory, instance).
    /// </summary>
    private static void DecorateSchemaAutoMapper(IServiceCollection services)
    {
        var original = services.LastOrDefault(d => d.ServiceType == typeof(ISchemaAutoMapper) && !d.IsKeyedService);

        if (original is null)
        {
            // Cannot happen under [ComposeAfter(SchemeWeaverComposer)], but a missing seam must
            // not leave the site without an auto-mapper: wrap the heuristic directly.
            services.AddScoped<ISchemaAutoMapper>(provider =>
                CreateDecorator(provider, provider.GetRequiredService<SchemaAutoMapper>()));
            return;
        }

        services.Remove(original);
        services.Add(new ServiceDescriptor(
            typeof(ISchemaAutoMapper),
            provider => CreateDecorator(provider, Materialise(provider, original)),
            original.Lifetime));
    }

    private static TypeSafeSchemaAutoMapper CreateDecorator(IServiceProvider provider, ISchemaAutoMapper prior) =>
        new(
            provider.GetRequiredService<ITypeSafePropertyMapper>(),
            provider.GetRequiredService<ITypeSafeClient>(),
            prior,
            provider.GetRequiredService<SchemaAutoMapper>(),
            provider.GetRequiredService<IOptions<TypeSafeOptions>>(),
            provider.GetRequiredService<ILogger<TypeSafeSchemaAutoMapper>>());

    /// <summary>
    /// Builds the instance the original descriptor would have produced. An implementation type
    /// is resolved from the container when it is registered as a service in its own right (the
    /// core heuristic is, via <c>AddScoped&lt;SchemaAutoMapper&gt;</c>), so the container owns
    /// its lifetime; otherwise it is activated with its dependencies injected.
    /// </summary>
    private static ISchemaAutoMapper Materialise(IServiceProvider provider, ServiceDescriptor original)
    {
        object? instance = original.ImplementationInstance
            ?? original.ImplementationFactory?.Invoke(provider)
            ?? (original.ImplementationType is { } implementationType
                ? provider.GetService(implementationType) ?? ActivatorUtilities.CreateInstance(provider, implementationType)
                : null);

        return instance as ISchemaAutoMapper
            ?? throw new InvalidOperationException(
                $"The prior ISchemaAutoMapper registration produced {instance?.GetType().FullName ?? "null"}, which does not implement ISchemaAutoMapper.");
    }
}
