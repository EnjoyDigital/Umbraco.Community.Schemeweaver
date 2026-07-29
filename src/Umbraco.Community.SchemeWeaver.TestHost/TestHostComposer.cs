using Umbraco.Cms.Core.Composing;
using Umbraco.Cms.Core.DependencyInjection;
using Umbraco.Cms.Core.Notifications;
using Umbraco.Extensions;

namespace Umbraco.Community.SchemeWeaver.TestHost;

/// <summary>
/// Test-host-only composition. Registers the boot-time seeders the E2E suite
/// relies on. Not shipped with the SchemeWeaver package.
/// </summary>
public sealed class TestHostComposer : IComposer
{
    public void Compose(IUmbracoBuilder builder)
    {
        builder.AddNotificationAsyncHandler<
            UmbracoApplicationStartedNotification,
            VariantArticlePublishSeeder>();
        builder.AddNotificationAsyncHandler<
            UmbracoApplicationStartedNotification,
            VariantContentBackfillSeeder>();
    }
}
