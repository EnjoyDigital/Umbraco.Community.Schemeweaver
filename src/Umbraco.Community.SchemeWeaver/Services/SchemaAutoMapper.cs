using Umbraco.Cms.Core.Services;
using Umbraco.Community.SchemeWeaver.Models.Api;

namespace Umbraco.Community.SchemeWeaver.Services;

/// <summary>
/// Suggests property mappings between Umbraco content types and Schema.org types
/// using exact, synonym, and partial matching with confidence scores.
/// Supports complex type inference for BlockList/BlockGrid and popular schema defaults.
/// </summary>
public class SchemaAutoMapper : ISchemaAutoMapper
{
    private readonly IContentTypeService _contentTypeService;
    private readonly ISchemaTypeRegistry _schemaTypeRegistry;

    private static HashSet<string> BlockEditorAliases => SchemeWeaverConstants.PropertyEditors.BlockEditorAliases;

    private static readonly HashSet<string> ContentPickerAliases = new(StringComparer.OrdinalIgnoreCase)
    {
        "Umbraco.ContentPicker"
    };

    /// <summary>
    /// Synonym dictionary mapping Schema.org property names to common Umbraco property aliases.
    /// Expanded from BaseSchemaModel.GetCommonPropertyNames.
    /// </summary>
    private static readonly Dictionary<string, string[]> Synonyms = new(StringComparer.OrdinalIgnoreCase)
    {
        // General / Article
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

        // Product
        ["sku"] = ["sku", "productCode"],
        ["brand"] = ["brand", "manufacturer", "brandName"],
        ["price"] = ["price", "cost", "amount"],
        ["offers"] = ["offers", "pricing"],
        ["review"] = ["review", "reviews", "customerReview"],
        ["ratingValue"] = ["ratingValue", "rating", "stars", "score"],
        ["availability"] = ["availability", "inStock", "stockStatus"],
        ["mpn"] = ["mpn", "partNumber"],
        ["gtin"] = ["gtin", "barcode", "ean", "upc"],
        ["currency"] = ["currency", "currencyCode", "priceCurrency"],

        // Event
        ["startDate"] = ["startDate", "eventDate", "fromDate", "dateFrom"],
        ["endDate"] = ["endDate", "toDate", "dateTo"],
        ["eventStatus"] = ["eventStatus", "status"],
        ["eventAttendanceMode"] = ["eventAttendanceMode", "attendanceMode"],
        ["location"] = ["location", "venue", "locationName", "eventLocation"],
        ["organizer"] = ["organizer", "organiser", "organiserName", "organisedBy"],
        ["performer"] = ["performer", "artist", "speaker"],

        // Recipe
        ["prepTime"] = ["prepTime", "preparationTime", "prepDuration"],
        ["cookTime"] = ["cookTime", "cookingTime", "cookDuration"],
        ["totalTime"] = ["totalTime", "totalDuration"],
        ["recipeYield"] = ["recipeYield", "servings", "serves", "yield"],
        ["calories"] = ["calories", "energy", "kcal"],
        ["recipeCategory"] = ["recipeCategory", "category", "mealType"],
        ["recipeCuisine"] = ["recipeCuisine", "cuisine", "cuisineType"],
        ["recipeIngredient"] = ["ingredients", "recipeIngredient", "ingredientList"],
        ["recipeInstructions"] = ["instructions", "recipeInstructions", "steps", "method"],

        // LocalBusiness
        ["openingHoursSpecification"] = ["openingHours", "hours", "businessHours", "openingHoursSpecification"],
        ["geo"] = ["geo", "coordinates", "location", "geoCoordinates"],
        ["paymentAccepted"] = ["paymentAccepted", "paymentMethods"],
        ["areaServed"] = ["areaServed", "serviceArea"],

        // Person
        ["givenName"] = ["givenName", "firstName", "forename"],
        ["familyName"] = ["familyName", "lastName", "surname"],
        ["jobTitle"] = ["jobTitle", "role", "position", "title"],
        ["worksFor"] = ["worksFor", "employer", "company", "organisation"],

        // Video
        ["thumbnailUrl"] = ["thumbnail", "thumbnailImage", "videoThumbnail", "posterImage"],
        ["uploadDate"] = ["uploadDate", "videoDate", "dateUploaded"],
        ["duration"] = ["duration", "videoLength", "length", "runtime"],
        ["contentUrl"] = ["contentUrl", "videoUrl", "videoFile", "mediaUrl"],
        ["embedUrl"] = ["embedUrl", "embedCode", "videoEmbed"],

        // Job Posting
        ["datePosted"] = ["datePosted", "postingDate", "jobDate", "listedDate"],
        ["validThrough"] = ["validThrough", "closingDate", "expiryDate", "deadline"],
        ["employmentType"] = ["employmentType", "jobType", "contractType", "workType"],
        ["hiringOrganization"] = ["hiringOrganization", "hiringOrganisation", "employer", "company"],
        ["jobLocation"] = ["jobLocation", "workLocation", "office"],
        ["baseSalary"] = ["salary", "baseSalary", "pay", "compensation"],
        ["qualifications"] = ["qualifications", "requirements", "skills"],

        // Course
        ["courseCode"] = ["courseCode", "code", "referenceNumber"],
        ["provider"] = ["provider", "institution", "school", "university"],

        // Software
        ["applicationCategory"] = ["applicationCategory", "category", "softwareCategory", "appCategory"],
        ["operatingSystem"] = ["operatingSystem", "platform", "os", "systemRequirements"],
        ["softwareVersion"] = ["softwareVersion", "version", "releaseVersion"],
        ["downloadUrl"] = ["downloadUrl", "downloadLink", "download"],

        // Book
        ["isbn"] = ["isbn", "isbnNumber", "bookId"],
        ["bookFormat"] = ["bookFormat", "format", "binding"],
        ["numberOfPages"] = ["numberOfPages", "pageCount", "pages"],

        // HowTo
        ["step"] = ["steps", "instructions", "howToSteps"],
        ["tool"] = ["tools", "equipment", "toolsNeeded"],
        ["supply"] = ["supplies", "materials", "suppliesNeeded"],
        ["estimatedCost"] = ["cost", "estimatedCost", "price"],

        // Restaurant
        ["servesCuisine"] = ["servesCuisine", "cuisineType", "cuisine", "foodType"],
        ["menu"] = ["menu", "menuUrl", "menuLink"],
        ["acceptsReservations"] = ["acceptsReservations", "reservations", "bookingAvailable"],

        // Person (additional)
        ["sameAs"] = ["sameAs", "socialLinks", "profiles", "socialMedia"],
        ["alumniOf"] = ["alumniOf", "education", "university"],

        // Organization (additional)
        ["foundingDate"] = ["foundingDate", "founded", "established"],
        ["numberOfEmployees"] = ["numberOfEmployees", "teamSize", "employees"],
    };

