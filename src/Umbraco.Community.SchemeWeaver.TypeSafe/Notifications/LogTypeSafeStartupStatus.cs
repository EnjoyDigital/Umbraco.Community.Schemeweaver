using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Umbraco.Cms.Core.Events;
using Umbraco.Cms.Core.Notifications;
using Umbraco.Community.SchemeWeaver.Services;
using Umbraco.Community.SchemeWeaver.TypeSafe.Client;
using Umbraco.Community.SchemeWeaver.TypeSafe.Configuration;
using Umbraco.Community.SchemeWeaver.TypeSafe.Services;

namespace Umbraco.Community.SchemeWeaver.TypeSafe.Notifications;

/// <summary>
/// Writes ONE Information line at application start saying whether TypeSafe answers
/// auto-map on this site, and if not, why. Either
/// <c>SchemeWeaver TypeSafe: active (model X, wrapping SchemaAutoMapper)</c> or
/// <c>SchemeWeaver TypeSafe: inert (&lt;reason&gt;)</c>.
/// </summary>
/// <remarks>
/// <para>
/// The line exists because the satellite is silent by design: without a key every call falls
/// through to the prior mapper and nothing in the backoffice looks different. A developer who
/// installed the package and sees heuristic-quality suggestions should be able to open the log
/// and read which of the three inert reasons applies (disabled, no key, or another composer,
/// such as the AI satellite's, registered after this one and took the seam).
/// </para>
/// <para>
/// <see cref="ISchemaAutoMapper"/> is scoped, so it is resolved inside a scope of its own rather
/// than from the root provider, and on a background thread like the core's registry warm-up:
/// constructing the mapper chain must not delay startup.
/// </para>
/// </remarks>
public sealed class LogTypeSafeStartupStatus : INotificationHandler<UmbracoApplicationStartedNotification>
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IOptions<TypeSafeOptions> _options;
    private readonly ILogger<LogTypeSafeStartupStatus> _logger;

    public LogTypeSafeStartupStatus(
        IServiceScopeFactory scopeFactory,
        IOptions<TypeSafeOptions> options,
        ILogger<LogTypeSafeStartupStatus> logger)
    {
        _scopeFactory = scopeFactory;
        _options = options;
        _logger = logger;
    }

    /// <inheritdoc />
    public void Handle(UmbracoApplicationStartedNotification notification) =>
        _ = Task.Run(LogStatus);

    private void LogStatus()
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var mapper = scope.ServiceProvider.GetRequiredService<ISchemaAutoMapper>();
            var options = _options.Value;

            if (mapper is not TypeSafeSchemaAutoMapper typeSafe)
            {
                _logger.LogInformation(
                    "SchemeWeaver TypeSafe: inert (ISchemaAutoMapper is {Mapper}; a composer that ran after SchemeWeaverTypeSafeComposer supplies auto-map)",
                    mapper.GetType().Name);
                return;
            }

            if (!options.Enabled)
            {
                _logger.LogInformation("SchemeWeaver TypeSafe: inert (SchemeWeaver:TypeSafe:Enabled=false)");
                return;
            }

            var client = scope.ServiceProvider.GetRequiredService<ITypeSafeClient>();
            if (!client.IsConfigured)
            {
                _logger.LogInformation(
                    "SchemeWeaver TypeSafe: inert (no API key; set SchemeWeaver:TypeSafe:ApiKey via user-secrets or the SchemeWeaver__TypeSafe__ApiKey environment variable)");
                return;
            }

            _logger.LogInformation(
                "SchemeWeaver TypeSafe: active (model {Model}, wrapping {Prior})",
                options.Model,
                typeSafe.PriorTypeName);
        }
        catch (Exception ex)
        {
            // Diagnostics only: a failure here must never affect startup.
            _logger.LogWarning(ex, "SchemeWeaver TypeSafe: could not determine the startup status.");
        }
    }
}
