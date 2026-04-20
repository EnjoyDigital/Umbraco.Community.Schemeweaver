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
    /// Schema.org property names that are broadly useful across most types. Used by
    /// <see cref="RankSchemaProperties"/> as the tier-2 scoring bucket (confidence 80).
    /// </summary>
    private static readonly HashSet<string> GlobalPopularPropertyNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "name",
        "description",
        "image",
        "url",
        "headline",
        "author",
        "datePublished",
        "dateModified",
        "sku",
        "price",
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
        ["sameAs"] = ["sameAs", "socialLinks", "profiles", "socialMedia", "social", "socials"],
        ["alumniOf"] = ["alumniOf", "education", "university"],

        // Organization (additional)
        ["foundingDate"] = ["foundingDate", "founded", "established"],
        ["numberOfEmployees"] = ["numberOfEmployees", "teamSize", "employees"],
        ["legalName"] = ["legalName", "registeredName", "companyName"],
        ["slogan"] = ["slogan", "tagline", "strapline"],
        ["currenciesAccepted"] = ["currenciesAccepted", "currency", "acceptedCurrency"],

        // Cross-entity references (used with `reference` source type when no
        // content property matches — keeps synonyms consistent for partial matches)
        ["publisher"] = ["publisher"],
        ["about"] = ["about", "aboutEntity"],
        ["mainEntity"] = ["mainEntity", "primaryEntity"],
        ["founder"] = ["founder", "founderPerson"],

        // Content (additional — generic bio / summary synonyms)
        ["biography"] = ["biography", "bio", "profile", "about"],
    };

    /// <summary>
    /// Schema.org properties that typically point at an Organization or Person
    /// piece in a Yoast-style graph. When there's no matching content property,
    /// the auto-mapper suggests <c>reference</c> source type with a target
    /// piece key so the user gets a ready-made cross-ref instead of an empty slot.
    /// </summary>
    private static readonly Dictionary<string, string> ReferenceCandidates = new(StringComparer.OrdinalIgnoreCase)
    {
        // Point at Organization piece
        ["publisher"] = "organization",
        ["about"] = "organization",
        ["sourceOrganization"] = "organization",
        ["provider"] = "organization",
        ["manufacturer"] = "organization",
        ["brand"] = "organization",
        ["worksFor"] = "organization",
        ["affiliation"] = "organization",
        ["memberOf"] = "organization",

        // Page-level container refs
        ["isPartOf"] = "website",
        ["breadcrumb"] = "breadcrumb",
        ["primaryImageOfPage"] = "primary-image",

        // MainEntity depends on context — most useful on AboutPage/ContactPage pointing at Organization
        ["mainEntity"] = "organization",
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
        ["LocalBusiness.logo"] = new("complexType", "ImageObject", null),
        ["LocalBusiness.contactPoint"] = new("blockContent", "ContactPoint", null),
        ["LocalBusiness.hasCredential"] = new("blockContent", "EducationalOccupationalCredential", null),
        ["LocalBusiness.makesOffer"] = new("blockContent", "Offer", null),
        ["LocalBusiness.founder"] = new("complexType", "Person", null),

        // RealEstateAgent extends LocalBusiness; same defaults apply but keyed
        // on the subtype so auto-map picks them up for mappings against it.
        ["RealEstateAgent.address"] = new("complexType", "PostalAddress", null),
        ["RealEstateAgent.openingHoursSpecification"] = new("blockContent", "OpeningHoursSpecification", null),
        ["RealEstateAgent.geo"] = new("complexType", "GeoCoordinates", null),
        ["RealEstateAgent.logo"] = new("complexType", "ImageObject", null),
        ["RealEstateAgent.contactPoint"] = new("blockContent", "ContactPoint", null),
        ["RealEstateAgent.hasCredential"] = new("blockContent", "EducationalOccupationalCredential", null),
        ["RealEstateAgent.makesOffer"] = new("blockContent", "Offer", null),
        ["RealEstateAgent.founder"] = new("complexType", "Person", null),
        ["RealEstateAgent.areaServed"] = new("blockContent", "City", null),

        // Organization-level defaults (apply when the mapping is plain Organization
        // rather than a LocalBusiness subtype).
        ["Organization.address"] = new("complexType", "PostalAddress", null),
        ["Organization.logo"] = new("complexType", "ImageObject", null),
        ["Organization.contactPoint"] = new("blockContent", "ContactPoint", null),
        ["Organization.founder"] = new("complexType", "Person", null),

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

        // AboutPage — the page describes the organisation; every cross-ref
        // resolves to a named graph piece via `reference` source type so the
        // output matches Yoast-style output shape (bare {"@id": ...} refs).
        ["AboutPage.about"] = new("reference", null, null, "organization"),
        ["AboutPage.mainEntity"] = new("reference", null, null, "organization"),
        ["AboutPage.isPartOf"] = new("reference", null, null, "website"),
        ["AboutPage.breadcrumb"] = new("reference", null, null, "breadcrumb"),
        ["AboutPage.primaryImageOfPage"] = new("reference", null, null, "primary-image"),
        ["AboutPage.publisher"] = new("reference", null, null, "organization"),

        // ContactPage — same pattern, the page describes the organisation.
        ["ContactPage.about"] = new("reference", null, null, "organization"),
        ["ContactPage.mainEntity"] = new("reference", null, null, "organization"),
        ["ContactPage.isPartOf"] = new("reference", null, null, "website"),
        ["ContactPage.breadcrumb"] = new("reference", null, null, "breadcrumb"),
        ["ContactPage.primaryImageOfPage"] = new("reference", null, null, "primary-image"),
        ["ContactPage.publisher"] = new("reference", null, null, "organization"),

        // Generic WebPage + ItemPage — isPartOf / breadcrumb / primaryImageOfPage
        // are always refs to the site-level pieces, regardless of the page's
        // specific subtype. The `about` / `mainEntity` fields are left unmapped
        // for these because they depend on the page's content.
        ["WebPage.isPartOf"] = new("reference", null, null, "website"),
        ["WebPage.breadcrumb"] = new("reference", null, null, "breadcrumb"),
        ["WebPage.primaryImageOfPage"] = new("reference", null, null, "primary-image"),
        ["WebPage.publisher"] = new("reference", null, null, "organization"),
        ["ItemPage.isPartOf"] = new("reference", null, null, "website"),
        ["ItemPage.breadcrumb"] = new("reference", null, null, "breadcrumb"),
        ["ItemPage.primaryImageOfPage"] = new("reference", null, null, "primary-image"),
        ["FAQPage.isPartOf"] = new("reference", null, null, "website"),
        ["FAQPage.breadcrumb"] = new("reference", null, null, "breadcrumb"),
        ["FAQPage.publisher"] = new("reference", null, null, "organization"),
        ["CollectionPage.isPartOf"] = new("reference", null, null, "website"),
        ["CollectionPage.breadcrumb"] = new("reference", null, null, "breadcrumb"),
        ["SearchResultsPage.isPartOf"] = new("reference", null, null, "website"),
        ["SearchResultsPage.breadcrumb"] = new("reference", null, null, "breadcrumb"),
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
                suggestion.Confidence = BoostForEditorMatch(100, exactMatch.PropertyEditorAlias, schemaProp);
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
                    suggestion.Confidence = BoostForEditorMatch(80, synonymMatch.PropertyEditorAlias, schemaProp);
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
                suggestion.Confidence = BoostForEditorMatch(50, partialMatch.PropertyEditorAlias, schemaProp);
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
                suggestion.SuggestedTargetPieceKey = popularDefault.TargetPieceKey;

                // Reference-typed popular defaults (e.g. AboutPage.about → org
                // piece) don't need a block property on the content type — they
                // resolve at graph-generation time from registered pieces.
                if (popularDefault.SourceType == "reference")
                {
                    suggestion.Confidence = 90;
                    suggestion.IsAutoMapped = true;
                }
                // For blockContent defaults, only auto-map if a matching block property exists
                else if (popularDefault.SourceType == "blockContent")
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
                var nestedType = GetFirstNonPrimitiveAcceptedType(schemaProp.AcceptedTypes);
                if (nestedType is not null)
                {
                    // Has a real complex type — if it's a known cross-piece ref
                    // candidate (about, publisher, worksFor, isPartOf, …), suggest
                    // `reference` so the user gets a one-click Yoast-style @id ref.
                    if (ReferenceCandidates.TryGetValue(schemaProp.Name, out var targetPieceKey))
                    {
                        suggestion.SuggestedSourceType = "reference";
                        suggestion.SuggestedTargetPieceKey = targetPieceKey;
                        suggestion.Confidence = 70;
                        suggestion.IsAutoMapped = true;
                    }
                    // Else: generic target-type fallback. If the content type has
                    // a BlockList/BlockGrid property we can plausibly map to a
                    // collection of the nested type, suggest that at low confidence
                    // so the user sees an actionable starting point instead of
                    // an empty slot.
                    else if (IsArrayProperty(schemaProp)
                        && contentProperties.FirstOrDefault(p => BlockEditorAliases.Contains(p.PropertyEditorAlias)) is { } blockProp)
                    {
                        suggestion.SuggestedSourceType = "blockContent";
                        suggestion.SuggestedNestedSchemaTypeName = nestedType;
                        suggestion.SuggestedContentTypePropertyAlias = blockProp.Alias;
                        suggestion.EditorAlias = blockProp.PropertyEditorAlias;
                        suggestion.Confidence = 40;
                        suggestion.IsAutoMapped = true;
                    }
                    else
                    {
                        suggestion.SuggestedSourceType = "complexType";
                        suggestion.SuggestedNestedSchemaTypeName = nestedType;
                        suggestion.Confidence = 0;
                        suggestion.IsAutoMapped = false;
                    }
                }
                else
                {
                    // All accepted types are primitive (e.g. String) — treat as simple unmatched
                    suggestion.IsComplexType = false;
                    suggestion.Confidence = 0;
                    suggestion.IsAutoMapped = false;
                }
            }
            else if (ReferenceCandidates.TryGetValue(schemaProp.Name, out var targetPieceKey2))
            {
                // Non-complex property with a known cross-piece ref name — rare
                // but handles e.g. future primitive refs. Low confidence because
                // we're guessing from name alone.
                suggestion.SuggestedSourceType = "reference";
                suggestion.SuggestedTargetPieceKey = targetPieceKey2;
                suggestion.Confidence = 50;
                suggestion.IsAutoMapped = true;
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

    public IEnumerable<RankedSchemaPropertyInfo> RankSchemaProperties(string schemaTypeName)
    {
        if (string.IsNullOrWhiteSpace(schemaTypeName))
            return [];

        var properties = _schemaTypeRegistry.GetProperties(schemaTypeName)?.ToList();
        if (properties is null || properties.Count == 0)
            return [];

        // Pre-compute the set of property names considered "popular" for this exact
        // schema type via PopularSchemaDefaults. Keys look like "Product.review" —
        // we extract the substring after the first "." for membership tests.
        var typePopularNames = new HashSet<string>(
            PopularSchemaDefaults.Keys
                .Where(key =>
                {
                    var dotIndex = key.IndexOf('.');
                    return dotIndex > 0 && dotIndex < key.Length - 1
                        && key.AsSpan(0, dotIndex).Equals(schemaTypeName.AsSpan(), StringComparison.OrdinalIgnoreCase);
                })
                .Select(key => key[(key.IndexOf('.') + 1)..]),
            StringComparer.OrdinalIgnoreCase);

        return properties
            .Select(prop =>
            {
                var confidence = typePopularNames.Contains(prop.Name) ? 100
                    : GlobalPopularPropertyNames.Contains(prop.Name) ? 80
                    : prop.IsComplexType ? 60
                    : 30;

                return new RankedSchemaPropertyInfo
                {
                    Name = prop.Name,
                    PropertyType = prop.PropertyType,
                    IsRequired = prop.IsRequired,
                    AcceptedTypes = prop.AcceptedTypes,
                    IsComplexType = prop.IsComplexType,
                    Confidence = confidence,
                    IsPopular = confidence >= 60,
                };
            })
            .OrderByDescending(p => p.Confidence)
            .ThenBy(p => p.Name, StringComparer.Ordinal)
            .ToList();
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
                suggestion.SuggestedTargetPieceKey = popularDefault.TargetPieceKey;
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
                suggestion.SuggestedTargetPieceKey = popularDefault.TargetPieceKey;
            }
        }
        else if (hasPopularDefault)
        {
            // Non-block, non-picker editor with a popular default
            suggestion.SuggestedSourceType = popularDefault!.SourceType;
            suggestion.SuggestedNestedSchemaTypeName = popularDefault.NestedSchemaTypeName;
            suggestion.SuggestedResolverConfig = popularDefault.ResolverConfig;
            suggestion.SuggestedTargetPieceKey = popularDefault.TargetPieceKey;
        }
    }

    /// <summary>
    /// Adds up to +15 confidence when the Umbraco editor alias is semantically
    /// aligned with the target Schema.org property type. E.g. a MediaPicker3
    /// feeding an ImageObject-typed property is a stronger match than an
    /// alias-only coincidence. Capped at 100 — a perfect-alias + perfect-editor
    /// match still reads as 100, not 115.
    /// </summary>
    private static int BoostForEditorMatch(int baseConfidence, string editorAlias, SchemaPropertyInfo schemaProp)
    {
        if (baseConfidence >= 100 || string.IsNullOrEmpty(editorAlias))
            return baseConfidence;

        var editor = editorAlias;
        var accepted = schemaProp.AcceptedTypes ?? [];
        var propertyType = schemaProp.PropertyType ?? string.Empty;

        var boosted = false;

        // MediaPicker → image-shaped schema properties (ImageObject, MediaObject,
        // ImageObject-accepting Thing fields like logo / image / photo).
        if (editor.Contains("MediaPicker", StringComparison.OrdinalIgnoreCase)
            && (AcceptsAny(accepted, "ImageObject", "MediaObject")
                || propertyType.Contains("ImageObject", StringComparison.OrdinalIgnoreCase)))
        {
            boosted = true;
        }

        // DateTime picker → Date-family schema properties (DateTime, Date, Time).
        else if (editor.Equals("Umbraco.DateTime", StringComparison.OrdinalIgnoreCase)
            && (propertyType.Contains("DateTime", StringComparison.OrdinalIgnoreCase)
                || propertyType.Contains("Date", StringComparison.OrdinalIgnoreCase)
                || propertyType.Contains("Time", StringComparison.OrdinalIgnoreCase)
                || AcceptsAny(accepted, "DateTime", "Date", "Time")))
        {
            boosted = true;
        }

        // MultiUrlPicker → URL-shaped properties (SameAs arrays, primary URL fields).
        else if (editor.Equals("Umbraco.MultiUrlPicker", StringComparison.OrdinalIgnoreCase)
            && (propertyType.Contains("URL", StringComparison.OrdinalIgnoreCase)
                || AcceptsAny(accepted, "URL")))
        {
            boosted = true;
        }

        // Tags / MultipleTextstring → text-array properties (keywords, sameAs).
        else if ((editor.Equals("Umbraco.Tags", StringComparison.OrdinalIgnoreCase)
                || editor.Equals("Umbraco.MultipleTextstring", StringComparison.OrdinalIgnoreCase))
            && (propertyType.Contains("Text", StringComparison.OrdinalIgnoreCase)
                || AcceptsAny(accepted, "Text")))
        {
            boosted = true;
        }

        return boosted ? Math.Min(100, baseConfidence + 15) : baseConfidence;
    }

    /// <summary>
    /// Heuristic: does the Schema.org property type look like it holds an array
    /// of entities? Schema.NET models plurality via <c>OneOrMany&lt;T&gt;</c> and
    /// <c>IList&lt;T&gt;</c>; the property type string reflects that. Treat
    /// plural schema.org names (ends in "s" plus known plurals) as arrays too.
    /// Used to decide whether a BlockList fallback is plausible.
    /// </summary>
    private static bool IsArrayProperty(SchemaPropertyInfo schemaProp)
    {
        var propertyType = schemaProp.PropertyType ?? string.Empty;
        if (propertyType.Contains("OneOrMany", StringComparison.OrdinalIgnoreCase)
            || propertyType.Contains("IList", StringComparison.OrdinalIgnoreCase)
            || propertyType.Contains("IEnumerable", StringComparison.OrdinalIgnoreCase)
            || propertyType.EndsWith("[]", StringComparison.OrdinalIgnoreCase))
            return true;

        // Schema.org property names ending in recognisable plural patterns.
        var name = schemaProp.Name ?? string.Empty;
        return name.EndsWith("s", StringComparison.OrdinalIgnoreCase)
            && !name.EndsWith("ss", StringComparison.OrdinalIgnoreCase)  // avoid "address", "business"
            && !name.EndsWith("us", StringComparison.OrdinalIgnoreCase)  // avoid "status"
            && !string.Equals(name, "sameAs", StringComparison.OrdinalIgnoreCase);
    }

    private static bool AcceptsAny(List<string> acceptedTypes, params string[] candidates)
    {
        if (acceptedTypes.Count == 0) return false;
        foreach (var candidate in candidates)
        {
            for (var i = 0; i < acceptedTypes.Count; i++)
            {
                if (string.Equals(acceptedTypes[i], candidate, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
        }
        return false;
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
            || (schemaProp.PropertyType?.Contains("URL", StringComparison.OrdinalIgnoreCase) ?? false))
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
    /// <paramref name="TargetPieceKey"/> is populated only for <c>reference</c>-type
    /// defaults (cross-piece @id refs to named graph pieces like Organization or WebSite).
    /// </summary>
    private sealed record PopularSchemaDefault(
        string SourceType,
        string? NestedSchemaTypeName,
        string? ResolverConfig,
        string? TargetPieceKey = null);
}
