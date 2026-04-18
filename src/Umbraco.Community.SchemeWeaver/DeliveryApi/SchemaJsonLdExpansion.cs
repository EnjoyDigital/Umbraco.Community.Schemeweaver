using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Umbraco.Cms.Core.DeliveryApi;
using Umbraco.Cms.Core.Models;
using Umbraco.Cms.Core.Web;
using Umbraco.Community.SchemeWeaver.Services;

namespace Umbraco.Community.SchemeWeaver.DeliveryApi;

/// <summary>
/// Adds schemaOrg JSON-LD data to the Delivery API content index.
/// </summary>
public class SchemaJsonLdContentIndexHandler : IContentIndexHandler
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IUmbracoContextAccessor _umbracoContextAccessor;
    private readonly ILogger<SchemaJsonLdContentIndexHandler> _logger;
    private readonly SchemeWeaverOptions _options;

    public SchemaJsonLdContentIndexHandler(
        IServiceScopeFactory scopeFactory,
        IUmbracoContextAccessor umbracoContextAccessor,
        ILogger<SchemaJsonLdContentIndexHandler> logger,
        IOptions<SchemeWeaverOptions> options)
    {
        _scopeFactory = scopeFactory;
        _umbracoContextAccessor = umbracoContextAccessor;
        _logger = logger;
        _options = options.Value;
    }

    public IEnumerable<IndexFieldValue> GetFieldValues(IContent content, string? culture)
    {
        if (!_umbracoContextAccessor.TryGetUmbracoContext(out var umbracoContext))
        {
            yield break;
        }

        var published = umbracoContext.Content?.GetById(content.Key);
        if (published == null)
        {
            yield break;
        }

        var allJsonLd = new List<string>();

        try
        {
            using var scope = _scopeFactory.CreateScope();
            var generator = scope.ServiceProvider.GetRequiredService<IJsonLdGenerator>();

            // Inherited schemas from ancestor nodes (root-first order)
            allJsonLd.AddRange(generator.GenerateInheritedJsonLdStrings(published, culture));

            // BreadcrumbList derived from the content tree ancestry. Opt out via
            // SchemeWeaverOptions.EmitBreadcrumbsInDeliveryApi if your headless
            // front-end builds its own breadcrumb from its routing layer.
            if (_options.EmitBreadcrumbsInDeliveryApi)
            {
                var breadcrumbJson = generator.GenerateBreadcrumbJsonLd(published, culture);
                if (!string.IsNullOrEmpty(breadcrumbJson))
                    allJsonLd.Add(breadcrumbJson);
            }

            // Main schema for the current content
            var jsonLd = generator.GenerateJsonLdString(published, culture);
            if (!string.IsNullOrEmpty(jsonLd))
                allJsonLd.Add(jsonLd);

            // Schemas from mapped block elements
            allJsonLd.AddRange(generator.GenerateBlockElementJsonLdStrings(published, culture));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to generate JSON-LD for content {ContentKey}", content.Key);
        }

        if (allJsonLd.Count > 0)
        {
            yield return new IndexFieldValue
            {
                FieldName = "schemaOrg",
                Values = allJsonLd.ToArray()
            };
        }
    }

    public IEnumerable<IndexField> GetFields()
    {
        return
        [
            new IndexField
            {
                FieldName = "schemaOrg",
                FieldType = FieldType.StringRaw,
                VariesByCulture = true
            }
        ];
    }
}
