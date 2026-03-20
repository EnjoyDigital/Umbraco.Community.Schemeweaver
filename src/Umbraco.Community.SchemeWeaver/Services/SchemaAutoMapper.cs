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
        //TODO: This is rather janky refactor
        ["name"] = ["title", "heading", "name", "pageTitle", "blogTitle", "nodeName"],
        ["headline"] = ["title", "heading", "pageTitle", "blogTitle"],
        ["description"] = ["description", "metaDescription", "excerpt", "summary", "intro"],
        ["articleBody"] = ["content", "bodyText", "richText", "mainContent", "body"],
        ["image"] = ["heroImage", "mainImage", "thumbnail", "featuredImage", "image", "photo"],
        ["author"] = ["authorName", "writer", "byline", "author"],
        ["datePublished"] = ["publishDate", "createDate", "articleDate", "datePublished", "publishedDate"],
        ["dateModified"] = ["updateDate", "modifyDate", "dateModified", "lastModified", "modifiedDate"],
        ["url"] = ["url", "link", "href", "pageUrl"],
        ["telephone"] = ["phone", "phoneNumber", "telephone", "tel", "contactNumber"],
        ["email"] = ["email", "emailAddress", "contactEmail"],
        ["address"] = ["address", "streetAddress", "location"],
        ["logo"] = ["logo", "logoImage", "brandLogo", "siteLogo"],
        ["copyrightYear"] = ["copyrightYear", "year"],
        ["inLanguage"] = ["language", "culture", "locale"],
        ["keywords"] = ["tags", "keywords", "categories"],
        ["aggregateRating"] = ["rating", "averageRating", "stars"],
        ["priceRange"] = ["priceRange", "price", "cost"],
        ["openingHours"] = ["openingHours", "hours", "businessHours"],
        ["streetAddress"] = ["streetAddress", "addressLine1", "street"],
        ["addressLocality"] = ["city", "town", "locality"],
        ["addressRegion"] = ["region", "county", "state", "province"],
        ["postalCode"] = ["postcode", "postalCode", "zipCode", "zip"],
        ["addressCountry"] = ["country", "countryCode"],
    };

    public SchemaAutoMapper(IContentTypeService contentTypeService, ISchemaTypeRegistry schemaTypeRegistry)
    {
        _contentTypeService = contentTypeService;
        _schemaTypeRegistry = schemaTypeRegistry;
    }

    public IEnumerable<PropertyMappingSuggestion> SuggestMappings(string contentTypeAlias, string schemaTypeName)
    {
        var contentType = _contentTypeService.Get(contentTypeAlias);
        if (contentType is null)
            return [];

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

            if (exactMatch is not null)
            {
                suggestion.SuggestedContentTypePropertyAlias = exactMatch.Alias;
                suggestion.EditorAlias = exactMatch.PropertyEditorAlias;
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

                if (synonymMatch is not null)
                {
                    suggestion.SuggestedContentTypePropertyAlias = synonymMatch.Alias;
                    suggestion.EditorAlias = synonymMatch.PropertyEditorAlias;
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

            if (partialMatch is not null)
            {
                suggestion.SuggestedContentTypePropertyAlias = partialMatch.Alias;
                suggestion.EditorAlias = partialMatch.PropertyEditorAlias;
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