    /// <summary>
    /// Pre-built defaults for popular Schema.org type/property combinations.
    /// Key format: "{SchemaTypeName}.{PropertyName}"
    /// </summary>
    private static readonly Dictionary<string, PopularSchemaDefault> PopularSchemaDefaults = new(StringComparer.OrdinalIgnoreCase)
    {
        ["FAQPage.mainEntity"] = new("blockContent", "Question",
            """{"nestedMappings":[{"schemaProperty":"name","contentProperty":"question"},{"schemaProperty":"acceptedAnswer","contentProperty":"answer","wrapInType":"Answer","wrapInProperty":"Text"}]}"""),

        ["Product.review"] = new("blockContent", "Review",
            """{"nestedMappings":[{"schemaProperty":"author","contentProperty":"reviewAuthor"},{"schemaProperty":"reviewRating","contentProperty":"ratingValue","wrapInType":"Rating","wrapInProperty":"RatingValue"},{"schemaProperty":"reviewBody","contentProperty":"reviewBody"}]}"""),
        ["Product.offers"] = new("complexType", "Offer", null),
        ["Product.aggregateRating"] = new("complexType", "AggregateRating", null),
        ["Product.brand"] = new("complexType", "Brand", null),

        ["Event.location"] = new("complexType", "Place", null),
        ["Event.organizer"] = new("complexType", "Organization", null),
        ["Event.offers"] = new("complexType", "Offer", null),

        ["Article.author"] = new("complexType", "Person", null),
        ["Article.publisher"] = new("complexType", "Organization", null),

        ["BlogPosting.author"] = new("complexType", "Person", null),
        ["BlogPosting.publisher"] = new("complexType", "Organization", null),

        ["Recipe.recipeIngredient"] = new("blockContent", null,
            """{"extractAs":"stringList","contentProperty":"ingredient"}"""),
        ["Recipe.recipeInstructions"] = new("blockContent", "HowToStep",
            """{"nestedMappings":[{"schemaProperty":"name","contentProperty":"stepName"},{"schemaProperty":"text","contentProperty":"stepText"}]}"""),
        ["Recipe.nutrition"] = new("complexType", "NutritionInformation", null),
        ["Recipe.author"] = new("complexType", "Person", null),

        ["LocalBusiness.address"] = new("complexType", "PostalAddress", null),
        ["LocalBusiness.openingHoursSpecification"] = new("blockContent", "OpeningHoursSpecification", null),
        ["LocalBusiness.geo"] = new("complexType", "GeoCoordinates", null),

        // NewsArticle / TechArticle (inherit Article patterns)
        ["NewsArticle.author"] = new("complexType", "Person", null),
        ["NewsArticle.publisher"] = new("complexType", "Organization", null),
        ["TechArticle.author"] = new("complexType", "Person", null),
        ["TechArticle.publisher"] = new("complexType", "Organization", null),

        // JobPosting
        ["JobPosting.hiringOrganization"] = new("complexType", "Organization", null),
        ["JobPosting.jobLocation"] = new("complexType", "Place", null),

        // Course
        ["Course.provider"] = new("complexType", "Organization", null),

        // SoftwareApplication
        ["SoftwareApplication.offers"] = new("complexType", "Offer", null),
        ["SoftwareApplication.aggregateRating"] = new("complexType", "AggregateRating", null),
        ["SoftwareApplication.author"] = new("complexType", "Organization", null),

        // Book
        ["Book.author"] = new("complexType", "Person", null),
        ["Book.publisher"] = new("complexType", "Organization", null),
        ["Book.offers"] = new("complexType", "Offer", null),

        // HowTo
        ["HowTo.step"] = new("blockContent", "HowToStep",
            """{"nestedMappings":[{"schemaProperty":"name","contentProperty":"stepName"},{"schemaProperty":"text","contentProperty":"stepText"}]}"""),
        ["HowTo.tool"] = new("blockContent", null,
            """{"extractAs":"stringList","contentProperty":"toolName"}"""),

        // Restaurant (extends LocalBusiness)
        ["Restaurant.address"] = new("complexType", "PostalAddress", null),
        ["Restaurant.geo"] = new("complexType", "GeoCoordinates", null),

        // WebSite
        ["WebSite.publisher"] = new("complexType", "Organization", null),

        // ProfilePage
        ["ProfilePage.mainEntity"] = new("complexType", "Person", null),
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
                SuggestedSourceType = "property",
                AcceptedTypes = schemaProp.AcceptedTypes,
                IsComplexType = schemaProp.IsComplexType,
            };

