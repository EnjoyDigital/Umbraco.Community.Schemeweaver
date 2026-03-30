using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
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

    public SchemaJsonLdContentIndexHandler(
        IServiceScopeFactory scopeFactory,
        IUmbracoContextAccessor umbracoContextAccessor,
        ILogger<SchemaJsonLdContentIndexHandler> logger)
    {
        _scopeFactory = scopeFactory;
        _umbracoContextAccessor = umbracoContextAccessor;
        _logger = logger;
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
            allJsonLd.AddRange(generator.GenerateInheritedJsonLdStrings(published));

            // Main schema for the current content
            var jsonLd = generator.GenerateJsonLdString(published);
            if (!string.IsNullOrEmpty(jsonLd))
                allJsonLd.Add(jsonLd);

            // Schemas from mapped block elements
            allJsonLd.AddRange(generator.GenerateBlockElementJsonLdStrings(published));
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
                VariesByCulture = false
            }
        ];
    }
}
