using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Umbraco.Cms.Core.Models;
using Umbraco.Cms.Core.Services;
using Umbraco.Community.SchemeWeaver.Models.Api;
using Umbraco.Community.SchemeWeaver.Services.Mapping;

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
    private readonly IDataTypeService? _dataTypeService;
    private readonly ILogger<SchemaAutoMapper>? _logger;
    private readonly int _autoApplyThreshold;
    private readonly int _showThreshold;

    private static HashSet<string> BlockEditorAliases => SchemeWeaverConstants.PropertyEditors.BlockEditorAliases;

    private static HashSet<string> MediaPickerAliases => SchemeWeaverConstants.PropertyEditors.MediaPickerAliases;

    private static readonly HashSet<string> ContentPickerAliases = new(StringComparer.OrdinalIgnoreCase)
    {
        "Umbraco.ContentPicker",
        "Umbraco.MultiNodeTreePicker"
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
        ["author"] = ["authorName", "writer", "byline", "author", "attribution", "attributedTo", "citation"],
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
        ["reviewBody"] = ["reviewBody", "quote", "testimonial", "reviewText", "quoteText", "testimonialText"],
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
        // publisher on an article is the site publisher — reference the shared
        // Organization graph node (as the page types do), NOT a fresh empty
        // Organization shell. Matches ReferenceCandidates["publisher"].
        ["Article.publisher"] = new("reference", null, null, "organization"),

        ["BlogPosting.author"] = new("complexType", "Person", null),
        ["BlogPosting.publisher"] = new("reference", null, null, "organization"),

        ["Recipe.recipeIngredient"] = new("blockContent", null,
            """{"extractAs":"stringList","contentProperty":"ingredient"}"""),
        ["Recipe.recipeInstructions"] = new("blockContent", "HowToStep",
            """{"nestedMappings":[{"schemaProperty":"name","contentProperty":"stepName"},{"schemaProperty":"text","contentProperty":"stepText"}]}"""),
        ["Recipe.nutrition"] = new("complexType", "NutritionInformation", null),
        ["Recipe.author"] = new("complexType", "Person", null),

        ["LocalBusiness.address"] = new("complexType", "PostalAddress", null),
        ["LocalBusiness.openingHoursSpecification"] = new("blockContent", "OpeningHoursSpecification", null),
        ["LocalBusiness.geo"] = new("complexType", "GeoCoordinates", null),
        // logo stays "property"-sourced: a matched media property resolves to a
        // fully-populated ImageObject via MediaPickerResolver (exactly like "image").
        // A complexType/ImageObject default here is a trap — the enricher would bind
        // ImageObject.Name <- the media alias and the string-only setter drops the
        // resolved media, emitting an empty {"@type":"ImageObject"} shell. The key is
        // kept so RankSchemaProperties still ranks logo at confidence 100.
        ["LocalBusiness.logo"] = new("property", null, null),
        ["LocalBusiness.contactPoint"] = new("blockContent", "ContactPoint", null),
        ["LocalBusiness.hasCredential"] = new("blockContent", "EducationalOccupationalCredential", null),
        ["LocalBusiness.makesOffer"] = new("blockContent", "Offer", null),
        ["LocalBusiness.founder"] = new("complexType", "Person", null),

        // RealEstateAgent extends LocalBusiness; same defaults apply but keyed
        // on the subtype so auto-map picks them up for mappings against it.
        ["RealEstateAgent.address"] = new("complexType", "PostalAddress", null),
        ["RealEstateAgent.openingHoursSpecification"] = new("blockContent", "OpeningHoursSpecification", null),
        ["RealEstateAgent.geo"] = new("complexType", "GeoCoordinates", null),
        ["RealEstateAgent.logo"] = new("property", null, null),
        ["RealEstateAgent.contactPoint"] = new("blockContent", "ContactPoint", null),
        ["RealEstateAgent.hasCredential"] = new("blockContent", "EducationalOccupationalCredential", null),
        ["RealEstateAgent.makesOffer"] = new("blockContent", "Offer", null),
        ["RealEstateAgent.founder"] = new("complexType", "Person", null),
        ["RealEstateAgent.areaServed"] = new("blockContent", "City", null),

        // Organization-level defaults (apply when the mapping is plain Organization
        // rather than a LocalBusiness subtype).
        ["Organization.address"] = new("complexType", "PostalAddress", null),
        ["Organization.logo"] = new("property", null, null),
        ["Organization.contactPoint"] = new("blockContent", "ContactPoint", null),
        ["Organization.founder"] = new("complexType", "Person", null),

        // NewsArticle / TechArticle (inherit Article patterns)
        ["NewsArticle.author"] = new("complexType", "Person", null),
        ["NewsArticle.publisher"] = new("reference", null, null, "organization"),
        ["TechArticle.author"] = new("complexType", "Person", null),
        ["TechArticle.publisher"] = new("reference", null, null, "organization"),

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
        ["Book.publisher"] = new("reference", null, null, "organization"),
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

    public SchemaAutoMapper(
        IContentTypeService contentTypeService,
        ISchemaTypeRegistry schemaTypeRegistry,
        IOptions<SchemaAutoMapperOptions>? options = null,
        IDataTypeService? dataTypeService = null,
        ILogger<SchemaAutoMapper>? logger = null)
    {
        _contentTypeService = contentTypeService;
        _schemaTypeRegistry = schemaTypeRegistry;
        _dataTypeService = dataTypeService;
        _logger = logger;

        var opts = options?.Value ?? new SchemaAutoMapperOptions();
        _autoApplyThreshold = opts.AutoApplyConfidenceThreshold;
        _showThreshold = opts.ShowConfidenceThreshold;
    }

    /// <summary>
    /// Heuristic mapping is synchronous; this just wraps it so the seam can be awaited.
    /// The AI satellite overrides <see cref="ISchemaAutoMapper.SuggestMappingsAsync"/> with a real async call.
    /// </summary>
    public Task<IEnumerable<PropertyMappingSuggestion>> SuggestMappingsAsync(string contentTypeAlias, string schemaTypeName)
        => Task.FromResult(SuggestMappings(contentTypeAlias, schemaTypeName));

    public IEnumerable<PropertyMappingSuggestion> SuggestMappings(string contentTypeAlias, string schemaTypeName)
    {
        var contentType = _contentTypeService.Get(contentTypeAlias);
        if (contentType is null)
            return [];

        var schemaProperties = _schemaTypeRegistry.GetProperties(schemaTypeName).ToList();
        var contentProperties = contentType.CompositionPropertyTypes.ToList();
        var suggestions = new List<PropertyMappingSuggestion>();

        foreach (var schemaProp in schemaProperties)
        {
            var suggestion = new PropertyMappingSuggestion
            {
                SchemaPropertyName = schemaProp.Name,
                SchemaPropertyType = schemaProp.PropertyType,
                SuggestedSourceType = SchemeWeaverConstants.SourceTypes.Property,
                AcceptedTypes = schemaProp.AcceptedTypes,
                IsComplexType = schemaProp.IsComplexType,
            };

            // Popular schema defaults are consulted both by the matched tiers (via
            // ApplyComplexTypeInference) and by the no-match ladder in ResolveUnmatched.
            var defaultKey = $"{schemaTypeName}.{schemaProp.Name}";
            var hasPopularDefault = PopularSchemaDefaults.TryGetValue(defaultKey, out var popularDefault);
            Synonyms.TryGetValue(schemaProp.Name, out var synonyms);

            // Tier precedence: exact → synonym → partial → built-in, FIRST match wins.
            // Exact/synonym/partial share one populate-and-return helper, differing only in
            // their base confidence and candidate matcher; `||` short-circuits so the first
            // tier that matches-and-populates stops the ladder (as the old `continue`s did).
            var matched =
                TryMatchTier(suggestion, schemaProp, contentProperties, 100,
                    p => string.Equals(p.Alias, schemaProp.Name, StringComparison.OrdinalIgnoreCase),
                    hasPopularDefault, popularDefault)
                || TryMatchTier(suggestion, schemaProp, contentProperties, 80,
                    p => synonyms is not null
                        && synonyms.Any(s => string.Equals(p.Alias, s, StringComparison.OrdinalIgnoreCase)),
                    hasPopularDefault, popularDefault)
                || TryMatchTier(suggestion, schemaProp, contentProperties, 50,
                    p => p.Alias.Contains(schemaProp.Name, StringComparison.OrdinalIgnoreCase)
                        || schemaProp.Name.Contains(p.Alias, StringComparison.OrdinalIgnoreCase),
                    hasPopularDefault, popularDefault)
                || TryBuiltIn(suggestion, schemaProp);

            if (!matched)
                ResolveUnmatched(suggestion, schemaProp, contentProperties, hasPopularDefault, popularDefault);

            suggestions.Add(suggestion);
        }

        // Structural enrichment pass: derive correct rich structures (complexType inner bindings,
        // blockContent string lists / nested mappings) the flat loop above left missing. Runs
        // BEFORE the threshold filter so a structurally-confirmed row can have its confidence
        // floored to the show threshold and survive. Priors (popular defaults, synonyms) already
        // applied above win where present; this only fills the gaps.
        var contentPropertyAliases = contentProperties.Select(p => p.Alias).ToList();
        var blockCache = new Dictionary<string, IReadOnlyList<BlockElementTypeInfo>>(StringComparer.OrdinalIgnoreCase);

        IReadOnlyList<BlockElementTypeInfo> BlockElementsFor(string alias)
        {
            if (!blockCache.TryGetValue(alias, out var elements))
            {
                elements = GetBlockElements(contentType, alias);
                blockCache[alias] = elements;
            }
            return elements;
        }

        var enricher = new StructuralEnricher(_schemaTypeRegistry, MatchPropertyAlias, _showThreshold, _logger);
        enricher.Enrich(suggestions, contentPropertyAliases, BlockElementsFor);

        // Threshold pass (authoritative over the per-branch IsAutoMapped values above):
        //  - auto-apply only the genuinely-reliable rows (>= AutoApplyConfidenceThreshold);
        //  - keep plausible rows (>= ShowConfidenceThreshold) as "click to accept";
        //  - drop the junk below the show threshold (partial-name matches at 50, generic
        //    block fallbacks at 40, no-match slots at 0) so it never reaches the UI.
        foreach (var suggestion in suggestions)
            suggestion.IsAutoMapped = suggestion.Confidence >= _autoApplyThreshold;

        return suggestions
            .Where(s => s.Confidence >= _showThreshold)
            .ToList();
    }

    /// <summary>
    /// Populate-and-return for the exact / synonym / partial name-match tiers, which are identical
    /// apart from their base confidence and candidate matcher. On a match it fills the suggestion
    /// (alias, editor, editor-boosted confidence, auto-mapped) and runs <see cref="ApplyComplexTypeInference"/>
    /// exactly as the flat tiers did, then returns true; returns false (leaving the suggestion untouched)
    /// when no candidate matches so the caller can try the next tier.
    /// </summary>
    private static bool TryMatchTier(
        PropertyMappingSuggestion suggestion,
        SchemaPropertyInfo schemaProp,
        List<IPropertyType> contentProperties,
        int baseConfidence,
        Func<IPropertyType, bool> matcher,
        bool hasPopularDefault,
        PopularSchemaDefault? popularDefault)
    {
        var match = contentProperties.FirstOrDefault(matcher);
        if (match is null)
            return false;

        suggestion.SuggestedContentTypePropertyAlias = match.Alias;
        suggestion.EditorAlias = match.PropertyEditorAlias;
        suggestion.Confidence = BoostForEditorMatch(baseConfidence, match.PropertyEditorAlias, schemaProp);
        suggestion.IsAutoMapped = true;

        ApplyComplexTypeInference(suggestion, match.PropertyEditorAlias, hasPopularDefault, popularDefault);
        return true;
    }

    /// <summary>
    /// Built-in property auto-mapping (URL, Name, dates) used as a fallback when no custom property
    /// matched. Only applies to non-complex schema properties. Canonical built-in mappings
    /// (schema url → node url, name → node Name, datePublished → CreateDate, dateModified → UpdateDate)
    /// are scored at the auto-apply bar so they stay pre-ticked after the confidence filter.
    /// </summary>
    private static bool TryBuiltIn(PropertyMappingSuggestion suggestion, SchemaPropertyInfo schemaProp)
    {
        if (schemaProp.IsComplexType)
            return false;

        var builtInAlias = TryMatchBuiltInProperty(schemaProp);
        if (builtInAlias is null)
            return false;

        suggestion.SuggestedContentTypePropertyAlias = builtInAlias;
        suggestion.EditorAlias = SchemeWeaverConstants.BuiltInProperties.EditorAlias;
        suggestion.Confidence = 80;
        suggestion.IsAutoMapped = true;
        return true;
    }

    /// <summary>
    /// No content property matched: resolves the suggestion from popular defaults, reference
    /// candidates and complex-type fallbacks. A <c>property</c>-sourced popular default (e.g. *.logo)
    /// is EXCLUDED here — it needs a real content property to bind, so with no match it falls through
    /// to the generic complex handling instead of authoring a dead alias-less row (mirrors
    /// <see cref="ApplyComplexTypeInference"/>).
    /// </summary>
    private static void ResolveUnmatched(
        PropertyMappingSuggestion suggestion,
        SchemaPropertyInfo schemaProp,
        List<IPropertyType> contentProperties,
        bool hasPopularDefault,
        PopularSchemaDefault? popularDefault)
    {
        if (schemaProp.IsComplexType && hasPopularDefault
            && !string.Equals(popularDefault!.SourceType, SchemeWeaverConstants.SourceTypes.Property, StringComparison.OrdinalIgnoreCase))
        {
            ApplyPopularDefaultForUnmatched(suggestion, contentProperties, popularDefault!);
            return;
        }

        if (schemaProp.IsComplexType)
        {
            ResolveUnmatchedComplex(suggestion, schemaProp, contentProperties);
            return;
        }

        if (ReferenceCandidates.TryGetValue(schemaProp.Name, out var targetPieceKey))
        {
            // Non-complex property with a known cross-piece ref name — rare but handles e.g. future
            // primitive refs. Low confidence because we're guessing from name alone.
            suggestion.SuggestedSourceType = SchemeWeaverConstants.SourceTypes.Reference;
            suggestion.SuggestedTargetPieceKey = targetPieceKey;
            suggestion.Confidence = 50;
            suggestion.IsAutoMapped = true;
            return;
        }

        suggestion.Confidence = 0;
        suggestion.IsAutoMapped = false;
    }

    /// <summary>
    /// Applies a non-<c>property</c> popular default to an unmatched complex suggestion. Reference
    /// defaults (e.g. AboutPage.about → org piece) resolve at graph-generation time and need no block
    /// property; blockContent defaults only auto-map when a matching block property exists; everything
    /// else is a shown-but-not-auto-applied complexType default.
    /// </summary>
    private static void ApplyPopularDefaultForUnmatched(
        PropertyMappingSuggestion suggestion,
        List<IPropertyType> contentProperties,
        PopularSchemaDefault popularDefault)
    {
        suggestion.SuggestedSourceType = popularDefault.SourceType;
        suggestion.SuggestedNestedSchemaTypeName = popularDefault.NestedSchemaTypeName;
        suggestion.SuggestedResolverConfig = popularDefault.ResolverConfig;
        suggestion.SuggestedTargetPieceKey = popularDefault.TargetPieceKey;

        // Case-sensitive switch (matches the render path's `==` comparison policy for source types).
        switch (popularDefault.SourceType)
        {
            case SchemeWeaverConstants.SourceTypes.Reference:
                suggestion.Confidence = 90;
                suggestion.IsAutoMapped = true;
                break;

            case SchemeWeaverConstants.SourceTypes.BlockContent:
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
                break;
            }

            default:
                suggestion.Confidence = 60;
                suggestion.IsAutoMapped = true;
                break;
        }
    }

    /// <summary>
    /// Unmatched complex property with no usable popular default. A known cross-piece ref name
    /// (about, publisher, worksFor, isPartOf, …) becomes a one-click <c>reference</c> @id ref; an
    /// array property with a Block List/Grid present becomes a low-confidence blockContent guess; a
    /// primitive-only accepted-types set collapses to a simple unmatched row; otherwise it is a
    /// shown-but-not-applied complexType placeholder.
    /// </summary>
    private static void ResolveUnmatchedComplex(
        PropertyMappingSuggestion suggestion,
        SchemaPropertyInfo schemaProp,
        List<IPropertyType> contentProperties)
    {
        var nestedType = GetFirstNonPrimitiveAcceptedType(schemaProp.AcceptedTypes);
        if (nestedType is null)
        {
            // All accepted types are primitive (e.g. String) — treat as simple unmatched.
            suggestion.IsComplexType = false;
            suggestion.Confidence = 0;
            suggestion.IsAutoMapped = false;
            return;
        }

        if (ReferenceCandidates.TryGetValue(schemaProp.Name, out var targetPieceKey))
        {
            suggestion.SuggestedSourceType = SchemeWeaverConstants.SourceTypes.Reference;
            suggestion.SuggestedTargetPieceKey = targetPieceKey;
            suggestion.Confidence = 70;
            suggestion.IsAutoMapped = true;
        }
        else if (IsArrayProperty(schemaProp)
            && contentProperties.FirstOrDefault(p => BlockEditorAliases.Contains(p.PropertyEditorAlias)) is { } blockProp)
        {
            suggestion.SuggestedSourceType = SchemeWeaverConstants.SourceTypes.BlockContent;
            suggestion.SuggestedNestedSchemaTypeName = nestedType;
            suggestion.SuggestedContentTypePropertyAlias = blockProp.Alias;
            suggestion.EditorAlias = blockProp.PropertyEditorAlias;
            suggestion.Confidence = 40;
            suggestion.IsAutoMapped = true;
        }
        else
        {
            suggestion.SuggestedSourceType = SchemeWeaverConstants.SourceTypes.ComplexType;
            suggestion.SuggestedNestedSchemaTypeName = nestedType;
            suggestion.Confidence = 0;
            suggestion.IsAutoMapped = false;
        }
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
            // Confidence stays at whatever the name match earned (exact 100 / synonym 80 /
            // partial 50). A block editor doesn't make a strong name match weaker, nor a
            // weak one stronger — so partial-name block matches stay below the show
            // threshold and drop out, while exact/synonym block matches auto-apply.
            // "property"-sourced defaults (e.g. *.logo) are meaningless for a block
            // editor, so they fall through to the generic blockContent shape.
            if (hasPopularDefault
                && !string.Equals(popularDefault!.SourceType, "property", StringComparison.OrdinalIgnoreCase))
            {
                suggestion.SuggestedSourceType = popularDefault!.SourceType;
                suggestion.SuggestedNestedSchemaTypeName = popularDefault.NestedSchemaTypeName;
                suggestion.SuggestedResolverConfig = popularDefault.ResolverConfig;
                suggestion.SuggestedTargetPieceKey = popularDefault.TargetPieceKey;
            }
            else
            {
                suggestion.SuggestedSourceType = "blockContent";
                suggestion.SuggestedNestedSchemaTypeName = GetFirstNonPrimitiveAcceptedType(suggestion.AcceptedTypes);
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
        else if (MediaPickerAliases.Contains(editorAlias)
            && AcceptsAny(suggestion.AcceptedTypes, "ImageObject", "MediaObject"))
        {
            // Media picker feeding an image-shaped property — keep the plain "property"
            // source and adopt nothing from any popular default: MediaPickerResolver
            // already yields fully-populated ImageObject(s) at render time. A nested
            // complexType here would strand the resolved media in an empty shell
            // (its inner bindings land on string-only sub-properties like Name).
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

        var accepted = schemaProp.AcceptedTypes ?? [];
        var propertyType = schemaProp.PropertyType ?? string.Empty;

        // The rules are mutually exclusive by editor family, and the boost is a flat +15 (never
        // cumulative), so evaluating them as `Any` is equivalent to the old if/else-if chain.
        var boosted = EditorBoostRules.Any(r => r.EditorMatches(editorAlias) && r.TargetMatches(accepted, propertyType));

        return boosted ? Math.Min(100, baseConfidence + 15) : baseConfidence;
    }

    /// <summary>
    /// A single editor↔target-shape alignment rule for <see cref="BoostForEditorMatch"/>.
    /// <paramref name="EditorMatches"/> tests the Umbraco editor alias; <paramref name="TargetMatches"/>
    /// tests the Schema.org target (accepted types + property type). Both are OrdinalIgnoreCase.
    /// </summary>
    private sealed record EditorBoostRule(
        Func<string, bool> EditorMatches,
        Func<List<string>, string, bool> TargetMatches);

    /// <summary>
    /// Editor-alias → target-shape boost rules. Each matched rule adds a flat +15 (capped at 100 by
    /// the caller). MediaPicker uses substring <c>Contains</c>; the others match the editor alias
    /// exactly with <c>Equals</c>. Order is irrelevant — the caller uses <c>Any</c>.
    /// </summary>
    private static readonly EditorBoostRule[] EditorBoostRules =
    [
        // MediaPicker → image-shaped schema properties (ImageObject, MediaObject,
        // ImageObject-accepting Thing fields like logo / image / photo).
        new(
            editor => editor.Contains("MediaPicker", StringComparison.OrdinalIgnoreCase),
            (accepted, propertyType) => AcceptsAny(accepted, "ImageObject", "MediaObject")
                || propertyType.Contains("ImageObject", StringComparison.OrdinalIgnoreCase)),

        // DateTime picker → Date-family schema properties (DateTime, Date, Time).
        new(
            editor => editor.Equals("Umbraco.DateTime", StringComparison.OrdinalIgnoreCase),
            (accepted, propertyType) => propertyType.Contains("DateTime", StringComparison.OrdinalIgnoreCase)
                || propertyType.Contains("Date", StringComparison.OrdinalIgnoreCase)
                || propertyType.Contains("Time", StringComparison.OrdinalIgnoreCase)
                || AcceptsAny(accepted, "DateTime", "Date", "Time")),

        // MultiUrlPicker → URL-shaped properties (SameAs arrays, primary URL fields).
        new(
            editor => editor.Equals("Umbraco.MultiUrlPicker", StringComparison.OrdinalIgnoreCase),
            (accepted, propertyType) => propertyType.Contains("URL", StringComparison.OrdinalIgnoreCase)
                || AcceptsAny(accepted, "URL")),

        // Tags / MultipleTextstring → text-array properties (keywords, sameAs).
        new(
            editor => editor.Equals("Umbraco.Tags", StringComparison.OrdinalIgnoreCase)
                || editor.Equals("Umbraco.MultipleTextstring", StringComparison.OrdinalIgnoreCase),
            (accepted, propertyType) => propertyType.Contains("Text", StringComparison.OrdinalIgnoreCase)
                || AcceptsAny(accepted, "Text")),
    ];

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
    /// Exact &gt; synonym match of a schema property name against a set of candidate content/element
    /// property aliases — the same precedence the flat loop uses, minus partial matching. Used by the
    /// structural enricher to bind nested sub-properties; partial (substring) matching is deliberately
    /// excluded here because, inside a nested type with many properties, it produces spurious bindings
    /// (e.g. a <c>reviewDate</c> field sticking to an unrelated property whose name is a substring).
    /// </summary>
    private string? MatchPropertyAlias(string schemaPropName, IReadOnlyList<string> candidates)
    {
        if (candidates.Count == 0 || string.IsNullOrEmpty(schemaPropName))
            return null;

        var exact = candidates.FirstOrDefault(a => string.Equals(a, schemaPropName, StringComparison.OrdinalIgnoreCase));
        if (exact is not null)
            return exact;

        if (Synonyms.TryGetValue(schemaPropName, out var syns))
        {
            var synonym = candidates.FirstOrDefault(a => syns.Any(s => string.Equals(a, s, StringComparison.OrdinalIgnoreCase)));
            if (synonym is not null)
                return synonym;
        }

        return null;
    }

    /// <summary>
    /// Synchronously resolves the block element types behind a Block List/Grid content property
    /// (alias + per-property editor aliases, one level deep — enough for string-list detection and
    /// nested-mapping supplementation). Returns empty when block introspection is unavailable
    /// (no <see cref="IDataTypeService"/> injected, e.g. in unit tests) or the property is not a block.
    /// </summary>
    private IReadOnlyList<BlockElementTypeInfo> GetBlockElements(IContentType contentType, string propertyAlias)
    {
        if (_dataTypeService is null)
            return [];

        var property = contentType.CompositionPropertyTypes.FirstOrDefault(
            p => string.Equals(p.Alias, propertyAlias, StringComparison.OrdinalIgnoreCase));
        if (property is null || !BlockEditorAliases.Contains(property.PropertyEditorAlias))
            return [];

        IDataType? dataType;
        try
        {
            dataType = _dataTypeService.GetAsync(property.DataTypeKey).GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            _logger?.LogDebug(ex,
                "Failed to load data type {DataTypeKey} for block property {PropertyAlias} — skipping block introspection",
                property.DataTypeKey, propertyAlias);
            return [];
        }

        if (dataType is null)
            return [];

        var elementKeys = ParseBlockElementKeys(dataType);
        if (elementKeys.Count == 0)
            return [];

        return elementKeys
            .Select(key => _contentTypeService.Get(key))
            .OfType<IContentType>()
            .Select(elementType => new BlockElementTypeInfo
            {
                Alias = elementType.Alias,
                Name = elementType.Name ?? elementType.Alias,
                Properties = elementType.CompositionPropertyTypes.Select(p => p.Alias).ToList(),
                PropertyInfos = elementType.CompositionPropertyTypes.Select(p => new BlockElementPropertyInfo
                {
                    Alias = p.Alias,
                    Name = p.Name ?? p.Alias,
                    EditorAlias = p.PropertyEditorAlias,
                }).ToList(),
            })
            .ToList();
    }

    /// <summary>
    /// Extracts content element type keys from a BlockList/BlockGrid data type's configuration JSON.
    /// Mirrors the extraction in <see cref="SchemeWeaverService"/> (kept local to avoid a service
    /// dependency cycle through the synchronous auto-mapper path).
    /// </summary>
    private static List<Guid> ParseBlockElementKeys(IDataType dataType)
    {
        var keys = new List<Guid>();
        if (dataType.ConfigurationData is null
            || !dataType.ConfigurationData.TryGetValue("blocks", out var blocksValue))
            return keys;

        try
        {
            var blocksJson = blocksValue?.ToString();
            if (string.IsNullOrEmpty(blocksJson))
                return keys;

            using var doc = JsonDocument.Parse(blocksJson);
            if (doc.RootElement.ValueKind != JsonValueKind.Array)
                return keys;

            foreach (var block in doc.RootElement.EnumerateArray())
            {
                if (block.TryGetProperty("contentElementTypeKey", out var keyProp)
                    && Guid.TryParse(keyProp.GetString(), out var elementKey))
                {
                    keys.Add(elementKey);
                }
            }
        }
        catch (JsonException)
        {
            // Configuration format not as expected — return whatever we collected.
        }

        return keys;
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
