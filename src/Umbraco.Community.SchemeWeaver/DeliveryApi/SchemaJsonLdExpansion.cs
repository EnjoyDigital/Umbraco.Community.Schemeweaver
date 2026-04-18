using Microsoft.Extensions.Logging;
using Umbraco.Cms.Core.DeliveryApi;
using Umbraco.Cms.Core.Models;
using Umbraco.Cms.Core.Web;
using Umbraco.Community.SchemeWeaver.Services;

namespace Umbraco.Community.SchemeWeaver.DeliveryApi;

/// <summary>
/// Adds the <c>schemaOrg</c> field to Umbraco's Delivery API Examine content index, enabling
/// filter / sort / search on JSON-LD values via the search endpoint. The response body of
/// <c>/content/item/*</c> is NOT sourced from this index — consumers reading <c>schemaOrg</c>
/// should hit the dedicated
/// <c>/umbraco/delivery/api/v2/schemeweaver/json-ld</c> endpoint instead, which reads from the
/// same <see cref="IJsonLdBlocksProvider"/> cache.
/// </summary>
public class SchemaJsonLdContentIndexHandler : IContentIndexHandler
{
    private readonly IUmbracoContextAccessor _umbracoContextAccessor;
    private readonly IJsonLdBlocksProvider _blocksProvider;
    private readonly ILogger<SchemaJsonLdContentIndexHandler> _logger;

    public SchemaJsonLdContentIndexHandler(
        IUmbracoContextAccessor umbracoContextAccessor,
        IJsonLdBlocksProvider blocksProvider,
        ILogger<SchemaJsonLdContentIndexHandler> logger)
    {
        _umbracoContextAccessor = umbracoContextAccessor;
        _blocksProvider = blocksProvider;
        _logger = logger;
    }

    public IEnumerable<IndexFieldValue> GetFieldValues(IContent content, string? culture)
    {
        if (!_umbracoContextAccessor.TryGetUmbracoContext(out var umbracoContext))
        {
            yield break;
        }

        var published = umbracoContext.Content?.GetById(content.Key);
        if (published is null)
        {
            yield break;
        }

        string[] blocks;
        try
        {
            blocks = _blocksProvider.GetBlocks(published, culture);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to resolve JSON-LD blocks for content {ContentKey}", content.Key);
            yield break;
        }

        if (blocks.Length > 0)
        {
            yield return new IndexFieldValue
            {
                FieldName = "schemaOrg",
                Values = blocks,
            };
        }
    }

    public IEnumerable<IndexField> GetFields() =>
    [
        new IndexField
        {
            FieldName = "schemaOrg",
            FieldType = FieldType.StringRaw,
            VariesByCulture = true,
        },
    ];
}
