using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Umbraco.Cms.Core.DeliveryApi;
using Umbraco.Cms.Core.Models;
using Umbraco.Cms.Core.Models.DeliveryApi;
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

        string? jsonLd = null;
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var generator = scope.ServiceProvider.GetRequiredService<IJsonLdGenerator>();
            jsonLd = generator.GenerateJsonLdString(published);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to generate JSON-LD for content {ContentKey}", content.Key);
        }

        if (!string.IsNullOrEmpty(jsonLd))
        {
            yield return new IndexFieldValue
            {
                FieldName = "schemaOrg",
                Values = [jsonLd]
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
