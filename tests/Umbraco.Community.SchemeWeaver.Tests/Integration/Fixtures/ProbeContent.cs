using Microsoft.Extensions.DependencyInjection;
using Umbraco.Cms.Core.Models;
using Umbraco.Cms.Core.Services;
using Umbraco.Cms.Core.Web;

namespace Umbraco.Community.SchemeWeaver.Tests.Integration.Fixtures;

/// <summary>
/// Creates (or reuses, by name and type) a root content node, sets values, saves and publishes
/// it invariantly, then waits for the published cache to serve it. Pairs with
/// <see cref="ProbeContentTypes"/>, whose types allow root.
/// </summary>
internal static class ProbeContent
{
    public static async Task<Guid> EnsurePublishedAsync(
        IServiceProvider services,
        string contentTypeAlias,
        string name,
        params (string Alias, object? Value)[] values)
    {
        using var scope = services.CreateScope();
        var contentService = scope.ServiceProvider.GetRequiredService<IContentService>();

        IContent? content = null;

        // SQLite shared-cache hosts occasionally report "database table is locked" when the first
        // save after boot overlaps start-up work; a partially persisted entity cannot be re-saved
        // ("Cannot save a non-current version"), so every attempt starts from a fresh entity.
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                content = contentService.GetRootContent().ToList()
                    .FirstOrDefault(c => c.ContentType.Alias == contentTypeAlias && c.Name == name)
                    ?? contentService.Create(name, Umbraco.Cms.Core.Constants.System.Root, contentTypeAlias);

                foreach (var (alias, value) in values)
                    content.SetValue(alias, value);

                contentService.Save(content);
                var result = contentService.Publish(content, ["*"]);
                if (!result.Success)
                    throw new InvalidOperationException($"Could not publish probe content '{name}': {result.Result}.");
                break;
            }
            catch (Exception ex) when (attempt < 10 && IsTransient(ex))
            {
                await Task.Delay(500 * attempt);
            }
        }

        var contextFactory = scope.ServiceProvider.GetRequiredService<IUmbracoContextFactory>();
        for (var attempt = 0; attempt < 50; attempt++)
        {
            using var reference = contextFactory.EnsureUmbracoContext();
            if (reference.UmbracoContext.Content?.GetById(content!.Key) is not null)
                return content.Key;
            await Task.Delay(100);
        }

        throw new InvalidOperationException($"Probe content '{name}' was published but never appeared in the published cache.");
    }

    private static bool IsTransient(Exception ex) =>
        ex is Microsoft.Data.Sqlite.SqliteException
        || ex is InvalidOperationException { Message: var m } && m.Contains("non-current version", StringComparison.OrdinalIgnoreCase);
}
