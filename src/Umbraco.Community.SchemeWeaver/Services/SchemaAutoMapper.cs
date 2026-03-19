using Umbraco.Cms.Core.Services;
using Umbraco.Community.SchemeWeaver.Models.Api;

namespace Umbraco.Community.SchemeWeaver.Services;

/// <summary>
/// Suggests property mappings between Umbraco content types and Schema.org types
/// using exact, synonym, and partial matching with confidence scores.
/// </summary>
public class SchemaAutoMapper : ISchemaAutoMapper
{
    private readonly IContentTypeService _contentTypeService;
    private readonly ISchemaTypeRegistry _schemaTypeRegistry;

    /// <summary>
    /// Synonym dictionary mapping Schema.org property names to common Umbraco property aliases.
    /// Expanded from BaseSchemaModel.GetCommonPropertyNames.
    /// </summary>
    private static readonly Dictionary<string, string[]> Synonyms = new(StringComparer.OrdinalIgnoreCase)
    {
        ["name"] = new[] { "title", "heading", "name", "pageTitle", "blogTitle", "nodeName" },
        ["headline"] = new[] { "title", "heading", "pageTitle", "blogTitle" },
        ["description"] = new[] { "description", "metaDescription", "excerpt", "summary", "intro" },
        ["articleBody"] = new[] { "content", "bodyText", "richText", "mainContent", "body" },
        ["image"] = new[] { "heroImage", "mainImage", "thumbnail", "featuredImage", "image", "photo" },
        ["author"] = new[] { "authorName", "writer", "byline", "author" },
        ["datePublished"] = new[] { "publishDate", "createDate", "articleDate", "datePublished", "publishedDate" },
        ["dateModified"] = new[] { "updateDate", "modifyDate", "dateModified", "lastModified", "modifiedDate" },
        ["url"] = new[] { "url", "link", "href", "pageUrl" },
        ["telephone"] = new[] { "phone", "phoneNumber", "telephone", "tel", "contactNumber" },
        ["email"] = new[] { "email", "emailAddress", "contactEmail" },
        ["address"] = new[] { "address", "streetAddress", "location" },
        ["logo"] = new[] { "logo", "logoImage", "brandLogo", "siteLogo" },
        ["copyrightYear"] = new[] { "copyrightYear", "year" },
        ["inLanguage"] = new[] { "language", "culture", "locale" },
        ["keywords"] = new[] { "tags", "keywords", "categories" },
        ["aggregateRating"] = new[] { "rating", "averageRating", "stars" },
        ["priceRange"] = new[] { "priceRange", "price", "cost" },
        ["openingHours"] = new[] { "openingHours", "hours", "businessHours" },
        ["streetAddress"] = new[] { "streetAddress", "addressLine1", "street" },
        ["addressLocality"] = new[] { "city", "town", "locality" },
        ["addressRegion"] = new[] { "region", "county", "state", "province" },
        ["postalCode"] = new[] { "postcode", "postalCode", "zipCode", "zip" },
        ["addressCountry"] = new[] { "country", "countryCode" },
    };

    public SchemaAutoMapper(IContentTypeService contentTypeService, ISchemaTypeRegistry schemaTypeRegistry)
    {
        _contentTypeService = contentTypeService;
        _schemaTypeRegistry = schemaTypeRegistry;
    }

    public IEnumerable<PropertyMappingSuggestion> SuggestMappings(string contentTypeAlias, string schemaTypeName)
    {
        var contentType = _contentTypeService.Get(contentTypeAlias);
        if (contentType == null) return Enumerable.Empty<PropertyMappingSuggestion>();

        var schemaProperties = _schemaTypeRegistry.GetProperties(schemaTypeName).ToList();
        var contentProperties = contentType.PropertyTypes.ToList();
        var suggestions = new List<PropertyMappingSuggestion>();

        foreach (var schemaProp in schemaProperties)
        {
            var suggestion = new PropertyMappingSuggestion
            {
                SchemaPropertyName = schemaProp.Name,
                SchemaPropertyType = schemaProp.PropertyType,
                SuggestedSourceType = "property"
            };

            // Exact match (case-insensitive)
            var exactMatch = contentProperties.FirstOrDefault(
                p => string.Equals(p.Alias, schemaProp.Name, StringComparison.OrdinalIgnoreCase));

            if (exactMatch != null)
            {
                suggestion.SuggestedContentTypePropertyAlias = exactMatch.Alias;
                suggestion.Confidence = 100;
                suggestion.IsAutoMapped = true;
                suggestions.Add(suggestion);
                continue;
            }

            // Synonym match
            if (Synonyms.TryGetValue(schemaProp.Name, out var synonyms))
            {
                var synonymMatch = contentProperties.FirstOrDefault(
                    p => synonyms.Any(s => string.Equals(p.Alias, s, StringComparison.OrdinalIgnoreCase)));

                if (synonymMatch != null)
                {
                    suggestion.SuggestedContentTypePropertyAlias = synonymMatch.Alias;
                    suggestion.Confidence = 80;
                    suggestion.IsAutoMapped = true;
                    suggestions.Add(suggestion);
                    continue;
                }
            }

            // Partial match (schema property name contained in content property alias or vice versa)
            var partialMatch = contentProperties.FirstOrDefault(
                p => p.Alias.Contains(schemaProp.Name, StringComparison.OrdinalIgnoreCase)
                  || schemaProp.Name.Contains(p.Alias, StringComparison.OrdinalIgnoreCase));

            if (partialMatch != null)
            {
                suggestion.SuggestedContentTypePropertyAlias = partialMatch.Alias;
                suggestion.Confidence = 50;
                suggestion.IsAutoMapped = true;
                suggestions.Add(suggestion);
                continue;
            }

            // No match found
            suggestion.Confidence = 0;
            suggestion.IsAutoMapped = false;
            suggestions.Add(suggestion);
        }

        return suggestions;
    }
}
