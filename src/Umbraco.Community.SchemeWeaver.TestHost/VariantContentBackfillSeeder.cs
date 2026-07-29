using System.Xml.Linq;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Umbraco.Cms.Core.Events;
using Umbraco.Cms.Core.Notifications;
using Umbraco.Cms.Core.Services;

namespace Umbraco.Community.SchemeWeaver.TestHost;

/// <summary>
/// Generalises <see cref="VariantArticlePublishSeeder"/> to every content node
/// seeded via uSync's first-boot import: the uSync 18 line's content
/// serialiser imports culture-variant nodes but does NOT round-trip their
/// culture-variant property values — after a fresh import, every
/// <c>&lt;Value Culture="…"&gt;</c> entry comes back <c>null</c>, so any node
/// whose mandatory <c>title</c> varies by culture fails to publish
/// (FailedPublishContentInvalid — Invalid Properties: title) and the
/// rendering E2E specs 404 on it.
///
/// This seeder re-reads the per-culture values straight from the committed
/// uSync content configs (<c>uSync/v18/Content/*.config</c>), backfills any
/// value that is missing on the saved node, and (re)publishes the cultures
/// the config marks as published. It runs on every boot and is a no-op once
/// values are present — so it disappears functionally the day uSync imports
/// them correctly. Not shipped with the package.
/// </summary>
public sealed class VariantContentBackfillSeeder
    : INotificationAsyncHandler<UmbracoApplicationStartedNotification>
{
    private readonly IContentService _contentService;
    private readonly IHostEnvironment _hostEnvironment;
    private readonly ILogger<VariantContentBackfillSeeder> _logger;

    public VariantContentBackfillSeeder(
        IContentService contentService,
        IHostEnvironment hostEnvironment,
        ILogger<VariantContentBackfillSeeder> logger)
    {
        _contentService = contentService;
        _hostEnvironment = hostEnvironment;
        _logger = logger;
    }

    public Task HandleAsync(
        UmbracoApplicationStartedNotification notification,
        CancellationToken cancellationToken)
    {
        var contentFolder = Path.Join(
            _hostEnvironment.ContentRootPath, "uSync", "v18", "Content");
        if (!Directory.Exists(contentFolder))
        {
            return Task.CompletedTask;
        }

        foreach (var file in Directory.EnumerateFiles(contentFolder, "*.config"))
        {
            try
            {
                BackfillFromConfig(file);
            }
            catch (Exception ex)
            {
                // A single malformed config must never take the harness down.
                _logger.LogWarning(ex, "Variant backfill skipped for {File}.", file);
            }
        }

        return Task.CompletedTask;
    }

    private void BackfillFromConfig(string file)
    {
        var doc = XDocument.Load(file);
        var root = doc.Root;
        if (root is null || root.Attribute("Key") is not { } keyAttr
            || !Guid.TryParse(keyAttr.Value, out var key))
        {
            return;
        }

        // Culture-variant property values: alias -> (culture -> value).
        var variantValues = new Dictionary<string, Dictionary<string, string>>();
        foreach (var prop in root.Element("Properties")?.Elements() ?? [])
        {
            foreach (var value in prop.Elements("Value"))
            {
                if (value.Attribute("Culture")?.Value is { Length: > 0 } culture)
                {
                    if (!variantValues.TryGetValue(prop.Name.LocalName, out var byCulture))
                    {
                        variantValues[prop.Name.LocalName] = byCulture = [];
                    }

                    byCulture[culture] = value.Value;
                }
            }
        }

        if (variantValues.Count == 0)
        {
            return; // fully invariant node — uSync imports those fine
        }

        var info = root.Element("Info");
        var cultureNames = (info?.Element("NodeName")?.Elements("Name") ?? [])
            .Where(n => n.Attribute("Culture")?.Value is { Length: > 0 })
            .ToDictionary(n => n.Attribute("Culture")!.Value, n => n.Value);
        var publishedCultures = (info?.Element("Published")?.Elements("Published") ?? [])
            .Where(p => p.Attribute("Culture")?.Value is { Length: > 0 }
                && bool.TryParse(p.Value, out var published) && published)
            .Select(p => p.Attribute("Culture")!.Value)
            .ToArray();

        var content = _contentService.GetById(key);
        if (content is null)
        {
            return; // node not seeded (e.g. trashed in the config) — nothing to do
        }

        var needsBackfill = false;
        foreach (var (alias, byCulture) in variantValues)
        {
            foreach (var (culture, value) in byCulture)
            {
                if (content.GetValue(alias, culture) is null)
                {
                    if (cultureNames.TryGetValue(culture, out var name))
                    {
                        content.SetCultureName(name, culture);
                    }

                    content.SetValue(alias, value, culture);
                    needsBackfill = true;
                }
            }
        }

        if (!needsBackfill)
        {
            return;
        }

        _contentService.Save(content);
        content = _contentService.GetById(key)!;
        _logger.LogInformation(
            "Backfilled culture-variant values for {Name} ({Key}) from {File} (uSync 18 did not round-trip them).",
            content.Name, key, Path.GetFileName(file));

        if (publishedCultures.Length == 0)
        {
            return;
        }

        var result = _contentService.Publish(content, publishedCultures);
        if (result.Success)
        {
            _logger.LogInformation(
                "Published {Name} ({Key}) for cultures {Cultures} after backfill.",
                content.Name, key, string.Join(", ", publishedCultures));
        }
        else
        {
            _logger.LogWarning(
                "Failed to publish {Name} ({Key}) after backfill: {Result}.",
                content.Name, key, result.Result);
        }
    }
}
