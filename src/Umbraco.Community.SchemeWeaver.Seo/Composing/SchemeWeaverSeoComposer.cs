using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Umbraco.Cms.Core.Composing;
using Umbraco.Cms.Core.DependencyInjection;
using Umbraco.Cms.Core.Events;
using Umbraco.Cms.Core.Notifications;
using Umbraco.Community.SchemeWeaver.Composing;
using Umbraco.Community.SchemeWeaver.Graph;
using Umbraco.Community.SchemeWeaver.Notifications;
using Umbraco.Community.SchemeWeaver.Seo.Authorization;
using Umbraco.Community.SchemeWeaver.Seo.Notifications;
using Umbraco.Community.SchemeWeaver.Seo.Overrides;
using Umbraco.Community.SchemeWeaver.Seo.Services;
using Umbraco.Community.SchemeWeaver.Seo.SiteIdentity;
using Umbraco.Community.SchemeWeaver.Seo.Templates;
using Umbraco.Community.SchemeWeaver.Services;
using OpenIddict.Validation.AspNetCore;

namespace Umbraco.Community.SchemeWeaver.Seo.Composing;

/// <summary>
/// Registers the SEO satellite after the core so its implementations of the two core seams
/// (<see cref="IContentMappingOverrideProvider"/>, <see cref="ISiteIdentityProvider"/>) win.
/// </summary>
[ComposeAfter(typeof(SchemeWeaverComposer))]
public class SchemeWeaverSeoComposer : IComposer
{
    public void Compose(IUmbracoBuilder builder)
    {
        builder.Services.AddOptions<SchemeWeaverSeoOptions>()
            .BindConfiguration(SchemeWeaverSeoOptions.SectionName)
            .Validate(o => o.HealthCheckCacheDuration >= TimeSpan.Zero, "SchemeWeaver:Seo:HealthCheckCacheDuration must not be negative.")
            .ValidateOnStart();

        builder.Services.AddControllers().AddApplicationPart(typeof(SchemeWeaverSeoComposer).Assembly);

        // Singletons depend only on singletons (registry, key-value store, memory cache).
        builder.Services.AddSingleton<ISeoTemplateCatalogue, SeoTemplateCatalogue>();
        builder.Services.AddSingleton<ISiteIdentityStore, SiteIdentityStore>();

        builder.Services.AddScoped<ISeoPageOverrideRepository, SeoPageOverrideRepository>();
        builder.Services.AddScoped<IPublishedContentOfTypeQuery, PublishedContentOfTypeQuery>();
        builder.Services.AddScoped<ISeoSlotProjector, SeoSlotProjector>();
        builder.Services.AddScoped<ISeoContentTypeStatusService, SeoContentTypeStatusService>();
        builder.Services.AddScoped<ISeoHealthCheckService, SeoHealthCheckService>();

        // Core seams: last registration wins (the core registered Null implementations first).
        builder.Services.AddScoped<IContentMappingOverrideProvider, SeoContentMappingOverrideProvider>();
        builder.Services.AddScoped<ISiteIdentityProvider, SeoSiteIdentityProvider>();

        // Section-access policy for the SEO API. AddAuthorization has already run inside
        // AddBackOffice(); Configure<AuthorizationOptions> appends to the same options.
        builder.Services.AddSingleton<IAuthorizationHandler, SeoSectionHandler>();
        builder.Services.Configure<AuthorizationOptions>(options =>
            options.AddPolicy(SeoAuthorizationPolicies.SectionAccess, policy =>
            {
                // Bearer (API users / MCP clients); the controller attribute adds the cookie scheme.
                policy.AuthenticationSchemes.Add(OpenIddictValidationAspNetCoreDefaults.AuthenticationScheme);
                policy.RequireAuthenticatedUser();
                policy.Requirements.Add(new SeoSectionRequirement(SchemeWeaverSeoConstants.SectionAlias));
            }));

        builder.AddNotificationAsyncHandler<UmbracoApplicationStartedNotification, GrantSeoSectionToAdministrators>();
        builder.AddNotificationHandler<ContentDeletedNotification, RemovePageOverridesOnContentDelete>();
        builder.AddNotificationHandler<SchemaMappingSavedNotification, EvictSeoHealthReportOnMappingChange>();
        builder.AddNotificationHandler<SchemaMappingDeletedNotification, EvictSeoHealthReportOnMappingChange>();
        builder.AddNotificationHandler<UmbracoApplicationStartedNotification, LogSeoStartupStatus>();
    }
}

/// <summary>One line at start-up so an operator can see the satellite is active.</summary>
public sealed class LogSeoStartupStatus : INotificationHandler<UmbracoApplicationStartedNotification>
{
    private readonly ISeoTemplateCatalogue _catalogue;
    private readonly ILogger<LogSeoStartupStatus> _logger;

    public LogSeoStartupStatus(ISeoTemplateCatalogue catalogue, ILogger<LogSeoStartupStatus> logger)
    {
        _catalogue = catalogue;
        _logger = logger;
    }

    public void Handle(UmbracoApplicationStartedNotification notification) =>
        _logger.LogInformation(
            "SchemeWeaver SEO active: {KindCount} content kinds, section '{Section}'",
            _catalogue.All.Count, SchemeWeaverSeoConstants.SectionAlias);
}
