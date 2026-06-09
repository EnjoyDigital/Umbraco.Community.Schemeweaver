using Microsoft.Extensions.Logging;
using Umbraco.Cms.Core.Events;
using Umbraco.Cms.Core.Models.ContentEditing;
using Umbraco.Cms.Core.Notifications;
using Umbraco.Cms.Core.Services;
using Umbraco.Extensions;

namespace Umbraco.Community.SchemeWeaver.TestHost;

/// <summary>
/// Ensures the language-variant demo article is fully populated and published
/// once Umbraco has finished booting.
///
/// The variant article (key <c>aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee</c>,
/// content type <c>variantArticle</c>) is seeded via uSync's first-boot
/// import. On Umbraco 18 RC the uSync 18.0.0-rc1 content serialiser imports
/// the node and its culture metadata but does NOT round-trip the
/// culture-variant property values — after import the variant
/// <c>title</c> and <c>bodyText</c> values come back <c>null</c> for every
/// culture. The node therefore can never publish, because its mandatory
/// <c>title</c> property is empty (FailedPublishContentInvalid — Invalid
/// Properties: title), and the language-variants E2E suite — which resolves
/// this node by key from the published cache — 404s.
///
/// To keep the harness self-contained we re-assert the expected
/// culture-variant values here (matching
/// uSync/v18/Content/test-variant-article.config), save, and publish. This
/// runs on every boot and is idempotent. Not shipped with the package.
/// </summary>
public sealed class VariantArticlePublishSeeder
    : INotificationAsyncHandler<UmbracoApplicationStartedNotification>
{
    private static readonly Guid VariantArticleKey =
        Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");

    // The two cultures the demo site varies by.
    private static readonly string[] Cultures = ["en-US", "de-DE"];

    // Mirrors uSync/v18/Content/test-variant-article.config.
    private static readonly (string Culture, string Title, string BodyText)[] Values =
    [
        ("en-US", "Seven things about SchemeWeaver", "<p>English body text for variant testing.</p>"),
        ("de-DE", "Sieben Dinge über SchemeWeaver", "<p>Deutscher Textkörper für Variantentests.</p>"),
    ];

    private readonly IContentService _contentService;
    private readonly IDomainService _domainService;
    private readonly ILogger<VariantArticlePublishSeeder> _logger;

    public VariantArticlePublishSeeder(
        IContentService contentService,
        IDomainService domainService,
        ILogger<VariantArticlePublishSeeder> logger)
    {
        _contentService = contentService;
        _domainService = domainService;
        _logger = logger;
    }

    public async Task HandleAsync(
        UmbracoApplicationStartedNotification notification,
        CancellationToken cancellationToken)
    {
        var content = _contentService.GetById(VariantArticleKey);
        if (content is null)
        {
            _logger.LogWarning(
                "Variant article {Key} not found — uSync seed may not have run.",
                VariantArticleKey);
            return;
        }

        // Backfill the culture-variant values uSync failed to round-trip, but
        // only when they are actually missing — keeps this a no-op once a
        // future uSync release imports them correctly.
        var needsBackfill = false;
        foreach (var (culture, title, bodyText) in Values)
        {
            if (content.GetValue("title", culture) is null)
            {
                content.SetCultureName(title, culture);
                content.SetValue("title", title, culture);
                content.SetValue("bodyText", bodyText, culture);
                needsBackfill = true;
            }
        }

        if (needsBackfill)
        {
            _contentService.Save(content);
            // Re-fetch so the publish operates on the persisted state.
            content = _contentService.GetById(VariantArticleKey)!;
            _logger.LogInformation(
                "Variant article {Key} culture values backfilled (uSync 18 RC did not round-trip them).",
                VariantArticleKey);
        }

        var result = _contentService.Publish(content, Cultures);
        if (result.Success)
        {
            _logger.LogInformation(
                "Variant article {Key} published on boot for cultures {Cultures}.",
                VariantArticleKey,
                string.Join(", ", Cultures));
        }
        else
        {
            _logger.LogError(
                "Failed to publish variant article {Key} on boot: {Result}.",
                VariantArticleKey,
                result.Result);
        }

        await EnsureCultureDomainsAsync();
    }

    /// <summary>
    /// Assigns per-culture path domains to the variant article root so that
    /// every culture — not just the site default — has a resolvable URL.
    ///
    /// The variant article is its own content root with no domain configured.
    /// Without a domain, Umbraco cannot build an absolute URL for the
    /// non-default (de-DE) culture, so the JSON-LD <c>@id</c> comes back null
    /// and the graph generator drops the Article node entirely — the de-DE
    /// preview then contains only the site-level WebSite node. Assigning
    /// <c>/variant-en</c> → en-US and <c>/variant-de</c> → de-DE gives both
    /// cultures a URL and lets the Article node render for each.
    /// </summary>
    private async Task EnsureCultureDomainsAsync()
    {
        var existing = await _domainService.GetAssignedDomainsAsync(VariantArticleKey, includeWildcards: false);
        if (existing.Any())
        {
            return;
        }

        var update = new DomainsUpdateModel
        {
            DefaultIsoCode = "en-US",
            Domains =
            [
                new DomainModel { DomainName = "/variant-en", IsoCode = "en-US" },
                new DomainModel { DomainName = "/variant-de", IsoCode = "de-DE" },
            ],
        };

        var attempt = await _domainService.UpdateDomainsAsync(VariantArticleKey, update);
        if (attempt.Success)
        {
            _logger.LogInformation(
                "Variant article {Key} culture domains assigned (/variant-en, /variant-de).",
                VariantArticleKey);
        }
        else
        {
            _logger.LogWarning(
                "Failed to assign culture domains to variant article {Key}: {Status}.",
                VariantArticleKey,
                attempt.Status);
        }
    }
}
