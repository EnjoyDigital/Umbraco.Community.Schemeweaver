using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Umbraco.Cms.Core;
using Umbraco.Cms.Core.Events;
using Umbraco.Cms.Core.Notifications;
using Umbraco.Cms.Core.Services;

namespace Umbraco.Community.SchemeWeaver.Seo.Notifications;

/// <summary>
/// Adds the SEO Schema section to the Administrators group once, on the first start after
/// install, so the section is reachable without a database edit. Runs on application start
/// (not as a migration step) because <see cref="IUserGroupService.UpdateAsync"/> needs a fully
/// running app for its notifications and cache refresh. A failed attempt leaves the flag unset
/// so it retries next boot.
/// </summary>
public sealed class GrantSeoSectionToAdministrators : INotificationAsyncHandler<UmbracoApplicationStartedNotification>
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IRuntimeState _runtimeState;
    private readonly IOptions<SchemeWeaverSeoOptions> _options;
    private readonly ILogger<GrantSeoSectionToAdministrators> _logger;

    public GrantSeoSectionToAdministrators(
        IServiceScopeFactory scopeFactory,
        IRuntimeState runtimeState,
        IOptions<SchemeWeaverSeoOptions> options,
        ILogger<GrantSeoSectionToAdministrators> logger)
    {
        _scopeFactory = scopeFactory;
        _runtimeState = runtimeState;
        _options = options;
        _logger = logger;
    }

    public async Task HandleAsync(UmbracoApplicationStartedNotification notification, CancellationToken cancellationToken)
    {
        if (!_options.Value.GrantSectionToAdministrators || _runtimeState.Level != RuntimeLevel.Run)
            return;

        try
        {
            using var scope = _scopeFactory.CreateScope();
            var keyValueService = scope.ServiceProvider.GetRequiredService<IKeyValueService>();
            if (keyValueService.GetValue(SchemeWeaverSeoConstants.KeyValueKeys.SectionGranted) is not null)
                return;

            var userGroupService = scope.ServiceProvider.GetRequiredService<IUserGroupService>();
            var admins = await userGroupService.GetAsync(Constants.Security.AdminGroupKey).ConfigureAwait(false);
            if (admins is null)
            {
                _logger.LogWarning("SchemeWeaver SEO: the Administrators user group was not found; the SEO Schema section must be granted manually");
                return;
            }

            if (!admins.AllowedSections.Contains(SchemeWeaverSeoConstants.SectionAlias, StringComparer.OrdinalIgnoreCase))
            {
                admins.AddAllowedSection(SchemeWeaverSeoConstants.SectionAlias);
                var attempt = await userGroupService.UpdateAsync(admins, Constants.Security.SuperUserKey).ConfigureAwait(false);
                if (!attempt.Success)
                {
                    _logger.LogWarning("SchemeWeaver SEO could not grant the SEO Schema section to Administrators ({Status}); will retry on next start", attempt.Status);
                    return;
                }

                _logger.LogInformation("SchemeWeaver SEO granted the '{Section}' section to the Administrators user group", SchemeWeaverSeoConstants.SectionAlias);
            }

            keyValueService.SetValue(SchemeWeaverSeoConstants.KeyValueKeys.SectionGranted, DateTime.UtcNow.ToString("O"));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "SchemeWeaver SEO could not grant the SEO Schema section to Administrators; will retry on next start");
        }
    }
}