            // Check for popular schema defaults first
            var defaultKey = $"{schemaTypeName}.{schemaProp.Name}";
            var hasPopularDefault = PopularSchemaDefaults.TryGetValue(defaultKey, out var popularDefault);

            // Exact match (case-insensitive)
            var exactMatch = contentProperties.FirstOrDefault(
                p => string.Equals(p.Alias, schemaProp.Name, StringComparison.OrdinalIgnoreCase));

            if (exactMatch is not null)
            {
                suggestion.SuggestedContentTypePropertyAlias = exactMatch.Alias;
                suggestion.EditorAlias = exactMatch.PropertyEditorAlias;
                suggestion.Confidence = 100;
                suggestion.IsAutoMapped = true;

                ApplyComplexTypeInference(suggestion, exactMatch.PropertyEditorAlias, hasPopularDefault, popularDefault);
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

                    ApplyComplexTypeInference(suggestion, synonymMatch.PropertyEditorAlias, hasPopularDefault, popularDefault);
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

                ApplyComplexTypeInference(suggestion, partialMatch.PropertyEditorAlias, hasPopularDefault, popularDefault);
                suggestions.Add(suggestion);
                continue;
            }

            // Built-in property auto-mapping (URL, Name, dates) as fallback when no custom property matched
            if (!schemaProp.IsComplexType)
            {
                var builtInAlias = TryMatchBuiltInProperty(schemaProp);
                if (builtInAlias is not null)
                {
                    suggestion.SuggestedContentTypePropertyAlias = builtInAlias;
                    suggestion.EditorAlias = SchemeWeaverConstants.BuiltInProperties.EditorAlias;
                    suggestion.Confidence = 70;
                    suggestion.IsAutoMapped = true;
                    suggestions.Add(suggestion);
                    continue;
                }
            }

            // No content property match — check for complex type defaults
            if (schemaProp.IsComplexType && hasPopularDefault)
            {
                suggestion.SuggestedSourceType = popularDefault!.SourceType;
                suggestion.SuggestedNestedSchemaTypeName = popularDefault.NestedSchemaTypeName;
                suggestion.SuggestedResolverConfig = popularDefault.ResolverConfig;

                // For blockContent defaults, only auto-map if a matching block property exists
                if (popularDefault.SourceType == "blockContent")
                {
                    var blockProperty = contentProperties
                        .FirstOrDefault(p => BlockEditorAliases.Contains(p.PropertyEditorAlias));
                    if (blockProperty is not null)
                    {
                        suggestion.SuggestedContentTypePropertyAlias = blockProperty.Alias;
                        suggestion.EditorAlias = blockProperty.PropertyEditorAlias;
                        suggestion.Confidence = 60;
                        suggestion.IsAutoMapped = true;
                    }
                    else
                    {
                        suggestion.Confidence = 0;
                        suggestion.IsAutoMapped = false;
                    }
                }
                else
                {
                    suggestion.Confidence = 60;
                    suggestion.IsAutoMapped = true;
                }
            }
            else if (schemaProp.IsComplexType)
            {
                // No popular default and no property match — keep suggestion data but don't auto-map
                suggestion.SuggestedSourceType = "complexType";
                suggestion.SuggestedNestedSchemaTypeName = GetFirstNonPrimitiveAcceptedType(schemaProp.AcceptedTypes);
                suggestion.Confidence = 0;
                suggestion.IsAutoMapped = false;
            }
            else
            {
                suggestion.Confidence = 0;
                suggestion.IsAutoMapped = false;
            }

            suggestions.Add(suggestion);
        }

        return suggestions;
    }

    /// <summary>
    /// Applies complex type inference when a content property has been matched.
    /// Adjusts source type and nested schema type based on editor alias and popular defaults.
    /// </summary>
    private static void ApplyComplexTypeInference(
        PropertyMappingSuggestion suggestion,
        string editorAlias,
        bool hasPopularDefault,
        PopularSchemaDefault? popularDefault)
    {
        if (!suggestion.IsComplexType)
            return;

        if (BlockEditorAliases.Contains(editorAlias))
        {
            if (hasPopularDefault)
            {
                suggestion.SuggestedSourceType = popularDefault!.SourceType;
                suggestion.SuggestedNestedSchemaTypeName = popularDefault.NestedSchemaTypeName;
                suggestion.SuggestedResolverConfig = popularDefault.ResolverConfig;
                suggestion.Confidence = 70;
            }
            else
            {
                suggestion.SuggestedSourceType = "blockContent";
                suggestion.SuggestedNestedSchemaTypeName = GetFirstNonPrimitiveAcceptedType(suggestion.AcceptedTypes);
                suggestion.Confidence = 70;
            }
        }
        else if (ContentPickerAliases.Contains(editorAlias))
        {
            // Content picker — keep source type as "property", resolver handles nesting
            if (hasPopularDefault)
            {
                suggestion.SuggestedNestedSchemaTypeName = popularDefault!.NestedSchemaTypeName;
            }
        }
        else if (hasPopularDefault)
        {
            // Non-block, non-picker editor with a popular default
            suggestion.SuggestedSourceType = popularDefault!.SourceType;
            suggestion.SuggestedNestedSchemaTypeName = popularDefault.NestedSchemaTypeName;
            suggestion.SuggestedResolverConfig = popularDefault.ResolverConfig;
        }
    }

    /// <summary>
    /// Returns the first accepted type that is not a primitive Schema.org type (Text, Number, Boolean, etc.).
    /// </summary>
    private static string? GetFirstNonPrimitiveAcceptedType(List<string> acceptedTypes)
    {
        return acceptedTypes.FirstOrDefault(t =>
            !string.Equals(t, "Text", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(t, "Number", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(t, "Boolean", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(t, "Date", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(t, "DateTime", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(t, "Time", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(t, "URL", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(t, "Integer", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(t, "Float", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(t, "Duration", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Attempts to match a schema property to a built-in IPublishedContent member.
    /// Returns the built-in alias (e.g. "__url") or null if no match.
    /// </summary>
    private static string? TryMatchBuiltInProperty(SchemaPropertyInfo schemaProp)
    {
        // URL schema properties → content URL
        if (string.Equals(schemaProp.Name, "url", StringComparison.OrdinalIgnoreCase)
            || schemaProp.PropertyType?.Contains("URL", StringComparison.OrdinalIgnoreCase) == true)
            return SchemeWeaverConstants.BuiltInProperties.Url;

        // name → content name (only if no custom property matched)
        if (string.Equals(schemaProp.Name, "name", StringComparison.OrdinalIgnoreCase))
            return SchemeWeaverConstants.BuiltInProperties.Name;

        // Date properties → built-in dates
        if (string.Equals(schemaProp.Name, "dateModified", StringComparison.OrdinalIgnoreCase))
            return SchemeWeaverConstants.BuiltInProperties.UpdateDate;

        if (string.Equals(schemaProp.Name, "datePublished", StringComparison.OrdinalIgnoreCase)
            || string.Equals(schemaProp.Name, "dateCreated", StringComparison.OrdinalIgnoreCase))
            return SchemeWeaverConstants.BuiltInProperties.CreateDate;

        return null;
    }

    /// <summary>
    /// Represents a pre-built default for a popular Schema.org type/property combination.
    /// </summary>
    private sealed record PopularSchemaDefault(string SourceType, string? NestedSchemaTypeName, string? ResolverConfig);
}
