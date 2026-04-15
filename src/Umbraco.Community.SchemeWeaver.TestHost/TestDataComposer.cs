using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Umbraco.Cms.Core;
using Umbraco.Cms.Core.Composing;
using Umbraco.Cms.Core.Models;
using Umbraco.Cms.Core.Models.ContentEditing;
using Umbraco.Cms.Core.Models.ContentPublishing;
using Umbraco.Cms.Core.PropertyEditors;
using Umbraco.Cms.Core.Serialization;
using Umbraco.Cms.Core.Services;
using Umbraco.Cms.Core.Strings;
using Umbraco.Community.SchemeWeaver.Models.Entities;
using Umbraco.Community.SchemeWeaver.Persistence;

namespace Umbraco.Community.SchemeWeaver.TestHost;

/// <summary>
/// Seeds document types, sample content, and schema mappings for e2e testing.
/// Creates element types, BlockList data types, sample document types,
/// published content nodes with block list items, and default schema mappings
/// so the SchemeWeaver dashboard has rich data to display.
/// </summary>
public class TestDataComposer : IComposer
{
    public void Compose(IUmbracoBuilder builder)
    {
        builder.Services.AddHostedService<TestDataSeeder>();
    }
}

public class TestDataSeeder : Microsoft.Extensions.Hosting.IHostedService
{
    private readonly IContentTypeService _contentTypeService;
    private readonly IDataTypeService _dataTypeService;
    private readonly IShortStringHelper _shortStringHelper;
    private readonly PropertyEditorCollection _propertyEditors;
    private readonly IConfigurationEditorJsonSerializer _configSerializer;
    private readonly IContentService _contentService;
    private readonly IContentPublishingService _contentPublishingService;
    private readonly IFileService _fileService;
    private readonly ITemplateService _templateService;
    private readonly IMediaImportService _mediaImportService;
    private readonly IWebHostEnvironment _webHostEnvironment;
    private readonly ILanguageService _languageService;
    private readonly IDomainService _domainService;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<TestDataSeeder> _logger;

    /// <summary>Media keys seeded during startup, available for content property assignment.</summary>
    private readonly Dictionary<string, Guid> _mediaKeys = new();

    /// <summary>MediaPicker3 data type captured during <see cref="StartAsync"/> so <see cref="CreateContentType"/> can add a universal "heroImage" property to every doctype.</summary>
    private IDataType? _mediaPickerDataType;

    public TestDataSeeder(
        IContentTypeService contentTypeService,
        IDataTypeService dataTypeService,
        IShortStringHelper shortStringHelper,
        PropertyEditorCollection propertyEditors,
        IConfigurationEditorJsonSerializer configSerializer,
        IContentService contentService,
        IContentPublishingService contentPublishingService,
        IFileService fileService,
        ITemplateService templateService,
        IMediaImportService mediaImportService,
        IWebHostEnvironment webHostEnvironment,
        ILanguageService languageService,
        IDomainService domainService,
        IServiceScopeFactory scopeFactory,
        ILogger<TestDataSeeder> logger)
    {
        _contentTypeService = contentTypeService;
        _dataTypeService = dataTypeService;
        _shortStringHelper = shortStringHelper;
        _propertyEditors = propertyEditors;
        _configSerializer = configSerializer;
        _contentService = contentService;
        _contentPublishingService = contentPublishingService;
        _fileService = fileService;
        _templateService = templateService;
        _mediaImportService = mediaImportService;
        _webHostEnvironment = webHostEnvironment;
        _languageService = languageService;
        _domainService = domainService;
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        // Only seed if no content types exist yet
        var existing = _contentTypeService.GetAll();
        if (existing.Any())
            return;

        // Add German language for variant testing
        var germanExists = await _languageService.GetAsync("de-DE");
        if (germanExists is null)
        {
            var german = new Language("de-DE", "German (Germany)") { IsDefault = false, IsMandatory = false };
            var langResult = await _languageService.CreateAsync(german, Constants.Security.SuperUserKey);
            _logger.LogInformation("TestDataSeeder: German language creation result: {Status}", langResult.Status);
        }

        // Fetch base data types
        var textboxDataType = (await _dataTypeService
            .GetByEditorAliasAsync(Constants.PropertyEditors.Aliases.TextBox)
            .ConfigureAwait(false))
            .FirstOrDefault();

        var textareaDataType = (await _dataTypeService
            .GetByEditorAliasAsync(Constants.PropertyEditors.Aliases.TextArea)
            .ConfigureAwait(false))
            .FirstOrDefault();

        var richtextDataType = (await _dataTypeService
            .GetByEditorAliasAsync(Constants.PropertyEditors.Aliases.RichText)
            .ConfigureAwait(false))
            .FirstOrDefault();

        var dateTimeDataType = (await _dataTypeService
            .GetByEditorAliasAsync(Constants.PropertyEditors.Aliases.DateTime)
            .ConfigureAwait(false))
            .FirstOrDefault();

        var mediaPickerDataType = (await _dataTypeService
            .GetByEditorAliasAsync(Constants.PropertyEditors.Aliases.MediaPicker3)
            .ConfigureAwait(false))
            .FirstOrDefault();
        _mediaPickerDataType = mediaPickerDataType;

        var bodyDataType = richtextDataType ?? textareaDataType ?? textboxDataType;
        var descDataType = textareaDataType ?? textboxDataType;

        if (textboxDataType is null) return;

        // 1. Create element types
        var faqItem = await CreateElementType("faqItem", "FAQ Item", new[]
        {
            ("question", "Question", textboxDataType),
            ("answer", "Answer", textareaDataType ?? textboxDataType),
        }, cancellationToken);

        var reviewItem = await CreateElementType("reviewItem", "Review Item", new[]
        {
            ("reviewAuthor", "Review Author", textboxDataType),
            ("ratingValue", "Rating Value", textboxDataType),
            ("reviewBody", "Review Body", textareaDataType ?? textboxDataType),
            ("reviewDate", "Review Date", dateTimeDataType ?? textboxDataType),
        }, cancellationToken);

        var recipeIngredient = await CreateElementType("recipeIngredient", "Recipe Ingredient", new[]
        {
            ("ingredient", "Ingredient", textboxDataType),
        }, cancellationToken);

        var recipeStep = await CreateElementType("recipeStep", "Recipe Step", new[]
        {
            ("stepName", "Step Name", textboxDataType),
            ("stepText", "Step Text", textareaDataType ?? textboxDataType),
        }, cancellationToken);

        // 1b. Create new element types for expanded demo
        var howToStepEl = await CreateElementType("howToStep", "How-To Step", new[]
        {
            ("stepName", "Step Name", textboxDataType),
            ("stepText", "Step Text", textareaDataType ?? textboxDataType),
        }, cancellationToken);

        var howToToolEl = await CreateElementType("howToTool", "How-To Tool", new[]
        {
            ("toolName", "Tool Name", textboxDataType),
        }, cancellationToken);

        var openingHoursEl = await CreateElementType("openingHoursItem", "Opening Hours", new[]
        {
            ("dayOfWeek", "Day of Week", textboxDataType),
            ("opens", "Opens", textboxDataType),
            ("closes", "Closes", textboxDataType),
        }, cancellationToken);

        // 1c. Block Grid element types (landing page demo)
        var heroBlockEl = await CreateElementType("heroBlock", "Hero Block", new[]
        {
            ("title", "Title", textboxDataType),
            ("subtitle", "Subtitle", textboxDataType),
            ("heroImage", "Hero Image", mediaPickerDataType ?? textboxDataType),
        }, cancellationToken);

        var featureBlockEl = await CreateElementType("featureBlock", "Feature Block", new[]
        {
            ("title", "Title", textboxDataType),
            ("description", "Description", richtextDataType ?? textareaDataType ?? textboxDataType),
            ("featureImage", "Feature Image", mediaPickerDataType ?? textboxDataType),
        }, cancellationToken);

        var quoteBlockEl = await CreateElementType("quoteBlock", "Quote Block", new[]
        {
            ("quote", "Quote", textboxDataType),
            ("attribution", "Attribution", textboxDataType),
        }, cancellationToken);

        // 2. Create BlockList data types
        var faqBlockList = await CreateBlockListDataType("FAQ Items Block List", [faqItem], cancellationToken);
        var reviewsBlockList = await CreateBlockListDataType("Reviews Block List", [reviewItem], cancellationToken);
        var ingredientsBlockList = await CreateBlockListDataType("Ingredients Block List", [recipeIngredient], cancellationToken);
        var instructionsBlockList = await CreateBlockListDataType("Instructions Block List", [recipeStep], cancellationToken);
        var howToStepsBlockList = await CreateBlockListDataType("HowTo Steps Block List", [howToStepEl], cancellationToken);
        var howToToolsBlockList = await CreateBlockListDataType("HowTo Tools Block List", [howToToolEl], cancellationToken);
        var openingHoursBlockList = await CreateBlockListDataType("Opening Hours Block List", [openingHoursEl], cancellationToken);

        // 2b. Create BlockGrid data type (landing page demo)
        var contentGridDataType = await CreateBlockGridDataType(
            "Content Grid",
            [heroBlockEl, featureBlockEl, quoteBlockEl],
            cancellationToken);

        // 3. Create content types — existing
        var blogArticleCt = await CreateBlogArticle(textboxDataType, descDataType, bodyDataType, dateTimeDataType, mediaPickerDataType, cancellationToken);
        var productPageCt = await CreateProductPage(textboxDataType, descDataType, bodyDataType, mediaPickerDataType, reviewsBlockList, cancellationToken);
        var faqPageCt = await CreateFaqPage(textboxDataType, descDataType, faqBlockList, cancellationToken);
        var contactPageCt = await CreateContactPage(textboxDataType, cancellationToken);
        var eventPageCt = await CreateEventPage(textboxDataType, descDataType, dateTimeDataType, mediaPickerDataType, cancellationToken);
        var recipePageCt = await CreateRecipePage(textboxDataType, descDataType, mediaPickerDataType, ingredientsBlockList, instructionsBlockList, cancellationToken);

        // 3a. Create new content types
        var homePageCt = await CreateContentType("homePage", "Home Page", "icon-home", new[]
        {
            ("siteName", "Site Name", textboxDataType),
            ("siteDescription", "Site Description", descDataType),
            ("organisationName", "Organisation Name", textboxDataType),
            ("organisationEmail", "Organisation Email", textboxDataType),
            ("organisationTelephone", "Organisation Telephone", textboxDataType),
            ("sameAs", "Social Links", descDataType),
            // BlockGrid showcase on the home page — features + quote blocks that demonstrate
            // Schema.org WebSite.mainEntity resolution plus nested ImageObject ranking.
            ("contentGrid", "Content Grid", contentGridDataType),
        }, cancellationToken);

        // Make homePage culture-variant (siteName + siteDescription vary by culture)
        homePageCt.Variations = ContentVariation.Culture;
        foreach (var prop in homePageCt.PropertyTypes.Where(p => p.Alias is "siteName" or "siteDescription"))
            prop.Variations = ContentVariation.Culture;
        await _contentTypeService.UpdateAsync(homePageCt, Constants.Security.SuperUserKey);

        var aboutPageCt = await CreateContentType("aboutPage", "About Page", "icon-info", new[]
        {
            ("title", "Title", textboxDataType),
            ("description", "Description", descDataType),
            ("bodyText", "Body Text", bodyDataType),
            ("organisationName", "Organisation Name", textboxDataType),
            ("foundingDate", "Founding Date", textboxDataType),
            ("numberOfEmployees", "Number of Employees", textboxDataType),
        }, cancellationToken);

        // Make aboutPage culture-variant (title + bodyText vary by culture)
        aboutPageCt.Variations = ContentVariation.Culture;
        foreach (var prop in aboutPageCt.PropertyTypes.Where(p => p.Alias is "title" or "bodyText"))
            prop.Variations = ContentVariation.Culture;
        await _contentTypeService.UpdateAsync(aboutPageCt, Constants.Security.SuperUserKey);

        var listingProps = new[]
        {
            ("title", "Title", textboxDataType),
            ("description", "Description", descDataType),
        };
        var categoriesListingCt = await CreateContentType("categoriesListing", "Categories Listing", "icon-categories", listingProps, cancellationToken);
        var blogListingCt = await CreateContentType("blogListing", "Blog Listing", "icon-thumbnails-small", listingProps, cancellationToken);
        var productListingCt = await CreateContentType("productListing", "Product Listing", "icon-thumbnails-small", listingProps, cancellationToken);
        var eventListingCt = await CreateContentType("eventListing", "Event Listing", "icon-thumbnails-small", listingProps, cancellationToken);
        var recipeListingCt = await CreateContentType("recipeListing", "Recipe Listing", "icon-thumbnails-small", listingProps, cancellationToken);

        var newsArticleCt = await CreateContentType("newsArticle", "News Article", "icon-newspaper-alt", new[]
        {
            ("title", "Title", textboxDataType),
            ("description", "Description", descDataType),
            ("bodyText", "Body Text", bodyDataType),
            ("authorName", "Author Name", textboxDataType),
            ("publishDate", "Publish Date", dateTimeDataType ?? textboxDataType),
            ("keywords", "Keywords", textboxDataType),
            ("dateline", "Dateline", textboxDataType),
        }, cancellationToken);

        var techArticleCt = await CreateContentType("techArticle", "Tech Article", "icon-code", new[]
        {
            ("title", "Title", textboxDataType),
            ("description", "Description", descDataType),
            ("bodyText", "Body Text", bodyDataType),
            ("authorName", "Author Name", textboxDataType),
            ("publishDate", "Publish Date", dateTimeDataType ?? textboxDataType),
            ("proficiencyLevel", "Proficiency Level", textboxDataType),
            ("dependencies", "Dependencies", textboxDataType),
        }, cancellationToken);

        var softwarePageCt = await CreateContentType("softwarePage", "Software Page", "icon-application-window-alt", new[]
        {
            ("title", "Title", textboxDataType),
            ("description", "Description", descDataType),
            ("bodyText", "Body Text", bodyDataType),
            ("applicationCategory", "Application Category", textboxDataType),
            ("operatingSystem", "Operating System", textboxDataType),
            ("softwareVersion", "Software Version", textboxDataType),
            ("downloadUrl", "Download URL", textboxDataType),
            ("price", "Price", textboxDataType),
            ("currency", "Currency", textboxDataType),
        }, cancellationToken);

        var coursePageCt = await CreateContentType("coursePage", "Course Page", "icon-mortarboard", new[]
        {
            ("title", "Title", textboxDataType),
            ("description", "Description", descDataType),
            ("bodyText", "Body Text", bodyDataType),
            ("courseCode", "Course Code", textboxDataType),
            ("providerName", "Provider Name", textboxDataType),
            ("duration", "Duration", textboxDataType),
            ("price", "Price", textboxDataType),
            ("currency", "Currency", textboxDataType),
            ("startDate", "Start Date", dateTimeDataType ?? textboxDataType),
        }, cancellationToken);

        var howToPageCt = await CreateContentType("howToPage", "How-To Page", "icon-handprint", new[]
        {
            ("title", "Title", textboxDataType),
            ("description", "Description", descDataType),
            ("bodyText", "Body Text", bodyDataType),
            ("totalTime", "Total Time", textboxDataType),
            ("estimatedCost", "Estimated Cost", textboxDataType),
        }, cancellationToken, ("howToSteps", "How-To Steps", howToStepsBlockList), ("howToTools", "How-To Tools", howToToolsBlockList));

        var videoPageCt = await CreateContentType("videoPage", "Video Page", "icon-video", new[]
        {
            ("title", "Title", textboxDataType),
            ("description", "Description", descDataType),
            ("thumbnailUrl", "Thumbnail URL", textboxDataType),
            ("uploadDate", "Upload Date", dateTimeDataType ?? textboxDataType),
            ("duration", "Duration", textboxDataType),
            ("contentUrl", "Content URL", textboxDataType),
            ("embedUrl", "Embed URL", textboxDataType),
        }, cancellationToken);

        var jobPostingPageCt = await CreateContentType("jobPostingPage", "Job Posting", "icon-users-alt", new[]
        {
            ("title", "Title", textboxDataType),
            ("description", "Description", descDataType),
            ("bodyText", "Body Text", bodyDataType),
            ("datePosted", "Date Posted", dateTimeDataType ?? textboxDataType),
            ("validThrough", "Valid Through", dateTimeDataType ?? textboxDataType),
            ("employmentType", "Employment Type", textboxDataType),
            ("hiringOrganisation", "Hiring Organisation", textboxDataType),
            ("salary", "Salary", textboxDataType),
            ("jobLocationName", "Job Location Name", textboxDataType),
            ("jobLocationAddress", "Job Location Address", textboxDataType),
            ("qualifications", "Qualifications", descDataType),
        }, cancellationToken);

        var profilePageCt = await CreateContentType("profilePage", "Profile Page", "icon-user", new[]
        {
            ("title", "Title", textboxDataType),
            ("description", "Description", descDataType),
            ("givenName", "Given Name", textboxDataType),
            ("familyName", "Family Name", textboxDataType),
            ("jobTitle", "Job Title", textboxDataType),
            ("email", "Email", textboxDataType),
            ("worksFor", "Works For", textboxDataType),
            ("sameAs", "Social Links", descDataType),
        }, cancellationToken);

        var locationPageCt = await CreateContentType("locationPage", "Location Page", "icon-pin-location", new[]
        {
            ("title", "Title", textboxDataType),
            ("description", "Description", descDataType),
            ("telephone", "Telephone", textboxDataType),
            ("email", "Email", textboxDataType),
            ("streetAddress", "Street Address", textboxDataType),
            ("addressLocality", "Address Locality", textboxDataType),
            ("addressRegion", "Address Region", textboxDataType),
            ("postalCode", "Postal Code", textboxDataType),
            ("addressCountry", "Address Country", textboxDataType),
            ("latitude", "Latitude", textboxDataType),
            ("longitude", "Longitude", textboxDataType),
            ("priceRange", "Price Range", textboxDataType),
        }, cancellationToken, ("openingHours", "Opening Hours", openingHoursBlockList));

        var restaurantPageCt = await CreateContentType("restaurantPage", "Restaurant Page", "icon-utensils", new[]
        {
            ("title", "Title", textboxDataType),
            ("description", "Description", descDataType),
            ("telephone", "Telephone", textboxDataType),
            ("email", "Email", textboxDataType),
            ("streetAddress", "Street Address", textboxDataType),
            ("addressLocality", "Address Locality", textboxDataType),
            ("postalCode", "Postal Code", textboxDataType),
            ("addressCountry", "Address Country", textboxDataType),
            ("latitude", "Latitude", textboxDataType),
            ("longitude", "Longitude", textboxDataType),
            ("priceRange", "Price Range", textboxDataType),
            ("servesCuisine", "Serves Cuisine", textboxDataType),
            ("menu", "Menu URL", textboxDataType),
            ("acceptsReservations", "Accepts Reservations", textboxDataType),
        }, cancellationToken, ("openingHours", "Opening Hours", openingHoursBlockList));

        var bookPageCt = await CreateContentType("bookPage", "Book Page", "icon-book-alt", new[]
        {
            ("title", "Title", textboxDataType),
            ("description", "Description", descDataType),
            ("bodyText", "Body Text", bodyDataType),
            ("authorName", "Author Name", textboxDataType),
            ("isbn", "ISBN", textboxDataType),
            ("bookFormat", "Book Format", textboxDataType),
            ("numberOfPages", "Number of Pages", textboxDataType),
            ("publisher", "Publisher", textboxDataType),
            ("datePublished", "Date Published", dateTimeDataType ?? textboxDataType),
            ("price", "Price", textboxDataType),
            ("currency", "Currency", textboxDataType),
        }, cancellationToken);

        // 3c. New subtype content types — Products
        var vehiclePageCt = await CreateContentType("vehiclePage", "Vehicle Page", "icon-truck", new[]
        {
            ("title", "Title", textboxDataType),
            ("description", "Description", descDataType),
            ("brand", "Brand", textboxDataType),
            ("model", "Model", textboxDataType),
            ("fuelType", "Fuel Type", textboxDataType),
            ("mileageFromOdometer", "Mileage", textboxDataType),
            ("vehicleEngine", "Engine", textboxDataType),
            ("color", "Colour", textboxDataType),
            ("numberOfDoors", "Number of Doors", textboxDataType),
            ("bodyText", "Body Text", bodyDataType),
        }, cancellationToken);

        var financialProductPageCt = await CreateContentType("financialProductPage", "Financial Product Page", "icon-coins-alt", new[]
        {
            ("title", "Title", textboxDataType),
            ("description", "Description", descDataType),
            ("feesAndCommissionsSpecification", "Fees and Commissions", descDataType),
            ("interestRate", "Interest Rate", textboxDataType),
            ("annualPercentageRate", "Annual Percentage Rate", textboxDataType),
            ("bodyText", "Body Text", bodyDataType),
        }, cancellationToken);

        var individualProductPageCt = await CreateContentType("individualProductPage", "Individual Product Page", "icon-tag", new[]
        {
            ("title", "Title", textboxDataType),
            ("description", "Description", descDataType),
            ("serialNumber", "Serial Number", textboxDataType),
            ("sku", "SKU", textboxDataType),
            ("color", "Colour", textboxDataType),
            ("weight", "Weight", textboxDataType),
            ("bodyText", "Body Text", bodyDataType),
        }, cancellationToken);

        var productModelPageCt = await CreateContentType("productModelPage", "Product Model Page", "icon-box-open", new[]
        {
            ("title", "Title", textboxDataType),
            ("description", "Description", descDataType),
            ("bodyText", "Body Text", bodyDataType),
        }, cancellationToken);

        // 3c. New subtype content types — Events
        var musicEventPageCt = await CreateContentType("musicEventPage", "Music Event Page", "icon-music", new[]
        {
            ("title", "Title", textboxDataType),
            ("description", "Description", descDataType),
            ("performer", "Performer", textboxDataType),
            ("startDate", "Start Date", dateTimeDataType ?? textboxDataType),
            ("endDate", "End Date", dateTimeDataType ?? textboxDataType),
            ("locationName", "Location Name", textboxDataType),
            ("locationAddress", "Location Address", textboxDataType),
        }, cancellationToken);

        var sportsEventPageCt = await CreateContentType("sportsEventPage", "Sports Event Page", "icon-trophy", new[]
        {
            ("title", "Title", textboxDataType),
            ("description", "Description", descDataType),
            ("competitor", "Competitor", textboxDataType),
            ("startDate", "Start Date", dateTimeDataType ?? textboxDataType),
            ("locationName", "Location Name", textboxDataType),
            ("sport", "Sport", textboxDataType),
        }, cancellationToken);

        var businessEventPageCt = await CreateContentType("businessEventPage", "Business Event Page", "icon-briefcase", new[]
        {
            ("title", "Title", textboxDataType),
            ("description", "Description", descDataType),
            ("organiserName", "Organiser Name", textboxDataType),
            ("startDate", "Start Date", dateTimeDataType ?? textboxDataType),
            ("locationName", "Location Name", textboxDataType),
            ("sponsor", "Sponsor", textboxDataType),
        }, cancellationToken);

        var foodEventPageCt = await CreateContentType("foodEventPage", "Food Event Page", "icon-food", new[]
        {
            ("title", "Title", textboxDataType),
            ("description", "Description", descDataType),
            ("startDate", "Start Date", dateTimeDataType ?? textboxDataType),
            ("locationName", "Location Name", textboxDataType),
        }, cancellationToken);

        var festivalPageCt = await CreateContentType("festivalPage", "Festival Page", "icon-hearts", new[]
        {
            ("title", "Title", textboxDataType),
            ("description", "Description", descDataType),
            ("startDate", "Start Date", dateTimeDataType ?? textboxDataType),
            ("endDate", "End Date", dateTimeDataType ?? textboxDataType),
            ("locationName", "Location Name", textboxDataType),
        }, cancellationToken);

        var educationEventPageCt = await CreateContentType("educationEventPage", "Education Event Page", "icon-mortarboard", new[]
        {
            ("title", "Title", textboxDataType),
            ("description", "Description", descDataType),
            ("startDate", "Start Date", dateTimeDataType ?? textboxDataType),
            ("locationName", "Location Name", textboxDataType),
        }, cancellationToken);

        // 3c. New subtype content types — Organisations
        var organisationListingCt = await CreateContentType("organisationListing", "Organisation Listing", "icon-thumbnails-small", listingProps, cancellationToken);

        var corporationPageCt = await CreateContentType("corporationPage", "Corporation Page", "icon-company", new[]
        {
            ("title", "Title", textboxDataType),
            ("description", "Description", descDataType),
            ("tickerSymbol", "Ticker Symbol", textboxDataType),
            ("legalName", "Legal Name", textboxDataType),
            ("foundingDate", "Founding Date", textboxDataType),
            ("numberOfEmployees", "Number of Employees", textboxDataType),
        }, cancellationToken);

        var sportsTeamPageCt = await CreateContentType("sportsTeamPage", "Sports Team Page", "icon-trophy", new[]
        {
            ("title", "Title", textboxDataType),
            ("description", "Description", descDataType),
            ("sport", "Sport", textboxDataType),
            ("coach", "Coach", textboxDataType),
        }, cancellationToken);

        var airlinePageCt = await CreateContentType("airlinePage", "Airline Page", "icon-plane", new[]
        {
            ("title", "Title", textboxDataType),
            ("description", "Description", descDataType),
            ("iataCode", "IATA Code", textboxDataType),
            ("foundingDate", "Founding Date", textboxDataType),
        }, cancellationToken);

        var ngoPageCt = await CreateContentType("ngoPage", "NGO Page", "icon-hearts", new[]
        {
            ("title", "Title", textboxDataType),
            ("description", "Description", descDataType),
            ("foundingDate", "Founding Date", textboxDataType),
            ("areaServed", "Area Served", textboxDataType),
        }, cancellationToken);

        // 3c. New subtype content types — Places
        var placesListingCt = await CreateContentType("placesListing", "Places Listing", "icon-thumbnails-small", listingProps, cancellationToken);

        var hotelPageCt = await CreateContentType("hotelPage", "Hotel Page", "icon-hotel", new[]
        {
            ("title", "Title", textboxDataType),
            ("description", "Description", descDataType),
            ("telephone", "Telephone", textboxDataType),
            ("streetAddress", "Street Address", textboxDataType),
            ("addressLocality", "Address Locality", textboxDataType),
            ("postalCode", "Postal Code", textboxDataType),
            ("starRating", "Star Rating", textboxDataType),
            ("checkinTime", "Check-in Time", textboxDataType),
            ("checkoutTime", "Check-out Time", textboxDataType),
        }, cancellationToken);

        var storePageCt = await CreateContentType("storePage", "Store Page", "icon-store", new[]
        {
            ("title", "Title", textboxDataType),
            ("description", "Description", descDataType),
            ("telephone", "Telephone", textboxDataType),
            ("streetAddress", "Street Address", textboxDataType),
            ("addressLocality", "Address Locality", textboxDataType),
            ("postalCode", "Postal Code", textboxDataType),
            ("paymentAccepted", "Payment Accepted", textboxDataType),
        }, cancellationToken);

        var hospitalPageCt = await CreateContentType("hospitalPage", "Hospital Page", "icon-medical", new[]
        {
            ("title", "Title", textboxDataType),
            ("description", "Description", descDataType),
            ("telephone", "Telephone", textboxDataType),
            ("streetAddress", "Street Address", textboxDataType),
            ("addressLocality", "Address Locality", textboxDataType),
            ("medicalSpecialty", "Medical Specialty", textboxDataType),
        }, cancellationToken);

        var gymPageCt = await CreateContentType("gymPage", "Gym Page", "icon-running", new[]
        {
            ("title", "Title", textboxDataType),
            ("description", "Description", descDataType),
            ("telephone", "Telephone", textboxDataType),
            ("streetAddress", "Street Address", textboxDataType),
            ("addressLocality", "Address Locality", textboxDataType),
            ("openingHours", "Opening Hours", textboxDataType),
        }, cancellationToken);

        // 3c. New subtype content types — Creative
        var creativeListingCt = await CreateContentType("creativeListing", "Creative Listing", "icon-thumbnails-small", listingProps, cancellationToken);

        var moviePageCt = await CreateContentType("moviePage", "Movie Page", "icon-movie-alt", new[]
        {
            ("title", "Title", textboxDataType),
            ("description", "Description", descDataType),
            ("director", "Director", textboxDataType),
            ("actor", "Actor", textboxDataType),
            ("duration", "Duration", textboxDataType),
            ("dateCreated", "Date Created", dateTimeDataType ?? textboxDataType),
        }, cancellationToken);

        var musicAlbumPageCt = await CreateContentType("musicAlbumPage", "Music Album Page", "icon-music", new[]
        {
            ("title", "Title", textboxDataType),
            ("description", "Description", descDataType),
            ("byArtist", "By Artist", textboxDataType),
            ("numTracks", "Number of Tracks", textboxDataType),
            ("datePublished", "Date Published", dateTimeDataType ?? textboxDataType),
        }, cancellationToken);

        var podcastEpisodePageCt = await CreateContentType("podcastEpisodePage", "Podcast Episode Page", "icon-microphone", new[]
        {
            ("title", "Title", textboxDataType),
            ("description", "Description", descDataType),
            ("duration", "Duration", textboxDataType),
            ("datePublished", "Date Published", dateTimeDataType ?? textboxDataType),
        }, cancellationToken);

        var photographPageCt = await CreateContentType("photographPage", "Photograph Page", "icon-picture", new[]
        {
            ("title", "Title", textboxDataType),
            ("description", "Description", descDataType),
            ("creator", "Creator", textboxDataType),
            ("dateCreated", "Date Created", dateTimeDataType ?? textboxDataType),
            ("contentUrl", "Content URL", textboxDataType),
        }, cancellationToken);

        var datasetPageCt = await CreateContentType("datasetPage", "Dataset Page", "icon-chart-curve", new[]
        {
            ("title", "Title", textboxDataType),
            ("description", "Description", descDataType),
            ("distribution", "Distribution", textboxDataType),
            ("license", "Licence", textboxDataType),
        }, cancellationToken);

        // 3c. New subtype content types — Standalone
        var mobileAppPageCt = await CreateContentType("mobileAppPage", "Mobile App Page", "icon-iphone", new[]
        {
            ("title", "Title", textboxDataType),
            ("description", "Description", descDataType),
            ("operatingSystem", "Operating System", textboxDataType),
            ("applicationCategory", "Application Category", textboxDataType),
            ("downloadUrl", "Download URL", textboxDataType),
            ("softwareVersion", "Software Version", textboxDataType),
        }, cancellationToken);

        var webAppPageCt = await CreateContentType("webAppPage", "Web App Page", "icon-globe", new[]
        {
            ("title", "Title", textboxDataType),
            ("description", "Description", descDataType),
            ("browserRequirements", "Browser Requirements", textboxDataType),
            ("applicationCategory", "Application Category", textboxDataType),
            ("url", "URL", textboxDataType),
        }, cancellationToken);

        // 3c. New subtype content types — Shops
        var shopsListingCt = await CreateContentType("shopsListing", "Shops Listing", "icon-thumbnails-small", listingProps, cancellationToken);

        var bookStorePageCt = await CreateContentType("bookStorePage", "Book Store Page", "icon-book-alt", new[]
        {
            ("title", "Title", textboxDataType),
            ("description", "Description", descDataType),
            ("telephone", "Telephone", textboxDataType),
            ("streetAddress", "Street Address", textboxDataType),
            ("addressLocality", "Address Locality", textboxDataType),
            ("postalCode", "Postal Code", textboxDataType),
        }, cancellationToken);

        var electronicsStorePageCt = await CreateContentType("electronicsStorePage", "Electronics Store Page", "icon-flash", new[]
        {
            ("title", "Title", textboxDataType),
            ("description", "Description", descDataType),
            ("telephone", "Telephone", textboxDataType),
            ("streetAddress", "Street Address", textboxDataType),
            ("addressLocality", "Address Locality", textboxDataType),
            ("postalCode", "Postal Code", textboxDataType),
        }, cancellationToken);

        var clothingStorePageCt = await CreateContentType("clothingStorePage", "Clothing Store Page", "icon-clothes-hanger", new[]
        {
            ("title", "Title", textboxDataType),
            ("description", "Description", descDataType),
            ("telephone", "Telephone", textboxDataType),
            ("streetAddress", "Street Address", textboxDataType),
            ("addressLocality", "Address Locality", textboxDataType),
            ("postalCode", "Postal Code", textboxDataType),
        }, cancellationToken);

        var groceryStorePageCt = await CreateContentType("groceryStorePage", "Grocery Store Page", "icon-shopping-basket-alt-2", new[]
        {
            ("title", "Title", textboxDataType),
            ("description", "Description", descDataType),
            ("telephone", "Telephone", textboxDataType),
            ("streetAddress", "Street Address", textboxDataType),
            ("addressLocality", "Address Locality", textboxDataType),
            ("postalCode", "Postal Code", textboxDataType),
        }, cancellationToken);

        var furnitureStorePageCt = await CreateContentType("furnitureStorePage", "Furniture Store Page", "icon-layout", new[]
        {
            ("title", "Title", textboxDataType),
            ("description", "Description", descDataType),
            ("telephone", "Telephone", textboxDataType),
            ("streetAddress", "Street Address", textboxDataType),
            ("addressLocality", "Address Locality", textboxDataType),
            ("postalCode", "Postal Code", textboxDataType),
        }, cancellationToken);

        var jewelryStorePageCt = await CreateContentType("jewelryStorePage", "Jewelry Store Page", "icon-medal", new[]
        {
            ("title", "Title", textboxDataType),
            ("description", "Description", descDataType),
            ("telephone", "Telephone", textboxDataType),
            ("streetAddress", "Street Address", textboxDataType),
            ("addressLocality", "Address Locality", textboxDataType),
            ("postalCode", "Postal Code", textboxDataType),
        }, cancellationToken);

        // 3c. New subtype content types — Dining
        var diningListingCt = await CreateContentType("diningListing", "Dining Listing", "icon-thumbnails-small", listingProps, cancellationToken);

        var bakeryPageCt = await CreateContentType("bakeryPage", "Bakery Page", "icon-food", new[]
        {
            ("title", "Title", textboxDataType),
            ("description", "Description", descDataType),
            ("telephone", "Telephone", textboxDataType),
            ("streetAddress", "Street Address", textboxDataType),
            ("addressLocality", "Address Locality", textboxDataType),
            ("servesCuisine", "Serves Cuisine", textboxDataType),
        }, cancellationToken);

        var cafePageCt = await CreateContentType("cafePage", "Cafe Page", "icon-mug", new[]
        {
            ("title", "Title", textboxDataType),
            ("description", "Description", descDataType),
            ("telephone", "Telephone", textboxDataType),
            ("streetAddress", "Street Address", textboxDataType),
            ("addressLocality", "Address Locality", textboxDataType),
        }, cancellationToken);

        var barPageCt = await CreateContentType("barPage", "Bar Page", "icon-wine-glass", new[]
        {
            ("title", "Title", textboxDataType),
            ("description", "Description", descDataType),
            ("telephone", "Telephone", textboxDataType),
            ("streetAddress", "Street Address", textboxDataType),
            ("addressLocality", "Address Locality", textboxDataType),
        }, cancellationToken);

        var fastFoodPageCt = await CreateContentType("fastFoodPage", "Fast Food Page", "icon-food", new[]
        {
            ("title", "Title", textboxDataType),
            ("description", "Description", descDataType),
            ("telephone", "Telephone", textboxDataType),
            ("streetAddress", "Street Address", textboxDataType),
            ("servesCuisine", "Serves Cuisine", textboxDataType),
        }, cancellationToken);

        var wineryPageCt = await CreateContentType("wineryPage", "Winery Page", "icon-wine-glass", new[]
        {
            ("title", "Title", textboxDataType),
            ("description", "Description", descDataType),
            ("telephone", "Telephone", textboxDataType),
            ("streetAddress", "Street Address", textboxDataType),
            ("addressLocality", "Address Locality", textboxDataType),
        }, cancellationToken);

        var breweryPageCt = await CreateContentType("breweryPage", "Brewery Page", "icon-wine-glass", new[]
        {
            ("title", "Title", textboxDataType),
            ("description", "Description", descDataType),
            ("telephone", "Telephone", textboxDataType),
            ("streetAddress", "Street Address", textboxDataType),
            ("addressLocality", "Address Locality", textboxDataType),
        }, cancellationToken);

        // 3c. New subtype content types — Travel
        var travelListingCt = await CreateContentType("travelListing", "Travel Listing", "icon-thumbnails-small", listingProps, cancellationToken);

        var flightReservationPageCt = await CreateContentType("flightReservationPage", "Flight Reservation Page", "icon-plane", new[]
        {
            ("title", "Title", textboxDataType),
            ("description", "Description", descDataType),
            ("reservationId", "Reservation ID", textboxDataType),
            ("flightNumber", "Flight Number", textboxDataType),
            ("departureAirport", "Departure Airport", textboxDataType),
            ("arrivalAirport", "Arrival Airport", textboxDataType),
            ("departureTime", "Departure Time", dateTimeDataType ?? textboxDataType),
        }, cancellationToken);

        var lodgingReservationPageCt = await CreateContentType("lodgingReservationPage", "Lodging Reservation Page", "icon-hotel", new[]
        {
            ("title", "Title", textboxDataType),
            ("description", "Description", descDataType),
            ("reservationId", "Reservation ID", textboxDataType),
            ("checkinTime", "Check-in Time", dateTimeDataType ?? textboxDataType),
            ("checkoutTime", "Check-out Time", dateTimeDataType ?? textboxDataType),
            ("lodgingUnitDescription", "Lodging Unit Description", textboxDataType),
        }, cancellationToken);

        var eventReservationPageCt = await CreateContentType("eventReservationPage", "Event Reservation Page", "icon-calendar", new[]
        {
            ("title", "Title", textboxDataType),
            ("description", "Description", descDataType),
            ("reservationId", "Reservation ID", textboxDataType),
            ("reservationFor", "Reservation For", textboxDataType),
        }, cancellationToken);

        var rentalCarPageCt = await CreateContentType("rentalCarPage", "Rental Car Page", "icon-truck", new[]
        {
            ("title", "Title", textboxDataType),
            ("description", "Description", descDataType),
            ("reservationId", "Reservation ID", textboxDataType),
            ("pickupLocation", "Pickup Location", textboxDataType),
            ("pickupTime", "Pickup Time", dateTimeDataType ?? textboxDataType),
        }, cancellationToken);

        var flightPageCt = await CreateContentType("flightPage", "Flight Page", "icon-plane", new[]
        {
            ("title", "Title", textboxDataType),
            ("description", "Description", descDataType),
            ("flightNumber", "Flight Number", textboxDataType),
            ("departureAirport", "Departure Airport", textboxDataType),
            ("arrivalAirport", "Arrival Airport", textboxDataType),
            ("departureTime", "Departure Time", dateTimeDataType ?? textboxDataType),
            ("arrivalTime", "Arrival Time", dateTimeDataType ?? textboxDataType),
        }, cancellationToken);

        var touristAttractionPageCt = await CreateContentType("touristAttractionPage", "Tourist Attraction Page", "icon-map-marker", new[]
        {
            ("title", "Title", textboxDataType),
            ("description", "Description", descDataType),
            ("streetAddress", "Street Address", textboxDataType),
            ("addressLocality", "Address Locality", textboxDataType),
            ("telephone", "Telephone", textboxDataType),
        }, cancellationToken);

        var bedAndBreakfastPageCt = await CreateContentType("bedAndBreakfastPage", "Bed and Breakfast Page", "icon-hotel", new[]
        {
            ("title", "Title", textboxDataType),
            ("description", "Description", descDataType),
            ("telephone", "Telephone", textboxDataType),
            ("streetAddress", "Street Address", textboxDataType),
            ("addressLocality", "Address Locality", textboxDataType),
            ("starRating", "Star Rating", textboxDataType),
        }, cancellationToken);

        var campgroundPageCt = await CreateContentType("campgroundPage", "Campground Page", "icon-tree", new[]
        {
            ("title", "Title", textboxDataType),
            ("description", "Description", descDataType),
            ("telephone", "Telephone", textboxDataType),
            ("streetAddress", "Street Address", textboxDataType),
            ("addressLocality", "Address Locality", textboxDataType),
        }, cancellationToken);

        // 3c. New subtype content types — Healthcare
        var healthcareListingCt = await CreateContentType("healthcareListing", "Healthcare Listing", "icon-thumbnails-small", listingProps, cancellationToken);

        var drugPageCt = await CreateContentType("drugPage", "Drug Page", "icon-pill", new[]
        {
            ("title", "Title", textboxDataType),
            ("description", "Description", descDataType),
            ("activeIngredient", "Active Ingredient", textboxDataType),
            ("dosageForm", "Dosage Form", textboxDataType),
            ("administrationRoute", "Administration Route", textboxDataType),
            ("prescriptionStatus", "Prescription Status", textboxDataType),
        }, cancellationToken);

        var medicalConditionPageCt = await CreateContentType("medicalConditionPage", "Medical Condition Page", "icon-medical", new[]
        {
            ("title", "Title", textboxDataType),
            ("description", "Description", descDataType),
            ("possibleTreatment", "Possible Treatment", textboxDataType),
            ("riskFactor", "Risk Factor", textboxDataType),
            ("signOrSymptom", "Sign or Symptom", textboxDataType),
        }, cancellationToken);

        var physicianPageCt = await CreateContentType("physicianPage", "Physician Page", "icon-user", new[]
        {
            ("title", "Title", textboxDataType),
            ("description", "Description", descDataType),
            ("telephone", "Telephone", textboxDataType),
            ("streetAddress", "Street Address", textboxDataType),
            ("addressLocality", "Address Locality", textboxDataType),
            ("medicalSpecialty", "Medical Specialty", textboxDataType),
        }, cancellationToken);

        var pharmacyPageCt = await CreateContentType("pharmacyPage", "Pharmacy Page", "icon-pill", new[]
        {
            ("title", "Title", textboxDataType),
            ("description", "Description", descDataType),
            ("telephone", "Telephone", textboxDataType),
            ("streetAddress", "Street Address", textboxDataType),
            ("addressLocality", "Address Locality", textboxDataType),
        }, cancellationToken);

        var dentistPageCt = await CreateContentType("dentistPage", "Dentist Page", "icon-smiley", new[]
        {
            ("title", "Title", textboxDataType),
            ("description", "Description", descDataType),
            ("telephone", "Telephone", textboxDataType),
            ("streetAddress", "Street Address", textboxDataType),
            ("addressLocality", "Address Locality", textboxDataType),
        }, cancellationToken);

        var medicalClinicPageCt = await CreateContentType("medicalClinicPage", "Medical Clinic Page", "icon-medical", new[]
        {
            ("title", "Title", textboxDataType),
            ("description", "Description", descDataType),
            ("telephone", "Telephone", textboxDataType),
            ("streetAddress", "Street Address", textboxDataType),
            ("addressLocality", "Address Locality", textboxDataType),
            ("medicalSpecialty", "Medical Specialty", textboxDataType),
        }, cancellationToken);

        // 3c. New subtype content types — Automotive
        var automotiveListingCt = await CreateContentType("automotiveListing", "Automotive Listing", "icon-thumbnails-small", listingProps, cancellationToken);

        var carPageCt = await CreateContentType("carPage", "Car Page", "icon-truck", new[]
        {
            ("title", "Title", textboxDataType),
            ("description", "Description", descDataType),
            ("brand", "Brand", textboxDataType),
            ("model", "Model", textboxDataType),
            ("fuelType", "Fuel Type", textboxDataType),
            ("mileageFromOdometer", "Mileage", textboxDataType),
            ("color", "Colour", textboxDataType),
            ("numberOfDoors", "Number of Doors", textboxDataType),
            ("vehicleTransmission", "Vehicle Transmission", textboxDataType),
        }, cancellationToken);

        var motorcyclePageCt = await CreateContentType("motorcyclePage", "Motorcycle Page", "icon-truck", new[]
        {
            ("title", "Title", textboxDataType),
            ("description", "Description", descDataType),
            ("brand", "Brand", textboxDataType),
            ("model", "Model", textboxDataType),
            ("fuelType", "Fuel Type", textboxDataType),
            ("mileageFromOdometer", "Mileage", textboxDataType),
            ("color", "Colour", textboxDataType),
        }, cancellationToken);

        var autoDealerPageCt = await CreateContentType("autoDealerPage", "Auto Dealer Page", "icon-store", new[]
        {
            ("title", "Title", textboxDataType),
            ("description", "Description", descDataType),
            ("telephone", "Telephone", textboxDataType),
            ("streetAddress", "Street Address", textboxDataType),
            ("addressLocality", "Address Locality", textboxDataType),
        }, cancellationToken);

        var autoRepairPageCt = await CreateContentType("autoRepairPage", "Auto Repair Page", "icon-wrench", new[]
        {
            ("title", "Title", textboxDataType),
            ("description", "Description", descDataType),
            ("telephone", "Telephone", textboxDataType),
            ("streetAddress", "Street Address", textboxDataType),
            ("addressLocality", "Address Locality", textboxDataType),
        }, cancellationToken);

        // 3c. New subtype content types — Events (additional)
        var theaterEventPageCt = await CreateContentType("theaterEventPage", "Theater Event Page", "icon-movie-alt", new[]
        {
            ("title", "Title", textboxDataType),
            ("description", "Description", descDataType),
            ("performer", "Performer", textboxDataType),
            ("startDate", "Start Date", dateTimeDataType ?? textboxDataType),
            ("locationName", "Location Name", textboxDataType),
        }, cancellationToken);

        var screeningEventPageCt = await CreateContentType("screeningEventPage", "Screening Event Page", "icon-movie-alt", new[]
        {
            ("title", "Title", textboxDataType),
            ("description", "Description", descDataType),
            ("startDate", "Start Date", dateTimeDataType ?? textboxDataType),
            ("locationName", "Location Name", textboxDataType),
            ("workPresented", "Work Presented", textboxDataType),
        }, cancellationToken);

        // 3c. New subtype content types — Entertainment
        var entertainmentListingCt = await CreateContentType("entertainmentListing", "Entertainment Listing", "icon-thumbnails-small", listingProps, cancellationToken);

        var musicGroupPageCt = await CreateContentType("musicGroupPage", "Music Group Page", "icon-music", new[]
        {
            ("title", "Title", textboxDataType),
            ("description", "Description", descDataType),
            ("genre", "Genre", textboxDataType),
            ("foundingDate", "Founding Date", textboxDataType),
        }, cancellationToken);

        var zooPageCt = await CreateContentType("zooPage", "Zoo Page", "icon-tree", new[]
        {
            ("title", "Title", textboxDataType),
            ("description", "Description", descDataType),
            ("telephone", "Telephone", textboxDataType),
            ("streetAddress", "Street Address", textboxDataType),
            ("addressLocality", "Address Locality", textboxDataType),
        }, cancellationToken);

        var museumPageCt = await CreateContentType("museumPage", "Museum Page", "icon-picture", new[]
        {
            ("title", "Title", textboxDataType),
            ("description", "Description", descDataType),
            ("telephone", "Telephone", textboxDataType),
            ("streetAddress", "Street Address", textboxDataType),
            ("addressLocality", "Address Locality", textboxDataType),
        }, cancellationToken);

        var amusementParkPageCt = await CreateContentType("amusementParkPage", "Amusement Park Page", "icon-hearts", new[]
        {
            ("title", "Title", textboxDataType),
            ("description", "Description", descDataType),
            ("telephone", "Telephone", textboxDataType),
            ("streetAddress", "Street Address", textboxDataType),
            ("addressLocality", "Address Locality", textboxDataType),
        }, cancellationToken);

        // 3c. New subtype content types — Services
        var servicesListingCt = await CreateContentType("servicesListing", "Services Listing", "icon-thumbnails-small", listingProps, cancellationToken);

        var attorneyPageCt = await CreateContentType("attorneyPage", "Attorney Page", "icon-gavel", new[]
        {
            ("title", "Title", textboxDataType),
            ("description", "Description", descDataType),
            ("telephone", "Telephone", textboxDataType),
            ("streetAddress", "Street Address", textboxDataType),
            ("addressLocality", "Address Locality", textboxDataType),
        }, cancellationToken);

        var accountingPageCt = await CreateContentType("accountingPage", "Accounting Page", "icon-coins-alt", new[]
        {
            ("title", "Title", textboxDataType),
            ("description", "Description", descDataType),
            ("telephone", "Telephone", textboxDataType),
            ("streetAddress", "Street Address", textboxDataType),
            ("addressLocality", "Address Locality", textboxDataType),
        }, cancellationToken);

        var realEstatePageCt = await CreateContentType("realEstatePage", "Real Estate Page", "icon-home", new[]
        {
            ("title", "Title", textboxDataType),
            ("description", "Description", descDataType),
            ("telephone", "Telephone", textboxDataType),
            ("streetAddress", "Street Address", textboxDataType),
            ("addressLocality", "Address Locality", textboxDataType),
        }, cancellationToken);

        var insurancePageCt = await CreateContentType("insurancePage", "Insurance Page", "icon-shield", new[]
        {
            ("title", "Title", textboxDataType),
            ("description", "Description", descDataType),
            ("telephone", "Telephone", textboxDataType),
            ("streetAddress", "Street Address", textboxDataType),
            ("addressLocality", "Address Locality", textboxDataType),
        }, cancellationToken);

        var travelAgencyPageCt = await CreateContentType("travelAgencyPage", "Travel Agency Page", "icon-globe", new[]
        {
            ("title", "Title", textboxDataType),
            ("description", "Description", descDataType),
            ("telephone", "Telephone", textboxDataType),
            ("streetAddress", "Street Address", textboxDataType),
            ("addressLocality", "Address Locality", textboxDataType),
        }, cancellationToken);

        // 3c. New subtype content types — Property (Real Estate)
        var propertyListingCt = await CreateContentType("propertyListing", "Property Listing", "icon-thumbnails-small", listingProps, cancellationToken);

        // 3c. New subtype content types — Education
        var educationListingCt = await CreateContentType("educationListing", "Education Listing", "icon-thumbnails-small", listingProps, cancellationToken);

        var universityPageCt = await CreateContentType("universityPage", "University Page", "icon-mortarboard", new[]
        {
            ("title", "Title", textboxDataType),
            ("description", "Description", descDataType),
            ("streetAddress", "Street Address", textboxDataType),
            ("addressLocality", "Address Locality", textboxDataType),
            ("telephone", "Telephone", textboxDataType),
            ("foundingDate", "Founding Date", textboxDataType),
        }, cancellationToken);

        var schoolPageCt = await CreateContentType("schoolPage", "School Page", "icon-mortarboard", new[]
        {
            ("title", "Title", textboxDataType),
            ("description", "Description", descDataType),
            ("streetAddress", "Street Address", textboxDataType),
            ("addressLocality", "Address Locality", textboxDataType),
            ("telephone", "Telephone", textboxDataType),
        }, cancellationToken);

        var courseInstancePageCt = await CreateContentType("courseInstancePage", "Course Instance Page", "icon-mortarboard", new[]
        {
            ("title", "Title", textboxDataType),
            ("description", "Description", descDataType),
            ("startDate", "Start Date", dateTimeDataType ?? textboxDataType),
            ("endDate", "End Date", dateTimeDataType ?? textboxDataType),
            ("locationName", "Location Name", textboxDataType),
            ("instructor", "Instructor", textboxDataType),
        }, cancellationToken);

        // 3c. New subtype content types — Blog (additional)
        var blogPageCt = await CreateContentType("blogPage", "Blog Page", "icon-edit", new[]
        {
            ("title", "Title", textboxDataType),
            ("description", "Description", descDataType),
        }, cancellationToken);

        var liveBlogPageCt = await CreateContentType("liveBlogPage", "Live Blog Page", "icon-edit", new[]
        {
            ("title", "Title", textboxDataType),
            ("description", "Description", descDataType),
            ("bodyText", "Body Text", bodyDataType),
            ("publishDate", "Publish Date", dateTimeDataType ?? textboxDataType),
            ("authorName", "Author Name", textboxDataType),
            ("coverageStartTime", "Coverage Start Time", dateTimeDataType ?? textboxDataType),
            ("coverageEndTime", "Coverage End Time", dateTimeDataType ?? textboxDataType),
        }, cancellationToken);

        // 3c. New subtype content types — Creative (additional)
        var reportPageCt = await CreateContentType("reportPage", "Report Page", "icon-newspaper-alt", new[]
        {
            ("title", "Title", textboxDataType),
            ("description", "Description", descDataType),
            ("bodyText", "Body Text", bodyDataType),
            ("authorName", "Author Name", textboxDataType),
            ("publishDate", "Publish Date", dateTimeDataType ?? textboxDataType),
        }, cancellationToken);

        var videoGamePageCt = await CreateContentType("videoGamePage", "Video Game Page", "icon-game-controller", new[]
        {
            ("title", "Title", textboxDataType),
            ("description", "Description", descDataType),
            ("applicationCategory", "Application Category", textboxDataType),
            ("operatingSystem", "Operating System", textboxDataType),
            ("gamePlatform", "Game Platform", textboxDataType),
            ("genre", "Genre", textboxDataType),
        }, cancellationToken);

        var sourceCodePageCt = await CreateContentType("sourceCodePage", "Source Code Page", "icon-code", new[]
        {
            ("title", "Title", textboxDataType),
            ("description", "Description", descDataType),
            ("programmingLanguage", "Programming Language", textboxDataType),
            ("runtimePlatform", "Runtime Platform", textboxDataType),
            ("codeRepository", "Code Repository", textboxDataType),
        }, cancellationToken);

        // 3c. New subtype content types — Standalone
        var occupationPageCt = await CreateContentType("occupationPage", "Occupation Page", "icon-users-alt", new[]
        {
            ("title", "Title", textboxDataType),
            ("description", "Description", descDataType),
            ("occupationalCategory", "Occupational Category", textboxDataType),
            ("estimatedSalary", "Estimated Salary", textboxDataType),
            ("skills", "Skills", textboxDataType),
        }, cancellationToken);

        // 3c. New expanded content types — Business/Services
        var servicePageCt = await CreateContentType("servicePage", "Service Page", "icon-wrench", new[]
        {
            ("title", "Title", textboxDataType),
            ("description", "Description", descDataType),
            ("serviceType", "Service Type", textboxDataType),
            ("provider", "Provider", textboxDataType),
            ("areaServed", "Area Served", textboxDataType),
            ("price", "Price", textboxDataType),
        }, cancellationToken);

        var professionalServicePageCt = await CreateContentType("professionalServicePage", "Professional Service Page", "icon-certificate", new[]
        {
            ("title", "Title", textboxDataType),
            ("description", "Description", descDataType),
            ("telephone", "Telephone", textboxDataType),
            ("email", "Email", textboxDataType),
            ("streetAddress", "Street Address", textboxDataType),
            ("addressLocality", "Address Locality", textboxDataType),
            ("postalCode", "Postal Code", textboxDataType),
            ("addressCountry", "Address Country", textboxDataType),
            ("priceRange", "Price Range", textboxDataType),
        }, cancellationToken);

        var legalServicePageCt = await CreateContentType("legalServicePage", "Legal Service Page", "icon-gavel", new[]
        {
            ("title", "Title", textboxDataType),
            ("description", "Description", descDataType),
            ("telephone", "Telephone", textboxDataType),
            ("email", "Email", textboxDataType),
            ("streetAddress", "Street Address", textboxDataType),
            ("addressLocality", "Address Locality", textboxDataType),
            ("postalCode", "Postal Code", textboxDataType),
            ("addressCountry", "Address Country", textboxDataType),
        }, cancellationToken);

        // 3c-ii. New Legal category content types
        var legalListingCt = await CreateContentType("legalListing", "Legal Listing", "icon-gavel", listingProps, cancellationToken);

        var notaryPageCt = await CreateContentType("notaryPage", "Notary Page", "icon-stamp", new[]
        {
            ("title", "Title", textboxDataType),
            ("description", "Description", descDataType),
            ("telephone", "Telephone", textboxDataType),
            ("email", "Email", textboxDataType),
            ("streetAddress", "Street Address", textboxDataType),
            ("addressLocality", "Address Locality", textboxDataType),
            ("postalCode", "Postal Code", textboxDataType),
            ("addressCountry", "Address Country", textboxDataType),
        }, cancellationToken);

        var courthousePageCt = await CreateContentType("courthousePage", "Courthouse Page", "icon-scales", new[]
        {
            ("title", "Title", textboxDataType),
            ("description", "Description", descDataType),
            ("telephone", "Telephone", textboxDataType),
            ("streetAddress", "Street Address", textboxDataType),
            ("addressLocality", "Address Locality", textboxDataType),
            ("postalCode", "Postal Code", textboxDataType),
            ("addressCountry", "Address Country", textboxDataType),
        }, cancellationToken);

        var legislationPageCt = await CreateContentType("legislationPage", "Legislation Page", "icon-certificate", new[]
        {
            ("title", "Title", textboxDataType),
            ("description", "Description", descDataType),
            ("bodyText", "Body Text", bodyDataType),
            ("legislationIdentifier", "Legislation Identifier", textboxDataType),
            ("legislationDate", "Legislation Date", dateTimeDataType ?? textboxDataType),
            ("legislationJurisdiction", "Jurisdiction", textboxDataType),
            ("legislationType", "Legislation Type", textboxDataType),
            ("legislationLegalForce", "Legal Force Status", textboxDataType),
        }, cancellationToken);

        var financialServicePageCt = await CreateContentType("financialServicePage", "Financial Service Page", "icon-coins-alt", new[]
        {
            ("title", "Title", textboxDataType),
            ("description", "Description", descDataType),
            ("telephone", "Telephone", textboxDataType),
            ("email", "Email", textboxDataType),
            ("streetAddress", "Street Address", textboxDataType),
            ("addressLocality", "Address Locality", textboxDataType),
            ("postalCode", "Postal Code", textboxDataType),
            ("addressCountry", "Address Country", textboxDataType),
        }, cancellationToken);

        var governmentOrgPageCt = await CreateContentType("governmentOrgPage", "Government Organisation Page", "icon-globe", new[]
        {
            ("title", "Title", textboxDataType),
            ("description", "Description", descDataType),
            ("telephone", "Telephone", textboxDataType),
            ("email", "Email", textboxDataType),
            ("streetAddress", "Street Address", textboxDataType),
            ("addressLocality", "Address Locality", textboxDataType),
            ("postalCode", "Postal Code", textboxDataType),
            ("addressCountry", "Address Country", textboxDataType),
            ("areaServed", "Area Served", textboxDataType),
        }, cancellationToken);

        // 3c. New expanded content types — Places
        var libraryPageCt = await CreateContentType("libraryPage", "Library Page", "icon-book-alt", new[]
        {
            ("title", "Title", textboxDataType),
            ("description", "Description", descDataType),
            ("telephone", "Telephone", textboxDataType),
            ("streetAddress", "Street Address", textboxDataType),
            ("addressLocality", "Address Locality", textboxDataType),
            ("postalCode", "Postal Code", textboxDataType),
            ("addressCountry", "Address Country", textboxDataType),
        }, cancellationToken);

        var movieTheaterPageCt = await CreateContentType("movieTheaterPage", "Movie Theater Page", "icon-movie", new[]
        {
            ("title", "Title", textboxDataType),
            ("description", "Description", descDataType),
            ("telephone", "Telephone", textboxDataType),
            ("screenCount", "Screen Count", textboxDataType),
            ("streetAddress", "Street Address", textboxDataType),
            ("addressLocality", "Address Locality", textboxDataType),
            ("postalCode", "Postal Code", textboxDataType),
            ("addressCountry", "Address Country", textboxDataType),
        }, cancellationToken);

        var nightClubPageCt = await CreateContentType("nightClubPage", "Night Club Page", "icon-music", new[]
        {
            ("title", "Title", textboxDataType),
            ("description", "Description", descDataType),
            ("telephone", "Telephone", textboxDataType),
            ("streetAddress", "Street Address", textboxDataType),
            ("addressLocality", "Address Locality", textboxDataType),
            ("postalCode", "Postal Code", textboxDataType),
            ("addressCountry", "Address Country", textboxDataType),
        }, cancellationToken);

        var stadiumPageCt = await CreateContentType("stadiumPage", "Stadium Page", "icon-trophy", new[]
        {
            ("title", "Title", textboxDataType),
            ("description", "Description", descDataType),
            ("telephone", "Telephone", textboxDataType),
            ("maximumAttendeeCapacity", "Maximum Attendee Capacity", textboxDataType),
            ("streetAddress", "Street Address", textboxDataType),
            ("addressLocality", "Address Locality", textboxDataType),
            ("postalCode", "Postal Code", textboxDataType),
            ("addressCountry", "Address Country", textboxDataType),
        }, cancellationToken);

        var skiResortPageCt = await CreateContentType("skiResortPage", "Ski Resort Page", "icon-cloud", new[]
        {
            ("title", "Title", textboxDataType),
            ("description", "Description", descDataType),
            ("telephone", "Telephone", textboxDataType),
            ("streetAddress", "Street Address", textboxDataType),
            ("addressLocality", "Address Locality", textboxDataType),
            ("postalCode", "Postal Code", textboxDataType),
            ("addressCountry", "Address Country", textboxDataType),
        }, cancellationToken);

        var golfCoursePageCt = await CreateContentType("golfCoursePage", "Golf Course Page", "icon-tree", new[]
        {
            ("title", "Title", textboxDataType),
            ("description", "Description", descDataType),
            ("telephone", "Telephone", textboxDataType),
            ("streetAddress", "Street Address", textboxDataType),
            ("addressLocality", "Address Locality", textboxDataType),
            ("postalCode", "Postal Code", textboxDataType),
            ("addressCountry", "Address Country", textboxDataType),
        }, cancellationToken);

        // 3c. New expanded content types — Accommodation
        var apartmentPageCt = await CreateContentType("apartmentPage", "Apartment Page", "icon-umb-members", new[]
        {
            ("title", "Title", textboxDataType),
            ("description", "Description", descDataType),
            ("numberOfRooms", "Number of Rooms", textboxDataType),
            ("floorSize", "Floor Size", textboxDataType),
            ("petsAllowed", "Pets Allowed", textboxDataType),
            ("streetAddress", "Street Address", textboxDataType),
            ("addressLocality", "Address Locality", textboxDataType),
            ("postalCode", "Postal Code", textboxDataType),
            ("addressCountry", "Address Country", textboxDataType),
        }, cancellationToken);

        var housePageCt = await CreateContentType("housePage", "House Page", "icon-home", new[]
        {
            ("title", "Title", textboxDataType),
            ("description", "Description", descDataType),
            ("numberOfRooms", "Number of Rooms", textboxDataType),
            ("floorSize", "Floor Size", textboxDataType),
            ("streetAddress", "Street Address", textboxDataType),
            ("addressLocality", "Address Locality", textboxDataType),
            ("postalCode", "Postal Code", textboxDataType),
            ("addressCountry", "Address Country", textboxDataType),
        }, cancellationToken);

        var lodgingBusinessPageCt = await CreateContentType("lodgingBusinessPage", "Lodging Business Page", "icon-hotel", new[]
        {
            ("title", "Title", textboxDataType),
            ("description", "Description", descDataType),
            ("telephone", "Telephone", textboxDataType),
            ("starRating", "Star Rating", textboxDataType),
            ("checkinTime", "Check-in Time", textboxDataType),
            ("checkoutTime", "Check-out Time", textboxDataType),
            ("streetAddress", "Street Address", textboxDataType),
            ("addressLocality", "Address Locality", textboxDataType),
            ("postalCode", "Postal Code", textboxDataType),
            ("addressCountry", "Address Country", textboxDataType),
        }, cancellationToken);

        // 3c. New expanded content types — Creative
        var articlePageCt = await CreateContentType("articlePage", "Article Page", "icon-document", new[]
        {
            ("title", "Title", textboxDataType),
            ("description", "Description", descDataType),
            ("bodyText", "Body Text", bodyDataType),
            ("authorName", "Author Name", textboxDataType),
            ("datePublished", "Date Published", dateTimeDataType ?? textboxDataType),
            ("articleSection", "Article Section", textboxDataType),
            ("wordCount", "Word Count", textboxDataType),
        }, cancellationToken);

        var podcastSeriesPageCt = await CreateContentType("podcastSeriesPage", "Podcast Series Page", "icon-microphone", new[]
        {
            ("title", "Title", textboxDataType),
            ("description", "Description", descDataType),
            ("webFeed", "Web Feed", textboxDataType),
            ("authorName", "Author Name", textboxDataType),
        }, cancellationToken);

        var musicRecordingPageCt = await CreateContentType("musicRecordingPage", "Music Recording Page", "icon-music", new[]
        {
            ("title", "Title", textboxDataType),
            ("description", "Description", descDataType),
            ("duration", "Duration", textboxDataType),
            ("byArtist", "By Artist", textboxDataType),
            ("inAlbum", "In Album", textboxDataType),
            ("datePublished", "Date Published", dateTimeDataType ?? textboxDataType),
        }, cancellationToken);

        // 3c. New expanded content types — Commerce
        var offerPageCt = await CreateContentType("offerPage", "Offer Page", "icon-tag", new[]
        {
            ("title", "Title", textboxDataType),
            ("description", "Description", descDataType),
            ("price", "Price", textboxDataType),
            ("priceCurrency", "Price Currency", textboxDataType),
            ("availability", "Availability", textboxDataType),
            ("validFrom", "Valid From", dateTimeDataType ?? textboxDataType),
            ("validThrough", "Valid Through", dateTimeDataType ?? textboxDataType),
            ("itemOffered", "Item Offered", textboxDataType),
        }, cancellationToken);

        // 3c. New expanded content types — Health
        var diagnosticLabPageCt = await CreateContentType("diagnosticLabPage", "Diagnostic Lab Page", "icon-science", new[]
        {
            ("title", "Title", textboxDataType),
            ("description", "Description", descDataType),
            ("telephone", "Telephone", textboxDataType),
            ("streetAddress", "Street Address", textboxDataType),
            ("addressLocality", "Address Locality", textboxDataType),
            ("postalCode", "Postal Code", textboxDataType),
            ("addressCountry", "Address Country", textboxDataType),
        }, cancellationToken);

        // 3c. New expanded content types — Education
        var educationalOrgPageCt = await CreateContentType("educationalOrgPage", "Educational Organisation Page", "icon-school", new[]
        {
            ("title", "Title", textboxDataType),
            ("description", "Description", descDataType),
            ("telephone", "Telephone", textboxDataType),
            ("streetAddress", "Street Address", textboxDataType),
            ("addressLocality", "Address Locality", textboxDataType),
            ("postalCode", "Postal Code", textboxDataType),
            ("addressCountry", "Address Country", textboxDataType),
        }, cancellationToken);

        // 3c. New expanded content types — Structural
        var webPageCt = await CreateContentType("webPage", "Web Page", "icon-browser-window", new[]
        {
            ("title", "Title", textboxDataType),
            ("description", "Description", descDataType),
            ("bodyText", "Body Text", bodyDataType),
        }, cancellationToken);

        // 3c. New expanded content types — Real Estate
        var realEstateListingPageCt = await CreateContentType("realEstateListingPage", "Real Estate Listing Page", "icon-home", new[]
        {
            ("title", "Title", textboxDataType),
            ("description", "Description", descDataType),
            ("price", "Price", textboxDataType),
            ("priceCurrency", "Price Currency", textboxDataType),
            ("datePosted", "Date Posted", dateTimeDataType ?? textboxDataType),
            ("streetAddress", "Street Address", textboxDataType),
            ("addressLocality", "Address Locality", textboxDataType),
            ("postalCode", "Postal Code", textboxDataType),
            ("addressCountry", "Address Country", textboxDataType),
        }, cancellationToken);

        var singleFamilyResidencePageCt = await CreateContentType("singleFamilyResidencePage", "Single Family Residence Page", "icon-home", new[]
        {
            ("title", "Title", textboxDataType),
            ("description", "Description", descDataType),
            ("numberOfRooms", "Number of Rooms", textboxDataType),
            ("floorSize", "Floor Size", textboxDataType),
            ("numberOfBathroomsTotal", "Number of Bathrooms", textboxDataType),
            ("yearBuilt", "Year Built", textboxDataType),
            ("streetAddress", "Street Address", textboxDataType),
            ("addressLocality", "Address Locality", textboxDataType),
            ("postalCode", "Postal Code", textboxDataType),
            ("addressCountry", "Address Country", textboxDataType),
        }, cancellationToken);

        var apartmentComplexPageCt = await CreateContentType("apartmentComplexPage", "Apartment Complex Page", "icon-umb-members", new[]
        {
            ("title", "Title", textboxDataType),
            ("description", "Description", descDataType),
            ("numberOfAccommodationUnits", "Number of Units", textboxDataType),
            ("petsAllowed", "Pets Allowed", textboxDataType),
            ("telephone", "Telephone", textboxDataType),
            ("streetAddress", "Street Address", textboxDataType),
            ("addressLocality", "Address Locality", textboxDataType),
            ("postalCode", "Postal Code", textboxDataType),
            ("addressCountry", "Address Country", textboxDataType),
        }, cancellationToken);

        var residencePageCt = await CreateContentType("residencePage", "Residence Page", "icon-home", new[]
        {
            ("title", "Title", textboxDataType),
            ("description", "Description", descDataType),
            ("numberOfRooms", "Number of Rooms", textboxDataType),
            ("floorSize", "Floor Size", textboxDataType),
            ("streetAddress", "Street Address", textboxDataType),
            ("addressLocality", "Address Locality", textboxDataType),
            ("postalCode", "Postal Code", textboxDataType),
            ("addressCountry", "Address Country", textboxDataType),
        }, cancellationToken);

        var suitePageCt = await CreateContentType("suitePage", "Suite Page", "icon-hotel", new[]
        {
            ("title", "Title", textboxDataType),
            ("description", "Description", descDataType),
            ("numberOfRooms", "Number of Rooms", textboxDataType),
            ("floorSize", "Floor Size", textboxDataType),
            ("bedType", "Bed Type", textboxDataType),
            ("occupancy", "Occupancy", textboxDataType),
            ("streetAddress", "Street Address", textboxDataType),
            ("addressLocality", "Address Locality", textboxDataType),
            ("postalCode", "Postal Code", textboxDataType),
            ("addressCountry", "Address Country", textboxDataType),
        }, cancellationToken);

        var gatedResidenceCommunityPageCt = await CreateContentType("gatedResidenceCommunityPage", "Gated Residence Community Page", "icon-fence", new[]
        {
            ("title", "Title", textboxDataType),
            ("description", "Description", descDataType),
            ("numberOfAccommodationUnits", "Number of Units", textboxDataType),
            ("petsAllowed", "Pets Allowed", textboxDataType),
            ("streetAddress", "Street Address", textboxDataType),
            ("addressLocality", "Address Locality", textboxDataType),
            ("postalCode", "Postal Code", textboxDataType),
            ("addressCountry", "Address Country", textboxDataType),
        }, cancellationToken);

        var accommodationPageCt = await CreateContentType("accommodationPage", "Accommodation Page", "icon-hotel", new[]
        {
            ("title", "Title", textboxDataType),
            ("description", "Description", descDataType),
            ("numberOfRooms", "Number of Rooms", textboxDataType),
            ("floorSize", "Floor Size", textboxDataType),
            ("petsAllowed", "Pets Allowed", textboxDataType),
            ("tourBookingPage", "Tour Booking Page", textboxDataType),
            ("streetAddress", "Street Address", textboxDataType),
            ("addressLocality", "Address Locality", textboxDataType),
            ("postalCode", "Postal Code", textboxDataType),
            ("addressCountry", "Address Country", textboxDataType),
        }, cancellationToken);

        // 3c. New expanded content types — Hierarchy (parent/ancestor/sibling testing)
        var organisationParentCt = await CreateContentType("organisationParent", "Organisation Parent", "icon-users", new[]
        {
            ("title", "Title", textboxDataType),
            ("description", "Description", descDataType),
            ("telephone", "Telephone", textboxDataType),
            ("email", "Email", textboxDataType),
            ("streetAddress", "Street Address", textboxDataType),
            ("addressLocality", "Address Locality", textboxDataType),
            ("postalCode", "Postal Code", textboxDataType),
            ("addressCountry", "Address Country", textboxDataType),
        }, cancellationToken);

        var localBusinessChildCt = await CreateContentType("localBusinessChild", "Local Business Child", "icon-store", new[]
        {
            ("title", "Title", textboxDataType),
            ("description", "Description", descDataType),
            ("telephone", "Telephone", textboxDataType),
            ("priceRange", "Price Range", textboxDataType),
            ("streetAddress", "Street Address", textboxDataType),
            ("addressLocality", "Address Locality", textboxDataType),
            ("postalCode", "Postal Code", textboxDataType),
            ("addressCountry", "Address Country", textboxDataType),
        }, cancellationToken);

        var departmentPageCt = await CreateContentType("departmentPage", "Department Page", "icon-users-alt", new[]
        {
            ("title", "Title", textboxDataType),
            ("description", "Description", descDataType),
        }, cancellationToken);

        // 3c-landing. Landing Page — exercises BlockGrid + nested ImageObject mapping
        var landingPageCt = await CreateContentType("landingPage", "Landing Page", "icon-presentation", new (string, string, IDataType?)[]
        {
            ("pageTitle", "Page Title", textboxDataType),
            ("metaDescription", "Meta Description", textareaDataType ?? textboxDataType),
            ("heroImage", "Hero Image", mediaPickerDataType ?? textboxDataType),
            ("contentGrid", "Content Grid", contentGridDataType),
        }, cancellationToken);

        // 3d. Variant article — culture-varying content type for multi-language testing
        var variantArticleCt = await CreateVariantArticle(textboxDataType!, bodyDataType!, cancellationToken);

        // 3b. Create master template and assign templates to content types
        if (await _templateService.GetAsync("master") is null)
        {
            await _templateService.CreateAsync("Master", "master", null, Constants.Security.SuperUserKey);
            _logger.LogInformation("TestDataSeeder: Created master template");
        }

        var allContentTypes = new IContentType[]
        {
            blogArticleCt, productPageCt, faqPageCt, eventPageCt, recipePageCt,
            homePageCt, aboutPageCt, categoriesListingCt, blogListingCt, productListingCt, eventListingCt, recipeListingCt,
            newsArticleCt, techArticleCt, softwarePageCt, coursePageCt,
            howToPageCt, videoPageCt, jobPostingPageCt, profilePageCt,
            locationPageCt, restaurantPageCt, bookPageCt, contactPageCt,
            // New subtypes
            vehiclePageCt, financialProductPageCt, individualProductPageCt, productModelPageCt,
            musicEventPageCt, sportsEventPageCt, businessEventPageCt, foodEventPageCt, festivalPageCt, educationEventPageCt,
            organisationListingCt, corporationPageCt, sportsTeamPageCt, airlinePageCt, ngoPageCt,
            placesListingCt, hotelPageCt, storePageCt, hospitalPageCt, gymPageCt,
            creativeListingCt, moviePageCt, musicAlbumPageCt, podcastEpisodePageCt, photographPageCt, datasetPageCt,
            mobileAppPageCt, webAppPageCt,
            // New subtypes — Shops
            shopsListingCt, bookStorePageCt, electronicsStorePageCt, clothingStorePageCt, groceryStorePageCt, furnitureStorePageCt, jewelryStorePageCt,
            // New subtypes — Dining
            diningListingCt, bakeryPageCt, cafePageCt, barPageCt, fastFoodPageCt, wineryPageCt, breweryPageCt,
            // New subtypes — Travel
            travelListingCt, flightReservationPageCt, lodgingReservationPageCt, eventReservationPageCt, rentalCarPageCt, flightPageCt, touristAttractionPageCt, bedAndBreakfastPageCt, campgroundPageCt,
            // New subtypes — Healthcare
            healthcareListingCt, drugPageCt, medicalConditionPageCt, physicianPageCt, pharmacyPageCt, dentistPageCt, medicalClinicPageCt,
            // New subtypes — Automotive
            automotiveListingCt, carPageCt, motorcyclePageCt, autoDealerPageCt, autoRepairPageCt,
            // New subtypes — Events (additional)
            theaterEventPageCt, screeningEventPageCt,
            // New subtypes — Entertainment
            entertainmentListingCt, musicGroupPageCt, zooPageCt, museumPageCt, amusementParkPageCt,
            // New subtypes — Services
            servicesListingCt, attorneyPageCt, accountingPageCt, realEstatePageCt, insurancePageCt, travelAgencyPageCt,
            // New subtypes — Education
            educationListingCt, universityPageCt, schoolPageCt, courseInstancePageCt,
            // New subtypes — Blog (additional)
            blogPageCt, liveBlogPageCt,
            // New subtypes — Creative (additional)
            reportPageCt, videoGamePageCt, sourceCodePageCt,
            // New subtypes — Standalone
            occupationPageCt,
            // New expanded types
            servicePageCt, professionalServicePageCt, legalServicePageCt, financialServicePageCt, governmentOrgPageCt,
            // New subtypes — Legal
            legalListingCt, notaryPageCt, courthousePageCt, legislationPageCt,
            libraryPageCt, movieTheaterPageCt, nightClubPageCt, stadiumPageCt, skiResortPageCt, golfCoursePageCt,
            apartmentPageCt, housePageCt, lodgingBusinessPageCt,
            articlePageCt, podcastSeriesPageCt, musicRecordingPageCt,
            offerPageCt,
            diagnosticLabPageCt,
            educationalOrgPageCt,
            webPageCt,
            realEstateListingPageCt,
            // New subtypes — Property (Real Estate)
            propertyListingCt, singleFamilyResidencePageCt, apartmentComplexPageCt, residencePageCt, suitePageCt, gatedResidenceCommunityPageCt, accommodationPageCt,
            organisationParentCt, localBusinessChildCt, departmentPageCt,
            // Landing page (BlockGrid demo)
            landingPageCt,
            // Variant (culture-varying) content type
            variantArticleCt,
        };

        foreach (var ct in allContentTypes)
        {
            await AssignTemplate(ct);
        }

        // 3b-ii. Allow listing types to nest themselves (subcategories)
        productListingCt.AllowedContentTypes = productListingCt.AllowedContentTypes!
            .Append(new ContentTypeSort(productListingCt.Key, 0, productListingCt.Alias))
            .ToList();
        await _contentTypeService.UpdateAsync(productListingCt, Constants.Security.SuperUserKey);

        eventListingCt.AllowedContentTypes = eventListingCt.AllowedContentTypes!
            .Append(new ContentTypeSort(eventListingCt.Key, 0, eventListingCt.Alias))
            .ToList();
        await _contentTypeService.UpdateAsync(eventListingCt, Constants.Security.SuperUserKey);

        // 3c. Seed media from existing images on disk
        await SeedMediaFromDisk(cancellationToken);

        // 4. Create and publish sample content (hierarchical site tree)
        await CreateSampleContent(
            faqItem, reviewItem, recipeIngredient, recipeStep,
            howToStepEl, howToToolEl, openingHoursEl,
            heroBlockEl, featureBlockEl, quoteBlockEl,
            cancellationToken);

        // 4a. Assign domain/culture routing on the home page (/en → en-US, /de → de-DE)
        try
        {
            var homeContent = _contentService.GetRootContent()?.FirstOrDefault(c => c.ContentType.Alias == "homePage");
            if (homeContent is not null)
            {
                var domainResult = await _domainService.UpdateDomainsAsync(homeContent.Key, new DomainsUpdateModel
                {
                    DefaultIsoCode = "en-US",
                    Domains = new[]
                    {
                        new DomainModel { DomainName = "/en", IsoCode = "en-US" },
                        new DomainModel { DomainName = "/de", IsoCode = "de-DE" },
                    },
                });
                _logger.LogInformation("TestDataSeeder: Domain routing result: {Status}", domainResult.Status);
            }
            else
            {
                _logger.LogWarning("TestDataSeeder: Could not find home page for domain assignment");
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "TestDataSeeder: Could not assign domain routing (may already exist)");
        }

        // 4b. Create variant content (multi-language)
        await CreateVariantArticleContent(cancellationToken);

        // 5. Seed default schema mappings
        SeedSchemaMappings(blogArticleCt, productPageCt, faqPageCt, eventPageCt, recipePageCt,
            homePageCt, aboutPageCt, blogListingCt, productListingCt, eventListingCt, recipeListingCt,
            newsArticleCt, techArticleCt, softwarePageCt, coursePageCt,
            howToPageCt, videoPageCt, jobPostingPageCt, profilePageCt,
            locationPageCt, restaurantPageCt, bookPageCt, contactPageCt,
            vehiclePageCt, financialProductPageCt, individualProductPageCt, productModelPageCt,
            musicEventPageCt, sportsEventPageCt, businessEventPageCt, foodEventPageCt, festivalPageCt, educationEventPageCt,
            organisationListingCt, corporationPageCt, sportsTeamPageCt, airlinePageCt, ngoPageCt,
            placesListingCt, hotelPageCt, storePageCt, hospitalPageCt, gymPageCt,
            creativeListingCt, moviePageCt, musicAlbumPageCt, podcastEpisodePageCt, photographPageCt, datasetPageCt,
            mobileAppPageCt, webAppPageCt,
            shopsListingCt, bookStorePageCt, electronicsStorePageCt, clothingStorePageCt, groceryStorePageCt, furnitureStorePageCt, jewelryStorePageCt,
            diningListingCt, bakeryPageCt, cafePageCt, barPageCt, fastFoodPageCt, wineryPageCt, breweryPageCt,
            travelListingCt, flightReservationPageCt, lodgingReservationPageCt, eventReservationPageCt, rentalCarPageCt, flightPageCt, touristAttractionPageCt, bedAndBreakfastPageCt, campgroundPageCt,
            healthcareListingCt, drugPageCt, medicalConditionPageCt, physicianPageCt, pharmacyPageCt, dentistPageCt, medicalClinicPageCt,
            automotiveListingCt, carPageCt, motorcyclePageCt, autoDealerPageCt, autoRepairPageCt,
            theaterEventPageCt, screeningEventPageCt,
            entertainmentListingCt, musicGroupPageCt, zooPageCt, museumPageCt, amusementParkPageCt,
            servicesListingCt, attorneyPageCt, accountingPageCt, realEstatePageCt, insurancePageCt, travelAgencyPageCt,
            educationListingCt, universityPageCt, schoolPageCt, courseInstancePageCt,
            blogPageCt, liveBlogPageCt,
            reportPageCt, videoGamePageCt, sourceCodePageCt,
            occupationPageCt,
            servicePageCt, professionalServicePageCt, legalServicePageCt, financialServicePageCt, governmentOrgPageCt,
            // New subtypes — Legal
            legalListingCt, notaryPageCt, courthousePageCt, legislationPageCt,
            libraryPageCt, movieTheaterPageCt, nightClubPageCt, stadiumPageCt, skiResortPageCt, golfCoursePageCt,
            apartmentPageCt, housePageCt, lodgingBusinessPageCt,
            articlePageCt, podcastSeriesPageCt, musicRecordingPageCt,
            offerPageCt,
            diagnosticLabPageCt,
            educationalOrgPageCt,
            webPageCt,
            realEstateListingPageCt,
            propertyListingCt, singleFamilyResidencePageCt, apartmentComplexPageCt, residencePageCt, suitePageCt, gatedResidenceCommunityPageCt, accommodationPageCt,
            organisationParentCt, localBusinessChildCt, departmentPageCt,
            // Landing page (BlockGrid demo — WebPage + nested ImageObject + BlockGrid mainEntity)
            landingPageCt,
            // Variant (culture-varying)
            variantArticleCt);
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    // ──────────────────────────────────────────────────────────────
    // Media seeding
    // ──────────────────────────────────────────────────────────────

    private async Task SeedMediaFromDisk(CancellationToken ct)
    {
        // Read committed seed assets from the project tree (NOT Umbraco's runtime wwwroot/media
        // ULID dirs, which are per-session and not in git). The .csproj copies SeedAssets/Media
        // to the output directory via <Content CopyToOutputDirectory="PreserveNewest" />.
        var seedDir = Path.Combine(_webHostEnvironment.ContentRootPath, "SeedAssets", "Media");
        if (!Directory.Exists(seedDir))
        {
            _logger.LogWarning("TestDataSeeder: Seed media directory missing at {Path}", seedDir);
            return;
        }

        var imageFiles = Directory.GetFiles(seedDir, "*.png")
            .Concat(Directory.GetFiles(seedDir, "*.jpg"))
            .Concat(Directory.GetFiles(seedDir, "*.jpeg"))
            .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
            .ToList();

        foreach (var filePath in imageFiles)
        {
            try
            {
                var fileName = Path.GetFileName(filePath);
                var key = Path.GetFileNameWithoutExtension(filePath); // stable basename key

                await using var stream = System.IO.File.OpenRead(filePath);
                var result = await _mediaImportService.ImportAsync(
                    fileName,
                    stream,
                    null, // root
                    Constants.Conventions.MediaTypes.Image,
                    Constants.Security.SuperUserKey);

                _mediaKeys[key] = result.Key;
                _logger.LogInformation("TestDataSeeder: Imported '{Key}' ({Id}) from {File}",
                    key, result.Key, fileName);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "TestDataSeeder: Failed to import media from {File}", filePath);
            }
        }

        _logger.LogInformation("TestDataSeeder: Seeded {Count} media items from {Path}", _mediaKeys.Count, seedDir);
    }

    /// <summary>
    /// Assigns a hero image media picker value to a content item if the corresponding media
    /// was imported during <see cref="SeedMediaFromDisk"/>. No-op if the media key is missing,
    /// which means content creation remains resilient when generation hasn't produced every image yet.
    /// </summary>
    private void TrySetHeroImage(IContent content, string mediaKey)
    {
        var value = BuildMediaPickerValue(mediaKey);
        if (value is not null)
            content.SetValue("heroImage", value);
    }

    /// <summary>
    /// Builds the MediaPicker3 JSON value for a single media item.
    /// </summary>
    private string? BuildMediaPickerValue(string mediaKeyAlias)
    {
        if (!_mediaKeys.TryGetValue(mediaKeyAlias, out var mediaKey))
            return null;

        return JsonSerializer.Serialize(new[]
        {
            new { key = Guid.NewGuid(), mediaKey }
        });
    }

    // ──────────────────────────────────────────────────────────────
    // Content type creation
    // ──────────────────────────────────────────────────────────────

    private async Task<IContentType> CreateElementType(
        string alias, string name,
        (string alias, string name, IDataType dataType)[] properties,
        CancellationToken cancellationToken)
    {
        var ct = new ContentType(_shortStringHelper, -1)
        {
            Alias = alias,
            Name = name,
            IsElement = true,
            Icon = "icon-science",
        };

        ct.AddPropertyGroup("content", "Content");

        foreach (var (propAlias, propName, dataType) in properties)
        {
            ct.AddPropertyType(new PropertyType(_shortStringHelper, dataType)
            {
                Alias = propAlias,
                Name = propName,
            }, "content", "Content");
        }

        await _contentTypeService.CreateAsync(ct, Constants.Security.SuperUserKey).ConfigureAwait(false);
        return ct;
    }

    /// <summary>
    /// Generic helper that creates a standard content type with properties in a single "content" group,
    /// plus optional additional properties (e.g. BlockList) in extra groups.
    /// </summary>
    private async Task<IContentType> CreateContentType(
        string alias, string name, string icon,
        (string alias, string name, IDataType? dataType)[] properties,
        CancellationToken cancellationToken,
        params (string alias, string name, IDataType? dataType)[] extraProperties)
    {
        var ct = new ContentType(_shortStringHelper, -1)
        {
            Alias = alias,
            Name = name,
            AllowedAsRoot = true,
            Icon = icon,
        };

        ct.AddPropertyGroup("content", "Content");

        foreach (var (propAlias, propName, dataType) in properties)
        {
            if (dataType is null) continue;
            ct.AddPropertyType(new PropertyType(_shortStringHelper, dataType)
            {
                Alias = propAlias,
                Name = propName,
            }, "content", "Content");
        }

        // Universal hero image: every doctype gets a "heroImage" media picker so the demo site
        // has a consistent image slot on every Schema.org type. Opt out by passing heroImage in
        // properties or extraProperties.
        var alreadyHasHeroImage =
            properties.Any(p => string.Equals(p.alias, "heroImage", StringComparison.OrdinalIgnoreCase))
            || extraProperties.Any(p => string.Equals(p.alias, "heroImage", StringComparison.OrdinalIgnoreCase));

        if (!alreadyHasHeroImage && _mediaPickerDataType is not null)
        {
            ct.AddPropertyType(new PropertyType(_shortStringHelper, _mediaPickerDataType)
            {
                Alias = "heroImage",
                Name = "Hero Image",
            }, "content", "Content");
        }

        foreach (var (propAlias, propName, dataType) in extraProperties)
        {
            if (dataType is null) continue;
            ct.AddPropertyType(new PropertyType(_shortStringHelper, dataType)
            {
                Alias = propAlias,
                Name = propName,
            }, "content", "Content");
        }

        await _contentTypeService.CreateAsync(ct, Constants.Security.SuperUserKey).ConfigureAwait(false);
        return ct;
    }

    private async Task<IDataType> CreateBlockListDataType(
        string name,
        IContentType[] elementTypes,
        CancellationToken cancellationToken)
    {
        if (!_propertyEditors.TryGet(Constants.PropertyEditors.Aliases.BlockList, out var blockListEditor))
        {
            _logger.LogError("TestDataSeeder: BlockList property editor not found — cannot create {Name}", name);
            throw new InvalidOperationException($"BlockList property editor '{Constants.PropertyEditors.Aliases.BlockList}' not found");
        }

        // Build blocks config as array of objects with contentElementTypeKey
        var blocks = elementTypes.Select(et => (object)new Dictionary<string, object?>
        {
            ["contentElementTypeKey"] = et.Key.ToString(),
            ["label"] = et.Name,
        }).ToList();

        var configData = new Dictionary<string, object>
        {
            ["blocks"] = blocks,
            ["validationLimit"] = new Dictionary<string, object?> { ["min"] = null, ["max"] = null },
            ["useSingleBlockMode"] = false,
            ["useLiveEditing"] = false,
            ["useInlineEditingAsDefault"] = false,
        };

        var dataType = new DataType(blockListEditor, _configSerializer, -1)
        {
            Name = name,
            DatabaseType = ValueStorageType.Ntext,
            ConfigurationData = configData,
            EditorUiAlias = "Umb.PropertyEditorUi.BlockList",
        };

        await _dataTypeService.CreateAsync(dataType, Constants.Security.SuperUserKey).ConfigureAwait(false);
        return dataType;
    }

    /// <summary>
    /// Creates a BlockGrid data type with a single 12-column root area allowing the supplied element types.
    /// Mirrors <see cref="CreateBlockListDataType"/> for the BlockGrid editor (Umbraco.BlockGrid).
    /// </summary>
    private async Task<IDataType> CreateBlockGridDataType(
        string name,
        IContentType[] elementTypes,
        CancellationToken cancellationToken)
    {
        if (!_propertyEditors.TryGet(Constants.PropertyEditors.Aliases.BlockGrid, out var blockGridEditor))
        {
            _logger.LogError("TestDataSeeder: BlockGrid property editor not found — cannot create {Name}", name);
            throw new InvalidOperationException($"BlockGrid property editor '{Constants.PropertyEditors.Aliases.BlockGrid}' not found");
        }

        // Every block is allowed at root; no per-block areas configured (minimal setup).
        var blocks = elementTypes.Select(et => (object)new Dictionary<string, object?>
        {
            ["contentElementTypeKey"] = et.Key.ToString(),
            ["label"] = et.Name,
            ["allowAtRoot"] = true,
            ["allowInAreas"] = false,
            ["areas"] = Array.Empty<object>(),
        }).ToList();

        var configData = new Dictionary<string, object>
        {
            ["blocks"] = blocks,
            ["validationLimit"] = new Dictionary<string, object?> { ["min"] = null, ["max"] = null },
            ["gridColumns"] = 12,
        };

        var dataType = new DataType(blockGridEditor, _configSerializer, -1)
        {
            Name = name,
            DatabaseType = ValueStorageType.Ntext,
            ConfigurationData = configData,
            EditorUiAlias = "Umb.PropertyEditorUi.BlockGrid",
        };

        await _dataTypeService.CreateAsync(dataType, Constants.Security.SuperUserKey).ConfigureAwait(false);
        return dataType;
    }

    private async Task AssignTemplate(IContentType ct)
    {
        var template = await _templateService.GetAsync(ct.Alias);

        // Create the template if it doesn't exist (Umbraco stores templates in DB)
        if (template is null)
        {
            var viewContent = $@"@inherits Umbraco.Cms.Web.Common.Views.UmbracoViewPage
@{{
    Layout = ""master.cshtml"";
}}";
            var result = await _templateService.CreateAsync(ct.Name ?? ct.Alias, ct.Alias, viewContent, Constants.Security.SuperUserKey);
            template = result.Result;
            _logger.LogInformation("TestDataSeeder: Created template {Alias}", ct.Alias);
        }

        if (template is not null)
        {
            ct.AllowedTemplates = new[] { template };
            ct.SetDefaultTemplate(template);
        }
        await _contentTypeService.UpdateAsync(ct, Constants.Security.SuperUserKey);
        _logger.LogInformation("TestDataSeeder: Assigned template {Alias} to {ContentType}", ct.Alias, ct.Name);
    }

    private async Task<IContentType> CreateBlogArticle(
        IDataType textbox, IDataType? desc, IDataType? body,
        IDataType? dateTime, IDataType? mediaPicker,
        CancellationToken cancellationToken)
    {
        var ct = new ContentType(_shortStringHelper, -1)
        {
            Alias = "blogArticle",
            Name = "Blog Article",
            Icon = "icon-edit",
            AllowedAsRoot = true,
            Variations = ContentVariation.Culture,
        };

        ct.AddPropertyGroup("content", "Content");
        ct.AddPropertyGroup("metadata", "Metadata");

        AddProperty(ct, "title", "Title", textbox, "content", true, ContentVariation.Culture);
        AddProperty(ct, "description", "Description", desc, "content", variations: ContentVariation.Culture);
        AddProperty(ct, "bodyText", "Body Text", body, "content", variations: ContentVariation.Culture);
        AddProperty(ct, "authorName", "Author Name", textbox, "metadata");
        AddProperty(ct, "publishDate", "Publish Date", dateTime ?? textbox, "metadata");
        AddProperty(ct, "featuredImage", "Featured Image", mediaPicker ?? textbox, "content");
        AddProperty(ct, "heroImage", "Hero Image", mediaPicker ?? textbox, "content");
        AddProperty(ct, "keywords", "Keywords", textbox, "metadata");
        AddProperty(ct, "category", "Category", textbox, "metadata");

        await _contentTypeService.CreateAsync(ct, Constants.Security.SuperUserKey).ConfigureAwait(false);
        return ct;
    }

    private async Task<IContentType> CreateProductPage(
        IDataType textbox, IDataType? desc, IDataType? body,
        IDataType? mediaPicker, IDataType reviewsBlockList,
        CancellationToken cancellationToken)
    {
        var ct = new ContentType(_shortStringHelper, -1)
        {
            Alias = "productPage",
            Name = "Product Page",
            Icon = "icon-box",
            AllowedAsRoot = true,
            Variations = ContentVariation.Culture,
        };

        ct.AddPropertyGroup("content", "Content");
        ct.AddPropertyGroup("product", "Product Details");

        AddProperty(ct, "title", "Title", textbox, "content", true, ContentVariation.Culture);
        AddProperty(ct, "description", "Description", desc, "content", variations: ContentVariation.Culture);
        AddProperty(ct, "bodyText", "Body Text", body, "content");
        AddProperty(ct, "price", "Price", textbox, "product");
        AddProperty(ct, "sku", "SKU", textbox, "product");
        AddProperty(ct, "brand", "Brand", textbox, "product");
        AddProperty(ct, "availability", "Availability", textbox, "product");
        AddProperty(ct, "currency", "Currency", textbox, "product");
        AddProperty(ct, "productImage", "Product Image", mediaPicker ?? textbox, "content");
        AddProperty(ct, "heroImage", "Hero Image", mediaPicker ?? textbox, "content");
        AddProperty(ct, "reviews", "Reviews", reviewsBlockList, "product");

        await _contentTypeService.CreateAsync(ct, Constants.Security.SuperUserKey).ConfigureAwait(false);
        return ct;
    }

    private async Task<IContentType> CreateFaqPage(
        IDataType textbox, IDataType? desc, IDataType faqBlockList,
        CancellationToken cancellationToken)
    {
        var ct = new ContentType(_shortStringHelper, -1)
        {
            Alias = "faqPage",
            Name = "FAQ Page",
            Icon = "icon-help-alt",
            AllowedAsRoot = true,
            Variations = ContentVariation.Culture,
        };

        ct.AddPropertyGroup("content", "Content");

        AddProperty(ct, "title", "Title", textbox, "content", true, ContentVariation.Culture);
        AddProperty(ct, "description", "Description", desc, "content", variations: ContentVariation.Culture);
        AddProperty(ct, "faqItems", "FAQ Items", faqBlockList, "content");
        if (_mediaPickerDataType is not null)
            AddProperty(ct, "heroImage", "Hero Image", _mediaPickerDataType, "content");

        await _contentTypeService.CreateAsync(ct, Constants.Security.SuperUserKey).ConfigureAwait(false);
        return ct;
    }

    private async Task<IContentType> CreateContactPage(
        IDataType textbox,
        CancellationToken cancellationToken)
    {
        var ct = new ContentType(_shortStringHelper, -1)
        {
            Alias = "contactPage",
            Name = "Contact Page",
            Icon = "icon-operator",
            AllowedAsRoot = true,
        };

        ct.AddPropertyGroup("content", "Content");
        ct.AddPropertyGroup("address", "Address");

        AddProperty(ct, "title", "Title", textbox, "content", true);
        AddProperty(ct, "telephone", "Telephone", textbox, "content");
        AddProperty(ct, "email", "Email", textbox, "content");
        AddProperty(ct, "streetAddress", "Street Address", textbox, "address");
        AddProperty(ct, "addressLocality", "City/Town", textbox, "address");
        AddProperty(ct, "postalCode", "Post Code", textbox, "address");
        AddProperty(ct, "addressCountry", "Country", textbox, "address");
        AddProperty(ct, "openingHours", "Opening Hours", textbox, "content");
        if (_mediaPickerDataType is not null)
            AddProperty(ct, "heroImage", "Hero Image", _mediaPickerDataType, "content");

        await _contentTypeService.CreateAsync(ct, Constants.Security.SuperUserKey).ConfigureAwait(false);
        return ct;
    }

    private async Task<IContentType> CreateEventPage(
        IDataType textbox, IDataType? desc,
        IDataType? dateTime, IDataType? mediaPicker,
        CancellationToken cancellationToken)
    {
        var ct = new ContentType(_shortStringHelper, -1)
        {
            Alias = "eventPage",
            Name = "Event Page",
            Icon = "icon-calendar",
            AllowedAsRoot = true,
            Variations = ContentVariation.Culture,
        };

        ct.AddPropertyGroup("content", "Content");
        ct.AddPropertyGroup("event", "Event Details");

        AddProperty(ct, "title", "Title", textbox, "content", true, ContentVariation.Culture);
        AddProperty(ct, "description", "Description", desc, "content", variations: ContentVariation.Culture);
        AddProperty(ct, "startDate", "Start Date", dateTime ?? textbox, "event");
        AddProperty(ct, "endDate", "End Date", dateTime ?? textbox, "event");
        AddProperty(ct, "locationName", "Location Name", textbox, "event");
        AddProperty(ct, "locationAddress", "Location Address", textbox, "event");
        AddProperty(ct, "organiserName", "Organiser Name", textbox, "event");
        AddProperty(ct, "ticketPrice", "Ticket Price", textbox, "event");
        AddProperty(ct, "ticketUrl", "Ticket URL", textbox, "event");
        AddProperty(ct, "eventImage", "Event Image", mediaPicker ?? textbox, "content");
        AddProperty(ct, "heroImage", "Hero Image", mediaPicker ?? textbox, "content");

        await _contentTypeService.CreateAsync(ct, Constants.Security.SuperUserKey).ConfigureAwait(false);
        return ct;
    }

    private async Task<IContentType> CreateRecipePage(
        IDataType textbox, IDataType? desc,
        IDataType? mediaPicker,
        IDataType ingredientsBlockList, IDataType instructionsBlockList,
        CancellationToken cancellationToken)
    {
        var ct = new ContentType(_shortStringHelper, -1)
        {
            Alias = "recipePage",
            Name = "Recipe Page",
            Icon = "icon-food",
            AllowedAsRoot = true,
            Variations = ContentVariation.Culture,
        };

        ct.AddPropertyGroup("content", "Content");
        ct.AddPropertyGroup("recipe", "Recipe Details");
        ct.AddPropertyGroup("ingredients", "Ingredients & Instructions");

        AddProperty(ct, "title", "Title", textbox, "content", true, ContentVariation.Culture);
        AddProperty(ct, "description", "Description", desc, "content", variations: ContentVariation.Culture);
        AddProperty(ct, "prepTime", "Prep Time", textbox, "recipe");
        AddProperty(ct, "cookTime", "Cook Time", textbox, "recipe");
        AddProperty(ct, "totalTime", "Total Time", textbox, "recipe");
        AddProperty(ct, "recipeYield", "Servings", textbox, "recipe");
        AddProperty(ct, "calories", "Calories", textbox, "recipe");
        AddProperty(ct, "recipeCategory", "Category", textbox, "recipe");
        AddProperty(ct, "recipeCuisine", "Cuisine", textbox, "recipe");
        AddProperty(ct, "authorName", "Author Name", textbox, "recipe");
        AddProperty(ct, "recipeImage", "Recipe Image", mediaPicker ?? textbox, "content");
        AddProperty(ct, "heroImage", "Hero Image", mediaPicker ?? textbox, "content");
        AddProperty(ct, "ingredients", "Ingredients", ingredientsBlockList, "ingredients");
        AddProperty(ct, "instructions", "Instructions", instructionsBlockList, "ingredients");

        await _contentTypeService.CreateAsync(ct, Constants.Security.SuperUserKey).ConfigureAwait(false);
        return ct;
    }

    private void AddProperty(
        ContentType ct, string alias, string name,
        IDataType? dataType, string groupAlias,
        bool mandatory = false,
        ContentVariation variations = ContentVariation.Nothing)
    {
        if (dataType is null) return;

        ct.AddPropertyType(new PropertyType(_shortStringHelper, dataType)
        {
            Alias = alias,
            Name = name,
            Mandatory = mandatory,
            Variations = variations,
        }, groupAlias);
    }

    // ──────────────────────────────────────────────────────────────
    // Variant (culture-varying) content type + content
    // ──────────────────────────────────────────────────────────────

    private async Task<IContentType> CreateVariantArticle(
        IDataType textbox, IDataType body, CancellationToken cancellationToken)
    {
        var ct = new ContentType(_shortStringHelper, -1)
        {
            Alias = "variantArticle",
            Name = "Variant Article",
            Icon = "icon-globe",
            AllowedAsRoot = true,
            Variations = ContentVariation.Culture,
        };

        ct.AddPropertyGroup("content", "Content");

        ct.AddPropertyType(new PropertyType(_shortStringHelper, textbox)
        {
            Alias = "title",
            Name = "Title",
            Mandatory = true,
            Variations = ContentVariation.Culture,
        }, "content", "Content");

        ct.AddPropertyType(new PropertyType(_shortStringHelper, body)
        {
            Alias = "bodyText",
            Name = "Body Text",
            Variations = ContentVariation.Culture,
        }, "content", "Content");

        if (_mediaPickerDataType is not null)
        {
            ct.AddPropertyType(new PropertyType(_shortStringHelper, _mediaPickerDataType)
            {
                Alias = "heroImage",
                Name = "Hero Image",
            }, "content", "Content");
        }

        await _contentTypeService.CreateAsync(ct, Constants.Security.SuperUserKey).ConfigureAwait(false);
        return ct;
    }

    /// <summary>
    /// Well-known key for the variant article content item. E2E tests in
    /// <c>language-variants.spec.ts</c> reference this directly so they
    /// don't need to discover it via the Umbraco management tree API
    /// (which requires OAuth tokens the Playwright storageState doesn't carry).
    /// </summary>
    public static readonly Guid VariantArticleContentKey =
        Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");

    private async Task CreateVariantArticleContent(CancellationToken cancellationToken)
    {
        var content = _contentService.Create("Test Variant Article", Constants.System.Root, "variantArticle");
        content.Key = VariantArticleContentKey;
        content.SetCultureName("Test Variant Article", "en-US");
        content.SetCultureName("Testvarianten-Artikel", "de-DE");
        content.SetValue("title", "Seven things about SchemeWeaver", "en-US");
        content.SetValue("title", "Sieben Dinge über SchemeWeaver", "de-DE");
        content.SetValue("bodyText", "<p>English body text for variant testing.</p>", "en-US");
        content.SetValue("bodyText", "<p>Deutscher Textkörper für Variantentests.</p>", "de-DE");
        _contentService.Save(content);

        try
        {
            var cultureSchedules = new List<CulturePublishScheduleModel>
            {
                new() { Culture = "en-US", Schedule = null },
                new() { Culture = "de-DE", Schedule = null },
            };
            await _contentPublishingService.PublishAsync(
                content.Key, cultureSchedules, Constants.Security.SuperUserKey);
            _logger.LogInformation("TestDataSeeder: Published variant article in en-US + de-DE");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "TestDataSeeder: Could not publish variant article, saved as draft");
        }
    }

    // ──────────────────────────────────────────────────────────────
    // Content creation and publishing
    // ──────────────────────────────────────────────────────────────

    private async Task CreateSampleContent(
        IContentType faqItemType,
        IContentType reviewItemType,
        IContentType recipeIngredientType,
        IContentType recipeStepType,
        IContentType howToStepType,
        IContentType howToToolType,
        IContentType openingHoursType,
        IContentType heroBlockType,
        IContentType featureBlockType,
        IContentType quoteBlockType,
        CancellationToken cancellationToken)
    {
        // Create home page as site root (culture-variant)
        var home = _contentService.Create("SchemeWeaver Demo Site", Constants.System.Root, "homePage");
        home.SetCultureName("SchemeWeaver Demo Site", "en-US");
        home.SetCultureName("SchemeWeaver Demosite", "de-DE");
        home.SetValue("siteName", "SchemeWeaver Demo Site", "en-US");
        home.SetValue("siteName", "SchemeWeaver Demosite", "de-DE");
        home.SetValue("siteDescription", "Comprehensive Schema.org structured data demo — covering 30+ schema types with working JSON-LD output.", "en-US");
        home.SetValue("siteDescription", "Willkommen bei der SchemeWeaver Demonstrationsseite — über 30 Schema.org-Typen mit funktionierender JSON-LD-Ausgabe.", "de-DE");
        home.SetValue("organisationName", "Enjoy Digital");
        home.SetValue("organisationEmail", "hello@enjoy-digital.co.uk");
        home.SetValue("organisationTelephone", "+44 113 357 0000");
        home.SetValue("sameAs", "https://twitter.com/enjoydigital,https://github.com/EnjoyDigital/Umbraco.Community.SchemeWeaver");
        home.SetValue("contentGrid", BuildHomePageContentGrid(heroBlockType, featureBlockType, quoteBlockType));
        await SaveAndPublishVariantAsync(home);

        // ── Top-level pages under home (nav order: Blog, Categories, About Us, FAQs) ──
        var blogListing = CreateAndPublishSimple("Blog", home.Id, "blogListing", "Blog", "Articles about Schema.org, structured data, and SEO.", cancellationToken);
        var categoriesListing = CreateAndPublishSimple("Categories", home.Id, "categoriesListing", "Categories", "Browse all content organised by category.", cancellationToken);
        await CreateAboutPage(home.Id, cancellationToken);
        await CreateFaqContent(faqItemType, home.Id, cancellationToken);

        // ── Category listings under Categories ──
        var categoriesId = await categoriesListing;
        var productListing = CreateAndPublishSimple("Products", categoriesId, "productListing", "Products", "Our software products and tools.", cancellationToken);
        var eventListing = CreateAndPublishSimple("Events", categoriesId, "eventListing", "Events", "Upcoming community events and conferences.", cancellationToken);
        var recipeListing = CreateAndPublishSimple("Recipes", categoriesId, "recipeListing", "Recipes", "Delicious recipes with structured data.", cancellationToken);

        // Product subcategories
        var electronicsCategory = CreateAndPublishSimple("Electronics", await productListing, "productListing", "Electronics", "Electronic devices and gadgets", cancellationToken);
        var softwareCategory = CreateAndPublishSimple("Software", await productListing, "productListing", "Software", "Software products and digital tools", cancellationToken);
        var automotiveCategory = CreateAndPublishSimple("Automotive", await productListing, "productListing", "Automotive", "Vehicles and automotive products", cancellationToken);
        var financeCategory = CreateAndPublishSimple("Finance", await productListing, "productListing", "Finance", "Financial products and services", cancellationToken);

        // Event subcategories
        var musicTheatreCategory = CreateAndPublishSimple("Music & Theatre", await eventListing, "eventListing", "Music & Theatre", "Live music, theatre, and performing arts", cancellationToken);
        var sportsCategory = CreateAndPublishSimple("Sports", await eventListing, "eventListing", "Sports", "Sporting events and matches", cancellationToken);
        var conferencesCategory = CreateAndPublishSimple("Conferences", await eventListing, "eventListing", "Conferences", "Tech conferences, summits, and bootcamps", cancellationToken);
        var foodFestivalsCategory = CreateAndPublishSimple("Food & Festivals", await eventListing, "eventListing", "Food & Festivals", "Food events, festivals, and outdoor celebrations", cancellationToken);

        // Content under listings
        await CreateProductContent(reviewItemType, await electronicsCategory, cancellationToken);
        await CreateSmartWatchContent(await electronicsCategory, cancellationToken);
        await CreateRecipeContent(recipeIngredientType, recipeStepType, await recipeListing, cancellationToken);
        await CreateBlogContent(await blogListing, cancellationToken);
        await CreateEventContent(await conferencesCategory, cancellationToken);

        // Blog content
        await CreateNewsArticle(await blogListing, cancellationToken);
        await CreateTechArticle(await blogListing, cancellationToken);
        await CreateBlogPageContent(await blogListing, cancellationToken);
        await CreateLiveBlogPage(await blogListing, cancellationToken);

        // Product subtypes
        await CreateSoftwarePage(await softwareCategory, cancellationToken);
        await CreateVehiclePage(await automotiveCategory, cancellationToken);
        await CreateFinancialProductPage(await financeCategory, cancellationToken);
        await CreateIndividualProductPage(await electronicsCategory, cancellationToken);
        await CreateProductModelPage(await electronicsCategory, cancellationToken);
        await CreateOfferPageContent(await softwareCategory, cancellationToken);

        // Event subtypes
        await CreateMusicEventPage(await musicTheatreCategory, cancellationToken);
        await CreateSportsEventPage(await sportsCategory, cancellationToken);
        await CreateBusinessEventPage(await conferencesCategory, cancellationToken);
        await CreateFoodEventPage(await foodFestivalsCategory, cancellationToken);
        await CreateFestivalPage(await foodFestivalsCategory, cancellationToken);
        await CreateEducationEventPage(await conferencesCategory, cancellationToken);
        await CreateTheaterEventPage(await musicTheatreCategory, cancellationToken);
        await CreateScreeningEventPage(await musicTheatreCategory, cancellationToken);

        // Organisations listing under Categories
        var organisationListing = CreateAndPublishSimple("Organisations", categoriesId, "organisationListing", "Organisations", "Notable organisations and companies.", cancellationToken);
        await CreateCorporationPage(await organisationListing, cancellationToken);
        await CreateSportsTeamPage(await organisationListing, cancellationToken);
        await CreateAirlinePage(await organisationListing, cancellationToken);
        await CreateNgoPage(await organisationListing, cancellationToken);
        await CreateGovernmentOrgPageContent(await organisationListing, cancellationToken);

        // Places listing under Categories
        var placesListing = CreateAndPublishSimple("Places", categoriesId, "placesListing", "Places", "Interesting places and venues.", cancellationToken);
        await CreateHotelPage(await placesListing, cancellationToken);
        await CreateStorePage(await placesListing, cancellationToken);
        await CreateHospitalPage(await placesListing, cancellationToken);
        await CreateGymPage(await placesListing, cancellationToken);
        await CreateLibraryPageContent(await placesListing, cancellationToken);
        await CreateMovieTheaterPageContent(await placesListing, cancellationToken);
        await CreateNightClubPageContent(await placesListing, cancellationToken);
        await CreateStadiumPageContent(await placesListing, cancellationToken);
        await CreateSkiResortPageContent(await placesListing, cancellationToken);
        await CreateGolfCoursePageContent(await placesListing, cancellationToken);

        // Creative listing under Categories
        var creativeListing = CreateAndPublishSimple("Creative", categoriesId, "creativeListing", "Creative", "Films, music, podcasts, and more.", cancellationToken);
        await CreateMoviePage(await creativeListing, cancellationToken);
        await CreateMusicAlbumPage(await creativeListing, cancellationToken);
        await CreatePodcastEpisodePage(await creativeListing, cancellationToken);
        await CreatePhotographPage(await creativeListing, cancellationToken);
        await CreateDatasetPage(await creativeListing, cancellationToken);
        await CreateReportPage(await creativeListing, cancellationToken);
        await CreateVideoGamePage(await creativeListing, cancellationToken);
        await CreateSourceCodePage(await creativeListing, cancellationToken);
        await CreateArticlePageContent(await creativeListing, cancellationToken);
        await CreatePodcastSeriesPageContent(await creativeListing, cancellationToken);
        await CreateMusicRecordingPageContent(await creativeListing, cancellationToken);

        // Shops listing under Categories
        var shopsListing = CreateAndPublishSimple("Shops", categoriesId, "shopsListing", "Shops", "Local shops and retail stores in Leeds.", cancellationToken);
        await CreateBookStorePage(await shopsListing, cancellationToken);
        await CreateElectronicsStorePage(await shopsListing, cancellationToken);
        await CreateClothingStorePage(await shopsListing, cancellationToken);
        await CreateGroceryStorePage(await shopsListing, cancellationToken);
        await CreateFurnitureStorePage(await shopsListing, cancellationToken);
        await CreateJewelryStorePage(await shopsListing, cancellationToken);

        // Dining listing under Categories
        var diningListing = CreateAndPublishSimple("Dining", categoriesId, "diningListing", "Dining", "Restaurants, cafes, and bars across Leeds and Yorkshire.", cancellationToken);
        await CreateBakeryPage(await diningListing, cancellationToken);
        await CreateCafePage(await diningListing, cancellationToken);
        await CreateBarPage(await diningListing, cancellationToken);
        await CreateFastFoodPage(await diningListing, cancellationToken);
        await CreateWineryPage(await diningListing, cancellationToken);
        await CreateBreweryPage(await diningListing, cancellationToken);

        // Travel listing under Categories
        var travelListing = CreateAndPublishSimple("Travel", categoriesId, "travelListing", "Travel", "Travel reservations, flights, and tourist attractions.", cancellationToken);
        await CreateFlightReservationPage(await travelListing, cancellationToken);
        await CreateLodgingReservationPage(await travelListing, cancellationToken);
        await CreateEventReservationPage(await travelListing, cancellationToken);
        await CreateRentalCarPage(await travelListing, cancellationToken);
        await CreateFlightPage(await travelListing, cancellationToken);
        await CreateTouristAttractionPage(await travelListing, cancellationToken);
        await CreateBedAndBreakfastPage(await travelListing, cancellationToken);
        await CreateCampgroundPage(await travelListing, cancellationToken);

        // Healthcare listing under Categories
        var healthcareListing = CreateAndPublishSimple("Healthcare", categoriesId, "healthcareListing", "Healthcare", "Medical services, conditions, and pharmaceuticals.", cancellationToken);
        await CreateDrugPage(await healthcareListing, cancellationToken);
        await CreateMedicalConditionPage(await healthcareListing, cancellationToken);
        await CreatePhysicianPage(await healthcareListing, cancellationToken);
        await CreatePharmacyPage(await healthcareListing, cancellationToken);
        await CreateDentistPage(await healthcareListing, cancellationToken);
        await CreateMedicalClinicPage(await healthcareListing, cancellationToken);
        await CreateDiagnosticLabPageContent(await healthcareListing, cancellationToken);

        // Automotive listing under Categories
        var automotiveListing = CreateAndPublishSimple("Automotive", categoriesId, "automotiveListing", "Automotive", "Cars, motorcycles, dealers, and repair shops.", cancellationToken);
        await CreateCarPage(await automotiveListing, cancellationToken);
        await CreateMotorcyclePage(await automotiveListing, cancellationToken);
        await CreateAutoDealerPage(await automotiveListing, cancellationToken);
        await CreateAutoRepairPage(await automotiveListing, cancellationToken);

        // Entertainment listing under Categories
        var entertainmentListing = CreateAndPublishSimple("Entertainment", categoriesId, "entertainmentListing", "Entertainment", "Music groups, zoos, museums, and amusement parks.", cancellationToken);
        await CreateMusicGroupPage(await entertainmentListing, cancellationToken);
        await CreateZooPage(await entertainmentListing, cancellationToken);
        await CreateMuseumPage(await entertainmentListing, cancellationToken);
        await CreateAmusementParkPage(await entertainmentListing, cancellationToken);

        // Services listing under Categories
        var servicesListing = CreateAndPublishSimple("Services", categoriesId, "servicesListing", "Services", "Professional services in Leeds and surrounding areas.", cancellationToken);
        await CreateAccountingPage(await servicesListing, cancellationToken);
        await CreateRealEstatePage(await servicesListing, cancellationToken);
        await CreateInsurancePage(await servicesListing, cancellationToken);
        await CreateTravelAgencyPage(await servicesListing, cancellationToken);
        await CreateServicePageContent(await servicesListing, cancellationToken);
        await CreateProfessionalServicePageContent(await servicesListing, cancellationToken);
        await CreateFinancialServicePageContent(await servicesListing, cancellationToken);

        // Legal listing under Categories
        var legalListing = CreateAndPublishSimple("Legal", categoriesId, "legalListing", "Legal", "Legal services, legislation, and judicial institutions.", cancellationToken);
        await CreateAttorneyPage(await legalListing, cancellationToken);
        await CreateLegalServicePageContent(await legalListing, cancellationToken);
        await CreateNotaryPageContent(await legalListing, cancellationToken);
        await CreateCourthousePageContent(await legalListing, cancellationToken);
        await CreateLegislationPageContent(await legalListing, cancellationToken);

        // Education listing under Categories
        var educationListing = CreateAndPublishSimple("Education", categoriesId, "educationListing", "Education", "Universities, schools, and courses.", cancellationToken);
        await CreateUniversityPage(await educationListing, cancellationToken);
        await CreateSchoolPage(await educationListing, cancellationToken);
        await CreateCourseInstancePage(await educationListing, cancellationToken);
        await CreateEducationalOrgPageContent(await educationListing, cancellationToken);

        // Property listing under Categories
        var propertyListing = CreateAndPublishSimple("Property", categoriesId, "propertyListing", "Property", "Residential and commercial property listings across Leeds and Yorkshire.", cancellationToken);
        await CreateApartmentPageContent(await propertyListing, cancellationToken);
        await CreateHousePageContent(await propertyListing, cancellationToken);
        await CreateLodgingBusinessPageContent(await propertyListing, cancellationToken);
        await CreateRealEstateListingPageContent(await propertyListing, cancellationToken);
        await CreateSingleFamilyResidencePageContent(await propertyListing, cancellationToken);
        await CreateApartmentComplexPageContent(await propertyListing, cancellationToken);
        await CreateResidencePageContent(await propertyListing, cancellationToken);
        await CreateSuitePageContent(await propertyListing, cancellationToken);
        await CreateGatedResidenceCommunityPageContent(await propertyListing, cancellationToken);
        await CreateAccommodationPageContent(await propertyListing, cancellationToken);

        // Standalone pages under Categories
        await CreateCoursePage(categoriesId, cancellationToken);
        await CreateHowToPage(howToStepType, howToToolType, categoriesId, cancellationToken);
        await CreateVideoPage(categoriesId, cancellationToken);
        await CreateJobPostingPage(categoriesId, cancellationToken);
        await CreateProfilePage(categoriesId, cancellationToken);
        await CreateLocationPage(openingHoursType, categoriesId, cancellationToken);
        await CreateRestaurantPage(openingHoursType, categoriesId, cancellationToken);
        await CreateBookPage(categoriesId, cancellationToken);
        await CreateContactContent(categoriesId, cancellationToken);
        await CreateMobileAppPage(categoriesId, cancellationToken);
        await CreateWebAppPage(categoriesId, cancellationToken);
        await CreateOccupationPage(categoriesId, cancellationToken);
        await CreateWebPageContent(categoriesId, cancellationToken);

        // Acme Corp hierarchy under Organisations (parent/ancestor/sibling testing)
        var acmeCorp = _contentService.Create("Acme Corporation", await organisationListing, "organisationParent");
        acmeCorp.SetValue("title", "Acme Corporation");
        acmeCorp.SetValue("description", "A multinational conglomerate headquartered in Leeds.");
        acmeCorp.SetValue("telephone", "+44 113 496 0000");
        acmeCorp.SetValue("email", "info@acme-corp.example.com");
        acmeCorp.SetValue("streetAddress", "100 Acme Way");
        acmeCorp.SetValue("addressLocality", "Leeds");
        acmeCorp.SetValue("postalCode", "LS1 1AA");
        acmeCorp.SetValue("addressCountry", "GB");
        _contentService.Save(acmeCorp);
        await PublishContent(acmeCorp, cancellationToken);

        var acmeOffice = _contentService.Create("Acme Leeds Office", acmeCorp.Id, "localBusinessChild");
        acmeOffice.SetValue("title", "Acme Leeds Office");
        acmeOffice.SetValue("description", "The Leeds branch office of Acme Corporation.");
        acmeOffice.SetValue("telephone", "+44 113 496 0001");
        acmeOffice.SetValue("priceRange", "$$");
        acmeOffice.SetValue("streetAddress", "101 Acme Way");
        acmeOffice.SetValue("addressLocality", "Leeds");
        acmeOffice.SetValue("postalCode", "LS1 1AB");
        acmeOffice.SetValue("addressCountry", "GB");
        _contentService.Save(acmeOffice);
        await PublishContent(acmeOffice, cancellationToken);

        var acmeMarketing = _contentService.Create("Acme Marketing Department", acmeCorp.Id, "departmentPage");
        acmeMarketing.SetValue("title", "Acme Marketing Department");
        acmeMarketing.SetValue("description", "The marketing division of Acme Corporation.");
        _contentService.Save(acmeMarketing);
        await PublishContent(acmeMarketing, cancellationToken);

        _logger.LogInformation("TestDataSeeder: created and published sample content nodes");
    }

    private async Task<int> CreateAndPublishSimple(string name, int parentId, string contentTypeAlias, string title, string description, CancellationToken cancellationToken)
    {
        var content = _contentService.Create(name, parentId, contentTypeAlias);
        content.SetValue("title", title);
        content.SetValue("description", description);
        _contentService.Save(content);
        await PublishContent(content, cancellationToken);
        return content.Id;
    }

    private async Task CreateFaqContent(IContentType faqItemType, int parentId, CancellationToken cancellationToken)
    {
        var content = _contentService.Create("FAQs", parentId, "faqPage");
        content.SetCultureName("FAQs", "en-US");
        content.SetCultureName("Häufig gestellte Fragen", "de-DE");
        content.SetValue("title", "FAQs", "en-US");
        content.SetValue("title", "Häufig gestellte Fragen", "de-DE");
        content.SetValue("description", "Common questions about our services", "en-US");
        content.SetValue("description", "Häufige Fragen zu unseren Dienstleistungen", "de-DE");

        var faqItems = BuildBlockListJson(faqItemType.Key, new[]
        {
            new Dictionary<string, object>
            {
                ["question"] = "What is your returns policy?",
                ["answer"] = "You can return any item within 30 days of purchase for a full refund.",
            },
            new Dictionary<string, object>
            {
                ["question"] = "How long does delivery take?",
                ["answer"] = "Standard delivery takes 3-5 working days across the UK.",
            },
            new Dictionary<string, object>
            {
                ["question"] = "Do you offer international shipping?",
                ["answer"] = "Yes, we ship to over 50 countries worldwide.",
            },
        });
        content.SetValue("faqItems", faqItems);

        await SaveAndPublishVariantAsync(content);
    }

    private async Task CreateProductContent(IContentType reviewItemType, int parentId, CancellationToken cancellationToken)
    {
        var content = _contentService.Create("Wireless Headphones Pro", parentId, "productPage");
        content.SetCultureName("Wireless Headphones Pro", "en-US");
        content.SetCultureName("Kabellose Kopfhörer Pro", "de-DE");
        content.SetValue("title", "Wireless Headphones Pro", "en-US");
        content.SetValue("title", "Kabellose Kopfhörer Pro", "de-DE");
        content.SetValue("description", "Premium noise-cancelling wireless headphones", "en-US");
        content.SetValue("description", "Erstklassige kabellose Kopfhörer mit Geräuschunterdrückung", "de-DE");
        content.SetValue("price", "149.99");
        content.SetValue("sku", "WHP-001");
        content.SetValue("brand", "AudioTech");
        content.SetValue("availability", "InStock");
        content.SetValue("currency", "GBP");

        var reviews = BuildBlockListJson(reviewItemType.Key, new[]
        {
            new Dictionary<string, object>
            {
                ["reviewAuthor"] = "Alice Smith",
                ["ratingValue"] = "5",
                ["reviewBody"] = "Best headphones I have ever owned. The noise cancellation is superb.",
                ["reviewDate"] = "2024-01-15",
            },
            new Dictionary<string, object>
            {
                ["reviewAuthor"] = "Bob Jones",
                ["ratingValue"] = "4",
                ["reviewBody"] = "Great sound quality, comfortable fit. Battery life could be better.",
                ["reviewDate"] = "2024-02-20",
            },
        });
        content.SetValue("reviews", reviews);

        // Assign product image using stable basename key (committed seed asset).
        var productImageValue = BuildMediaPickerValue("wireless-headphones-v1");
        if (productImageValue is not null)
            content.SetValue("productImage", productImageValue);
        TrySetHeroImage(content, "wireless-headphones-v1");

        await SaveAndPublishVariantAsync(content);
    }

    private async Task CreateSmartWatchContent(int parentId, CancellationToken cancellationToken)
    {
        var content = _contentService.Create("C64 Syntax Watch", parentId, "productPage");
        content.SetCultureName("C64 Syntax Watch", "en-US");
        content.SetCultureName("C64 Syntax-Uhr", "de-DE");
        content.SetValue("title", "C64 Syntax Watch", "en-US");
        content.SetValue("title", "C64 Syntax-Uhr", "de-DE");
        content.SetValue("description", "A retro-styled smart watch that proudly displays every developer's favourite error message. Hand-assembled, limited run of 64 pieces.", "en-US");
        content.SetValue("description", "Eine Retro-Smartwatch, die stolz die Lieblingsfehlermeldung jedes Entwicklers anzeigt. Handgefertigt, limitierte Auflage von 64 Stück.", "de-DE");
        content.SetValue("price", "129.99");
        content.SetValue("sku", "C64-SW-001");
        content.SetValue("brand", "Commodore Revival");
        content.SetValue("availability", "InStock");
        content.SetValue("currency", "GBP");

        var productImageValue = BuildMediaPickerValue("smart-watch");
        if (productImageValue is not null)
            content.SetValue("productImage", productImageValue);
        TrySetHeroImage(content, "smart-watch");

        await SaveAndPublishVariantAsync(content);
    }

    private async Task CreateRecipeContent(
        IContentType recipeIngredientType,
        IContentType recipeStepType,
        int parentId,
        CancellationToken cancellationToken)
    {
        var content = _contentService.Create("Classic Victoria Sponge", parentId, "recipePage");
        content.SetCultureName("Classic Victoria Sponge", "en-US");
        content.SetCultureName("Omas Apfelkuchen", "de-DE");
        content.SetValue("title", "Classic Victoria Sponge", "en-US");
        content.SetValue("title", "Omas Apfelkuchen", "de-DE");
        content.SetValue("description", "A traditional British cake recipe", "en-US");
        content.SetValue("description", "Ein traditionelles deutsches Apfelkuchenrezept nach Großmutters Art", "de-DE");
        content.SetValue("authorName", "Mary Berry");
        content.SetValue("prepTime", "PT20M");
        content.SetValue("cookTime", "PT25M");
        content.SetValue("totalTime", "PT45M");
        content.SetValue("recipeYield", "Serves 8");
        content.SetValue("calories", "350 kcal");
        content.SetValue("recipeCategory", "Dessert");
        content.SetValue("recipeCuisine", "British");

        var ingredients = BuildBlockListJson(recipeIngredientType.Key, new[]
        {
            new Dictionary<string, object> { ["ingredient"] = "200g self-raising flour" },
            new Dictionary<string, object> { ["ingredient"] = "200g caster sugar" },
            new Dictionary<string, object> { ["ingredient"] = "200g softened butter" },
            new Dictionary<string, object> { ["ingredient"] = "4 large eggs" },
        });
        content.SetValue("ingredients", ingredients);

        var instructions = BuildBlockListJson(recipeStepType.Key, new[]
        {
            new Dictionary<string, object>
            {
                ["stepName"] = "Prepare",
                ["stepText"] = "Preheat oven to 180C/160C fan/gas 4. Grease and line two 20cm sandwich tins.",
            },
            new Dictionary<string, object>
            {
                ["stepName"] = "Mix",
                ["stepText"] = "Beat together the butter and sugar until pale and fluffy. Add eggs one at a time, folding in flour.",
            },
            new Dictionary<string, object>
            {
                ["stepName"] = "Bake",
                ["stepText"] = "Divide between the tins and bake for 20-25 minutes until golden and springy to touch.",
            },
        });
        content.SetValue("instructions", instructions);

        TrySetHeroImage(content, "classic-victoria-sponge");

        await SaveAndPublishVariantAsync(content);
    }

    private async Task CreateBlogContent(int parentId, CancellationToken cancellationToken)
    {
        var content = _contentService.Create("Getting Started with Schema.org", parentId, "blogArticle");
        content.SetCultureName("Getting Started with Schema.org", "en-US");
        content.SetCultureName("Zehn Tipps für besseres SEO", "de-DE");
        content.SetValue("title", "Getting Started with Schema.org", "en-US");
        content.SetValue("title", "Zehn Tipps für besseres SEO", "de-DE");
        content.SetValue("description", "Learn how structured data helps search engines understand your content.", "en-US");
        content.SetValue("description", "Erfahren Sie, wie strukturierte Daten Suchmaschinen helfen, Ihre Inhalte zu verstehen.", "de-DE");
        content.SetValue("bodyText", "<p>Schema.org provides a shared vocabulary for structured data markup on web pages.</p>", "en-US");
        content.SetValue("bodyText", "<p>Schema.org bietet ein gemeinsames Vokabular für strukturierte Datenauszeichnung auf Webseiten. Strukturierte Daten helfen Suchmaschinen, den Inhalt Ihrer Seiten besser zu verstehen und reichhaltigere Suchergebnisse anzuzeigen.</p>", "de-DE");
        content.SetValue("authorName", "Oliver");
        content.SetValue("publishDate", DateTime.Now);
        content.SetValue("keywords", "schema.org, structured data, SEO");
        content.SetValue("category", "Technology");

        TrySetHeroImage(content, "getting-started-with-schema-org");

        await SaveAndPublishVariantAsync(content);
    }

    private async Task CreateEventContent(int parentId, CancellationToken cancellationToken)
    {
        var content = _contentService.Create("Umbraco UK Festival 2026", parentId, "eventPage");
        content.SetCultureName("Umbraco UK Festival 2026", "en-US");
        content.SetCultureName("Technologiekonferenz 2025", "de-DE");
        content.SetValue("title", "Umbraco UK Festival 2026", "en-US");
        content.SetValue("title", "Technologiekonferenz 2025", "de-DE");
        content.SetValue("description", "The premier Umbraco community event in the UK", "en-US");
        content.SetValue("description", "Die führende Technologiekonferenz für Webentwickler in Europa", "de-DE");
        content.SetValue("startDate", DateTime.Now.AddMonths(2));
        content.SetValue("endDate", DateTime.Now.AddMonths(2).AddDays(1));
        content.SetValue("locationName", "etc.venues");
        content.SetValue("locationAddress", "155 Bishopsgate, London EC2M 3YD");
        content.SetValue("organiserName", "Umbraco Community");
        content.SetValue("ticketPrice", "199.00");
        content.SetValue("ticketUrl", "https://umbracofestival.co.uk/tickets");

        TrySetHeroImage(content, "umbraco-uk-festival-2026");

        await SaveAndPublishVariantAsync(content);
    }

    /// <summary>
    /// Builds the BlockGrid value applied to the home page's <c>contentGrid</c> property.
    /// Features two feature blocks + one testimonial quote, plus a hero block at the top
    /// — exercises the BlockGrid resolver (for Schema.org WebSite.mainEntity) and the
    /// nested ImageObject ranking UX when editors map the blocks' images.
    /// </summary>
    private string BuildHomePageContentGrid(
        IContentType heroBlockType,
        IContentType featureBlockType,
        IContentType quoteBlockType)
    {
        var heroBlockImage = BuildMediaPickerValue("getting-started-with-schema-org");
        var featureImageA = BuildMediaPickerValue("about-this-site");
        var featureImageB = BuildMediaPickerValue("breaking-news-schema-org-v26-released");

        var heroValues = new Dictionary<string, object>
        {
            ["title"] = "Map. Weave. Ship.",
            ["subtitle"] = "Schema.org structured data, bolted onto Umbraco with zero fuss.",
        };
        if (heroBlockImage is not null)
            heroValues["heroImage"] = heroBlockImage;

        var feature1Values = new Dictionary<string, object>
        {
            ["title"] = "One-click mappings",
            ["description"] = "<p>Auto-map any content type to a Schema.org type — the registry knows over 800 of them.</p>",
        };
        if (featureImageA is not null)
            feature1Values["featureImage"] = featureImageA;

        var feature2Values = new Dictionary<string, object>
        {
            ["title"] = "Live JSON-LD preview",
            ["description"] = "<p>Inspect the generated JSON-LD for any content item, validated against the published spec.</p>",
        };
        if (featureImageB is not null)
            feature2Values["featureImage"] = featureImageB;

        var quoteValues = new Dictionary<string, object>
        {
            ["quote"] = "SchemeWeaver turned structured data from a chore into a feature we actually ship.",
            ["attribution"] = "A very satisfied Umbraco developer",
        };

        return BuildBlockGridJson(new (Guid, Dictionary<string, object>)[]
        {
            (heroBlockType.Key, heroValues),
            (featureBlockType.Key, feature1Values),
            (featureBlockType.Key, feature2Values),
            (quoteBlockType.Key, quoteValues),
        });
    }

    private async Task SaveAndPublishAsync(IContent content)
    {
        _contentService.Save(content);

        try
        {
            var cultureSchedules = new List<CulturePublishScheduleModel>
            {
                new() { Culture = null, Schedule = null },
            };

            var result = await _contentPublishingService.PublishAsync(
                content.Key,
                cultureSchedules,
                Constants.Security.SuperUserKey);

            _logger.LogInformation("TestDataSeeder: Published {Name} — result: {Result}",
                content.Name, result.GetType().Name);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "TestDataSeeder: Could not publish {Name}, saved as draft", content.Name);
        }
    }

    /// <summary>
    /// Saves and publishes content in both en-US and de-DE cultures.
    /// Used for the 7 key variant content types.
    /// </summary>
    private async Task SaveAndPublishVariantAsync(IContent content)
    {
        _contentService.Save(content);

        try
        {
            var cultureSchedules = new List<CulturePublishScheduleModel>
            {
                new() { Culture = "en-US", Schedule = null },
                new() { Culture = "de-DE", Schedule = null },
            };

            var result = await _contentPublishingService.PublishAsync(
                content.Key,
                cultureSchedules,
                Constants.Security.SuperUserKey);

            _logger.LogInformation("TestDataSeeder: Published {Name} in en-US + de-DE — result: {Result}",
                content.Name, result.GetType().Name);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "TestDataSeeder: Could not publish variant {Name}, saved as draft", content.Name);
        }
    }

    // ──────────────────────────────────────────────────────────────
    // New content creation methods (expanded demo)
    // ──────────────────────────────────────────────────────────────

    private async Task CreateNewsArticle(int parentId, CancellationToken ct)
    {
        var content = _contentService.Create("Breaking News: Schema.org v26 Released", parentId, "newsArticle");
        content.SetValue("title", "Breaking News: Schema.org v26 Released");
        content.SetValue("description", "The latest version of Schema.org brings exciting new types and properties for structured data.");
        content.SetValue("bodyText", "<p>Schema.org version 26 has been released, introducing several new types and properties that improve structured data coverage for modern web applications.</p>");
        content.SetValue("authorName", "Oliver Picton");
        content.SetValue("publishDate", DateTime.Now.AddDays(-3));
        content.SetValue("keywords", "schema.org, structured data, release, news");
        content.SetValue("dateline", "London, UK");
        TrySetHeroImage(content, "breaking-news-schema-org-v26-released");
        await SaveAndPublishAsync(content);
    }

    private async Task CreateTechArticle(int parentId, CancellationToken ct)
    {
        var content = _contentService.Create("Building Lit Web Components for Umbraco", parentId, "techArticle");
        content.SetValue("title", "Building Lit Web Components for Umbraco");
        content.SetValue("description", "A technical guide to building Umbraco backoffice extensions with Lit web components.");
        content.SetValue("bodyText", "<p>Umbraco's new backoffice is built on Lit web components. This guide shows you how to create custom property editors, dashboards, and workspace views.</p>");
        content.SetValue("authorName", "Oliver Picton");
        content.SetValue("publishDate", DateTime.Now.AddDays(-7));
        content.SetValue("proficiencyLevel", "Advanced");
        content.SetValue("dependencies", "Lit 3.x, TypeScript 5.x, Umbraco 17+");
        TrySetHeroImage(content, "building-lit-web-components-for-umbraco");
        await SaveAndPublishAsync(content);
    }

    private async Task CreateSoftwarePage(int parentId, CancellationToken ct)
    {
        var content = _contentService.Create("SchemeWeaver Plugin v1.0", parentId, "softwarePage");
        content.SetValue("title", "SchemeWeaver Plugin v1.0");
        content.SetValue("description", "Map your Umbraco content types to Schema.org for better SEO with automatic JSON-LD generation.");
        content.SetValue("bodyText", "<p>SchemeWeaver automatically generates JSON-LD structured data from your Umbraco content, improving your site's visibility in search results.</p>");
        content.SetValue("applicationCategory", "DeveloperApplication");
        content.SetValue("operatingSystem", "Cross-platform");
        content.SetValue("softwareVersion", "1.0.0");
        content.SetValue("downloadUrl", "https://www.nuget.org/packages/Umbraco.Community.SchemeWeaver");
        content.SetValue("price", "0");
        content.SetValue("currency", "GBP");
        TrySetHeroImage(content, "schemeweaver-software");
        await SaveAndPublishAsync(content);
    }

    private async Task CreateCoursePage(int parentId, CancellationToken ct)
    {
        var content = _contentService.Create("Web Development Masterclass", parentId, "coursePage");
        content.SetValue("title", "Web Development Masterclass");
        content.SetValue("description", "Learn modern web development with Umbraco CMS and .NET in this comprehensive course.");
        content.SetValue("bodyText", "<p>This masterclass covers everything from setting up your first Umbraco project to building custom backoffice extensions and deploying to production.</p>");
        content.SetValue("courseCode", "WDM-2026");
        content.SetValue("providerName", "Enjoy Digital");
        content.SetValue("duration", "PT40H");
        content.SetValue("price", "499.00");
        content.SetValue("currency", "GBP");
        content.SetValue("startDate", DateTime.Now.AddMonths(1));
        TrySetHeroImage(content, "web-development-masterclass");
        await SaveAndPublishAsync(content);
    }

    private async Task CreateHowToPage(IContentType howToStepType, IContentType howToToolType, int parentId, CancellationToken ct)
    {
        var content = _contentService.Create("How to Set Up Schema.org Markup", parentId, "howToPage");
        content.SetValue("title", "How to Set Up Schema.org Markup");
        content.SetValue("description", "Step-by-step guide to adding structured data to your Umbraco site using SchemeWeaver.");
        content.SetValue("totalTime", "PT30M");
        content.SetValue("estimatedCost", "Free");

        var steps = BuildBlockListJson(howToStepType.Key, new[]
        {
            new Dictionary<string, object> { ["stepName"] = "Install SchemeWeaver", ["stepText"] = "Install the SchemeWeaver NuGet package into your Umbraco project." },
            new Dictionary<string, object> { ["stepName"] = "Configure Content Types", ["stepText"] = "Open the document type editor and navigate to the Schema.org tab." },
            new Dictionary<string, object> { ["stepName"] = "Map Properties", ["stepText"] = "Use auto-map to match your content properties to Schema.org properties." },
            new Dictionary<string, object> { ["stepName"] = "Test Output", ["stepText"] = "Preview the JSON-LD output and validate with Google Rich Results Test." },
        });
        content.SetValue("howToSteps", steps);

        var tools = BuildBlockListJson(howToToolType.Key, new[]
        {
            new Dictionary<string, object> { ["toolName"] = "Browser Developer Tools" },
            new Dictionary<string, object> { ["toolName"] = "Google Rich Results Test" },
        });
        content.SetValue("howToTools", tools);

        TrySetHeroImage(content, "how-to-set-up-schema-org-markup");

        await SaveAndPublishAsync(content);
    }

    private async Task CreateVideoPage(int parentId, CancellationToken ct)
    {
        var content = _contentService.Create("Introduction to Structured Data", parentId, "videoPage");
        content.SetValue("title", "Introduction to Structured Data");
        content.SetValue("description", "A beginner-friendly video explaining structured data and how Schema.org helps search engines.");
        content.SetValue("thumbnailUrl", "https://example.com/thumbnails/intro-structured-data.jpg");
        content.SetValue("uploadDate", DateTime.Now.AddDays(-14));
        content.SetValue("duration", "PT12M30S");
        content.SetValue("contentUrl", "https://example.com/videos/intro-structured-data.mp4");
        content.SetValue("embedUrl", "https://www.youtube.com/embed/dQw4w9WgXcQ");
        TrySetHeroImage(content, "introduction-to-structured-data");
        await SaveAndPublishAsync(content);
    }

    private async Task CreateJobPostingPage(int parentId, CancellationToken ct)
    {
        var content = _contentService.Create("Senior .NET Developer", parentId, "jobPostingPage");
        content.SetValue("title", "Senior .NET Developer");
        content.SetValue("description", "Join our team building Umbraco community packages and delivering enterprise CMS solutions.");
        content.SetValue("bodyText", "<p>We are looking for an experienced .NET developer to join our growing team. You will work on Umbraco CMS projects, build NuGet packages, and contribute to the open-source community.</p>");
        content.SetValue("datePosted", DateTime.Now.AddDays(-5));
        content.SetValue("validThrough", DateTime.Now.AddMonths(1));
        content.SetValue("employmentType", "FULL_TIME");
        content.SetValue("hiringOrganisation", "Enjoy Digital");
        content.SetValue("salary", "60000-80000 GBP per year");
        content.SetValue("jobLocationName", "Leeds, UK");
        content.SetValue("jobLocationAddress", "7 Park Row, Leeds LS1 5HD");
        content.SetValue("qualifications", ".NET 8+\nUmbraco CMS\nTypeScript\nREST APIs\nSQL Server");
        TrySetHeroImage(content, "senior-net-developer");
        await SaveAndPublishAsync(content);
    }

    private async Task CreateProfilePage(int parentId, CancellationToken ct)
    {
        var content = _contentService.Create("Oliver Picton", parentId, "profilePage");
        content.SetValue("title", "Oliver Picton");
        content.SetValue("description", "Software engineer and Umbraco community contributor specialising in structured data and SEO.");
        content.SetValue("givenName", "Oliver");
        content.SetValue("familyName", "Picton");
        content.SetValue("jobTitle", "Software Engineer");
        content.SetValue("email", "oliver@enjoy-digital.co.uk");
        content.SetValue("worksFor", "Enjoy Digital");
        content.SetValue("sameAs", "https://github.com/EnjoyDigital/Umbraco.Community.SchemeWeaver,https://twitter.com/oliverpicton");
        TrySetHeroImage(content, "oliver-picton");
        await SaveAndPublishAsync(content);
    }

    private async Task CreateLocationPage(IContentType openingHoursType, int parentId, CancellationToken ct)
    {
        var content = _contentService.Create("Enjoy Digital", parentId, "locationPage");
        content.SetValue("title", "Enjoy Digital");
        content.SetValue("description", "Digital agency specialising in Umbraco CMS development and structured data solutions.");
        content.SetValue("telephone", "+44 113 357 0000");
        content.SetValue("email", "hello@enjoy-digital.co.uk");
        content.SetValue("streetAddress", "7 Park Row");
        content.SetValue("addressLocality", "Leeds");
        content.SetValue("addressRegion", "West Yorkshire");
        content.SetValue("postalCode", "LS1 5HD");
        content.SetValue("addressCountry", "GB");
        content.SetValue("latitude", "53.7997");
        content.SetValue("longitude", "-1.5492");
        content.SetValue("priceRange", "$$");

        var hours = BuildBlockListJson(openingHoursType.Key, new[]
        {
            new Dictionary<string, object> { ["dayOfWeek"] = "Monday", ["opens"] = "09:00", ["closes"] = "17:30" },
            new Dictionary<string, object> { ["dayOfWeek"] = "Tuesday", ["opens"] = "09:00", ["closes"] = "17:30" },
            new Dictionary<string, object> { ["dayOfWeek"] = "Wednesday", ["opens"] = "09:00", ["closes"] = "17:30" },
            new Dictionary<string, object> { ["dayOfWeek"] = "Thursday", ["opens"] = "09:00", ["closes"] = "17:30" },
            new Dictionary<string, object> { ["dayOfWeek"] = "Friday", ["opens"] = "09:00", ["closes"] = "17:00" },
        });
        content.SetValue("openingHours", hours);

        TrySetHeroImage(content, "location-enjoy-digital");

        await SaveAndPublishAsync(content);
    }

    private async Task CreateRestaurantPage(IContentType openingHoursType, int parentId, CancellationToken ct)
    {
        var content = _contentService.Create("The Olive Kitchen", parentId, "restaurantPage");
        content.SetValue("title", "The Olive Kitchen");
        content.SetValue("description", "Mediterranean and Italian cuisine in the heart of Leeds city centre.");
        content.SetValue("telephone", "+44 113 245 1234");
        content.SetValue("email", "info@olivekitchen.co.uk");
        content.SetValue("streetAddress", "42 The Headrow");
        content.SetValue("addressLocality", "Leeds");
        content.SetValue("postalCode", "LS1 8EQ");
        content.SetValue("addressCountry", "GB");
        content.SetValue("latitude", "53.7992");
        content.SetValue("longitude", "-1.5434");
        content.SetValue("priceRange", "$$$");
        content.SetValue("servesCuisine", "Mediterranean, Italian");
        content.SetValue("menu", "https://olivekitchen.co.uk/menu");
        content.SetValue("acceptsReservations", "True");

        var hours = BuildBlockListJson(openingHoursType.Key, new[]
        {
            new Dictionary<string, object> { ["dayOfWeek"] = "Monday", ["opens"] = "12:00", ["closes"] = "22:00" },
            new Dictionary<string, object> { ["dayOfWeek"] = "Tuesday", ["opens"] = "12:00", ["closes"] = "22:00" },
            new Dictionary<string, object> { ["dayOfWeek"] = "Wednesday", ["opens"] = "12:00", ["closes"] = "22:00" },
            new Dictionary<string, object> { ["dayOfWeek"] = "Thursday", ["opens"] = "12:00", ["closes"] = "23:00" },
            new Dictionary<string, object> { ["dayOfWeek"] = "Friday", ["opens"] = "12:00", ["closes"] = "23:00" },
            new Dictionary<string, object> { ["dayOfWeek"] = "Saturday", ["opens"] = "11:00", ["closes"] = "23:00" },
            new Dictionary<string, object> { ["dayOfWeek"] = "Sunday", ["opens"] = "11:00", ["closes"] = "21:00" },
        });
        content.SetValue("openingHours", hours);

        TrySetHeroImage(content, "the-olive-kitchen");

        await SaveAndPublishAsync(content);
    }

    private async Task CreateBookPage(int parentId, CancellationToken ct)
    {
        var content = _contentService.Create("Umbraco in Practice", parentId, "bookPage");
        content.SetValue("title", "Umbraco in Practice");
        content.SetValue("description", "A comprehensive guide to building websites with Umbraco CMS — from setup to deployment.");
        content.SetValue("bodyText", "<p>This book covers everything you need to know to build professional websites with Umbraco CMS, including content modelling, custom property editors, and structured data.</p>");
        content.SetValue("authorName", "Oliver Picton");
        content.SetValue("isbn", "978-1-4842-9876-5");
        content.SetValue("bookFormat", "Paperback");
        content.SetValue("numberOfPages", "320");
        content.SetValue("publisher", "Apress");
        content.SetValue("datePublished", DateTime.Now.AddMonths(-6));
        content.SetValue("price", "39.99");
        content.SetValue("currency", "GBP");
        TrySetHeroImage(content, "umbraco-in-practice");
        await SaveAndPublishAsync(content);
    }

    private async Task CreateAboutPage(int parentId, CancellationToken ct)
    {
        var content = _contentService.Create("About Us", parentId, "aboutPage");
        content.SetCultureName("About Us", "en-US");
        content.SetCultureName("Über uns", "de-DE");
        content.SetValue("title", "About Enjoy Digital", "en-US");
        content.SetValue("title", "Über uns", "de-DE");
        content.SetValue("description", "We are a digital agency specialising in Umbraco CMS development and structured data solutions.");
        content.SetValue("bodyText", "<p>Enjoy Digital has been building exceptional digital experiences since 2006. We specialise in Umbraco CMS, .NET development, and helping organisations improve their search visibility through structured data.</p>", "en-US");
        content.SetValue("bodyText", "<p>Wir sind ein engagiertes Team von Webentwicklern, das sich auf Umbraco CMS und strukturierte Daten spezialisiert hat. Seit 2006 erstellen wir außergewöhnliche digitale Erlebnisse für unsere Kunden.</p>", "de-DE");
        content.SetValue("organisationName", "Enjoy Digital");
        content.SetValue("foundingDate", "2006");
        content.SetValue("numberOfEmployees", "50");
        await SaveAndPublishVariantAsync(content);
    }

    private async Task CreateContactContent(int parentId, CancellationToken ct)
    {
        var content = _contentService.Create("Contact Us", parentId, "contactPage");
        content.SetValue("title", "Contact Us");
        content.SetValue("telephone", "+44 113 357 0000");
        content.SetValue("email", "hello@enjoy-digital.co.uk");
        content.SetValue("streetAddress", "7 Park Row");
        content.SetValue("addressLocality", "Leeds");
        content.SetValue("postalCode", "LS1 5HD");
        content.SetValue("openingHours", "Monday-Friday 09:00-17:30");
        await SaveAndPublishAsync(content);
    }

    // ──────────────────────────────────────────────────────────────
    // New subtype content creation methods
    // ──────────────────────────────────────────────────────────────

    private async Task CreateVehiclePage(int parentId, CancellationToken ct)
    {
        var content = _contentService.Create("Family Saloon 2024", parentId, "vehiclePage");
        content.SetValue("title", "Family Saloon 2024");
        content.SetValue("description", "A reliable and efficient family car with modern features and excellent fuel economy.");
        content.SetValue("brand", "Ford");
        content.SetValue("model", "Focus");
        content.SetValue("fuelType", "Petrol");
        content.SetValue("mileageFromOdometer", "15000 miles");
        content.SetValue("vehicleEngine", "1.5L EcoBoost");
        content.SetValue("color", "Magnetic Grey");
        content.SetValue("numberOfDoors", "5");
        content.SetValue("bodyText", "<p>This well-maintained Ford Focus is perfect for families. Low mileage, full service history, and one previous owner. MOT until March 2027.</p>");
        TrySetHeroImage(content, "family-saloon-2024");
        await SaveAndPublishAsync(content);
    }

    private async Task CreateFinancialProductPage(int parentId, CancellationToken ct)
    {
        var content = _contentService.Create("Premium Savings Account", parentId, "financialProductPage");
        content.SetValue("title", "Premium Savings Account");
        content.SetValue("description", "A competitive fixed-rate savings account with guaranteed returns for 12 months.");
        content.SetValue("feesAndCommissionsSpecification", "No monthly fees. Early withdrawal penalty of 90 days' interest.");
        content.SetValue("interestRate", "4.5");
        content.SetValue("annualPercentageRate", "4.5% AER");
        content.SetValue("bodyText", "<p>Our Premium Savings Account offers one of the best rates on the high street. Minimum deposit of £1,000 required. FSCS protected up to £85,000.</p>");
        TrySetHeroImage(content, "premium-savings-account");
        await SaveAndPublishAsync(content);
    }

    private async Task CreateIndividualProductPage(int parentId, CancellationToken ct)
    {
        var content = _contentService.Create("Limited Edition Watch", parentId, "individualProductPage");
        content.SetValue("title", "Limited Edition Watch");
        content.SetValue("description", "A hand-crafted timepiece from our exclusive Leeds collection, number 42 of 100.");
        content.SetValue("serialNumber", "LDS-2024-042");
        content.SetValue("sku", "WATCH-LE-042");
        content.SetValue("color", "Rose Gold");
        content.SetValue("weight", "85g");
        content.SetValue("bodyText", "<p>Each watch in this limited run is individually numbered and comes with a certificate of authenticity. Swiss movement, sapphire crystal glass, and genuine leather strap.</p>");
        TrySetHeroImage(content, "limited-edition-watch");
        await SaveAndPublishAsync(content);
    }

    private async Task CreateProductModelPage(int parentId, CancellationToken ct)
    {
        var content = _contentService.Create("Headphones Pro v2", parentId, "productModelPage");
        content.SetValue("title", "Headphones Pro v2");
        content.SetValue("description", "The second generation of our award-winning noise-cancelling headphones with improved battery life and spatial audio.");
        content.SetValue("bodyText", "<p>Building on the success of the original Headphones Pro, the v2 features 40-hour battery life, adaptive noise cancellation, and support for spatial audio. Available in Midnight Black, Arctic White, and Leeds Blue.</p>");
        TrySetHeroImage(content, "wireless-headphones-v2");
        await SaveAndPublishAsync(content);
    }

    private async Task CreateMusicEventPage(int parentId, CancellationToken ct)
    {
        var content = _contentService.Create("Jazz Night at Leeds", parentId, "musicEventPage");
        content.SetValue("title", "Jazz Night at Leeds");
        content.SetValue("description", "An evening of live jazz featuring local and international artists at the Howard Assembly Room.");
        content.SetValue("performer", "The Leeds Jazz Quartet");
        content.SetValue("startDate", new DateTime(2026, 6, 15, 19, 30, 0));
        content.SetValue("endDate", new DateTime(2026, 6, 15, 23, 0, 0));
        content.SetValue("locationName", "Howard Assembly Room");
        content.SetValue("locationAddress", "46 New Briggate, Leeds LS1 6NU");
        TrySetHeroImage(content, "jazz-night-at-leeds");
        await SaveAndPublishAsync(content);
    }

    private async Task CreateSportsEventPage(int parentId, CancellationToken ct)
    {
        var content = _contentService.Create("Leeds United vs Manchester City", parentId, "sportsEventPage");
        content.SetValue("title", "Leeds United vs Manchester City");
        content.SetValue("description", "Premier League fixture at Elland Road — an exciting clash between Yorkshire grit and Manchester flair.");
        content.SetValue("competitor", "Manchester City");
        content.SetValue("startDate", new DateTime(2026, 9, 20, 15, 0, 0));
        content.SetValue("locationName", "Elland Road");
        content.SetValue("sport", "Football");
        TrySetHeroImage(content, "event-leeds-utd-vs-city");
        await SaveAndPublishAsync(content);
    }

    private async Task CreateBusinessEventPage(int parentId, CancellationToken ct)
    {
        var content = _contentService.Create("Digital Summit 2026", parentId, "businessEventPage");
        content.SetValue("title", "Digital Summit 2026");
        content.SetValue("description", "Yorkshire's premier digital conference bringing together industry leaders, developers, and designers.");
        content.SetValue("organiserName", "Enjoy Digital");
        content.SetValue("startDate", new DateTime(2026, 10, 8, 9, 0, 0));
        content.SetValue("locationName", "Leeds Dock");
        content.SetValue("sponsor", "Umbraco HQ");
        TrySetHeroImage(content, "digital-summit-2026");
        await SaveAndPublishAsync(content);
    }

    private async Task CreateFoodEventPage(int parentId, CancellationToken ct)
    {
        var content = _contentService.Create("Yorkshire Food Fest", parentId, "foodEventPage");
        content.SetValue("title", "Yorkshire Food Fest");
        content.SetValue("description", "A celebration of Yorkshire's finest produce, street food, and craft beverages in Roundhay Park.");
        content.SetValue("startDate", new DateTime(2026, 7, 12, 10, 0, 0));
        content.SetValue("locationName", "Roundhay Park, Leeds");
        TrySetHeroImage(content, "yorkshire-food-fest");
        await SaveAndPublishAsync(content);
    }

    private async Task CreateFestivalPage(int parentId, CancellationToken ct)
    {
        var content = _contentService.Create("Headingley Festival", parentId, "festivalPage");
        content.SetValue("title", "Headingley Festival");
        content.SetValue("description", "A weekend of music, comedy, and arts in the heart of Headingley — Leeds' favourite summer gathering.");
        content.SetValue("startDate", new DateTime(2026, 8, 1, 12, 0, 0));
        content.SetValue("endDate", new DateTime(2026, 8, 3, 23, 0, 0));
        content.SetValue("locationName", "Headingley Stadium, Leeds");
        TrySetHeroImage(content, "headingley-festival");
        await SaveAndPublishAsync(content);
    }

    private async Task CreateEducationEventPage(int parentId, CancellationToken ct)
    {
        var content = _contentService.Create("Coding Bootcamp", parentId, "educationEventPage");
        content.SetValue("title", "Coding Bootcamp");
        content.SetValue("description", "An intensive weekend bootcamp covering .NET, TypeScript, and modern CMS development with Umbraco.");
        content.SetValue("startDate", new DateTime(2026, 5, 17, 9, 0, 0));
        content.SetValue("locationName", "Platform, Leeds");
        TrySetHeroImage(content, "coding-bootcamp");
        await SaveAndPublishAsync(content);
    }

    private async Task CreateCorporationPage(int parentId, CancellationToken ct)
    {
        var content = _contentService.Create("TechCorp plc", parentId, "corporationPage");
        content.SetValue("title", "TechCorp plc");
        content.SetValue("description", "A FTSE-listed technology company headquartered in Leeds, specialising in enterprise software solutions.");
        content.SetValue("tickerSymbol", "TCH");
        content.SetValue("legalName", "TechCorp Public Limited Company");
        content.SetValue("foundingDate", "2008");
        content.SetValue("numberOfEmployees", "1200");
        TrySetHeroImage(content, "techcorp-plc");
        await SaveAndPublishAsync(content);
    }

    private async Task CreateSportsTeamPage(int parentId, CancellationToken ct)
    {
        var content = _contentService.Create("Leeds Rhinos", parentId, "sportsTeamPage");
        content.SetValue("title", "Leeds Rhinos");
        content.SetValue("description", "Professional rugby league club based at Headingley Stadium, one of the most successful teams in Super League history.");
        content.SetValue("sport", "Rugby League");
        content.SetValue("coach", "Rohan Smith");
        TrySetHeroImage(content, "leeds-rhinos");
        await SaveAndPublishAsync(content);
    }

    private async Task CreateAirlinePage(int parentId, CancellationToken ct)
    {
        var content = _contentService.Create("FlyBe Airways", parentId, "airlinePage");
        content.SetValue("title", "FlyBe Airways");
        content.SetValue("description", "Regional airline operating domestic and European routes from Leeds Bradford Airport.");
        content.SetValue("iataCode", "BE");
        content.SetValue("foundingDate", "1979");
        TrySetHeroImage(content, "flybe-airways");
        await SaveAndPublishAsync(content);
    }

    private async Task CreateNgoPage(int parentId, CancellationToken ct)
    {
        var content = _contentService.Create("Code for Good", parentId, "ngoPage");
        content.SetValue("title", "Code for Good");
        content.SetValue("description", "A non-profit organisation teaching coding skills to disadvantaged young people across West Yorkshire.");
        content.SetValue("foundingDate", "2019");
        content.SetValue("areaServed", "West Yorkshire, United Kingdom");
        TrySetHeroImage(content, "code-for-good");
        await SaveAndPublishAsync(content);
    }

    private async Task CreateHotelPage(int parentId, CancellationToken ct)
    {
        var content = _contentService.Create("Grand Hotel Leeds", parentId, "hotelPage");
        content.SetValue("title", "Grand Hotel Leeds");
        content.SetValue("description", "A luxury 4-star hotel in the heart of Leeds city centre, housed in a stunning Grade II listed building.");
        content.SetValue("telephone", "+44 113 380 0100");
        content.SetValue("streetAddress", "Wellington Street");
        content.SetValue("addressLocality", "Leeds");
        content.SetValue("postalCode", "LS1 4DL");
        content.SetValue("starRating", "4");
        content.SetValue("checkinTime", "15:00");
        content.SetValue("checkoutTime", "11:00");
        TrySetHeroImage(content, "grand-hotel-leeds");
        await SaveAndPublishAsync(content);
    }

    private async Task CreateStorePage(int parentId, CancellationToken ct)
    {
        var content = _contentService.Create("Digital Store Leeds", parentId, "storePage");
        content.SetValue("title", "Digital Store Leeds");
        content.SetValue("description", "Your local technology and electronics store in the Victoria Quarter, Leeds.");
        content.SetValue("telephone", "+44 113 245 9900");
        content.SetValue("streetAddress", "12 Queen Victoria Street");
        content.SetValue("addressLocality", "Leeds");
        content.SetValue("postalCode", "LS1 6AZ");
        content.SetValue("paymentAccepted", "Cash, Credit Card, Apple Pay, Google Pay");
        TrySetHeroImage(content, "digital-store-leeds");
        await SaveAndPublishAsync(content);
    }

    private async Task CreateHospitalPage(int parentId, CancellationToken ct)
    {
        var content = _contentService.Create("Leeds General Infirmary", parentId, "hospitalPage");
        content.SetValue("title", "Leeds General Infirmary");
        content.SetValue("description", "One of the largest teaching hospitals in Europe, providing specialist care and emergency services.");
        content.SetValue("telephone", "+44 113 243 2799");
        content.SetValue("streetAddress", "Great George Street");
        content.SetValue("addressLocality", "Leeds");
        content.SetValue("medicalSpecialty", "Cardiology, Oncology, Neurology, Emergency Medicine");
        TrySetHeroImage(content, "leeds-general-infirmary");
        await SaveAndPublishAsync(content);
    }

    private async Task CreateGymPage(int parentId, CancellationToken ct)
    {
        var content = _contentService.Create("FitLife Gym", parentId, "gymPage");
        content.SetValue("title", "FitLife Gym");
        content.SetValue("description", "A modern fitness centre in Headingley with state-of-the-art equipment, classes, and personal training.");
        content.SetValue("telephone", "+44 113 275 4321");
        content.SetValue("streetAddress", "14 North Lane");
        content.SetValue("addressLocality", "Headingley, Leeds");
        content.SetValue("openingHours", "Mon-Fri 06:00-22:00, Sat-Sun 08:00-20:00");
        TrySetHeroImage(content, "fitlife-gym");
        await SaveAndPublishAsync(content);
    }

    private async Task CreateMoviePage(int parentId, CancellationToken ct)
    {
        var content = _contentService.Create("Umbraco: The Documentary", parentId, "moviePage");
        content.SetValue("title", "Umbraco: The Documentary");
        content.SetValue("description", "A feature-length documentary exploring the open-source CMS community that changed web development.");
        content.SetValue("director", "Niels Hartvig");
        content.SetValue("actor", "The Umbraco Community");
        content.SetValue("duration", "PT1H30M");
        content.SetValue("dateCreated", new DateTime(2025, 3, 15));
        TrySetHeroImage(content, "umbraco-the-documentary");
        await SaveAndPublishAsync(content);
    }

    private async Task CreateMusicAlbumPage(int parentId, CancellationToken ct)
    {
        var content = _contentService.Create("Schema Sounds", parentId, "musicAlbumPage");
        content.SetValue("title", "Schema Sounds");
        content.SetValue("description", "An electronic album inspired by structured data, featuring tracks named after Schema.org types.");
        content.SetValue("byArtist", "DJ Structured");
        content.SetValue("numTracks", "12");
        content.SetValue("datePublished", new DateTime(2025, 11, 1));
        TrySetHeroImage(content, "schema-sounds");
        await SaveAndPublishAsync(content);
    }

    private async Task CreatePodcastEpisodePage(int parentId, CancellationToken ct)
    {
        var content = _contentService.Create("Dev Talk Ep.1", parentId, "podcastEpisodePage");
        content.SetValue("title", "Dev Talk Ep.1 — Structured Data and SEO");
        content.SetValue("description", "In the first episode of Dev Talk, we explore how Schema.org structured data improves search visibility for Umbraco sites.");
        content.SetValue("duration", "PT45M");
        content.SetValue("datePublished", new DateTime(2026, 1, 10));
        TrySetHeroImage(content, "dev-talk-ep-1");
        await SaveAndPublishAsync(content);
    }

    private async Task CreatePhotographPage(int parentId, CancellationToken ct)
    {
        var content = _contentService.Create("Leeds Skyline", parentId, "photographPage");
        content.SetValue("title", "Leeds Skyline");
        content.SetValue("description", "A stunning panoramic photograph of the Leeds city centre skyline at sunset, taken from Roundhay Park.");
        content.SetValue("creator", "Oliver Picton");
        content.SetValue("dateCreated", new DateTime(2025, 8, 22));
        content.SetValue("contentUrl", "https://example.com/photos/leeds-skyline-2025.jpg");
        TrySetHeroImage(content, "leeds-skyline");
        await SaveAndPublishAsync(content);
    }

    private async Task CreateDatasetPage(int parentId, CancellationToken ct)
    {
        var content = _contentService.Create("UK Census 2021", parentId, "datasetPage");
        content.SetValue("title", "UK Census 2021");
        content.SetValue("description", "Official census data for England and Wales, including population demographics, housing, and employment statistics.");
        content.SetValue("distribution", "https://www.ons.gov.uk/census/2021census");
        content.SetValue("license", "Open Government Licence v3.0");
        TrySetHeroImage(content, "uk-census-2021");
        await SaveAndPublishAsync(content);
    }

    private async Task CreateMobileAppPage(int parentId, CancellationToken ct)
    {
        var content = _contentService.Create("SchemeWeaver Mobile", parentId, "mobileAppPage");
        content.SetValue("title", "SchemeWeaver Mobile");
        content.SetValue("description", "Monitor your Schema.org structured data on the go — validate JSON-LD, check mappings, and preview rich results.");
        content.SetValue("operatingSystem", "iOS, Android");
        content.SetValue("applicationCategory", "DeveloperApplication");
        content.SetValue("downloadUrl", "https://apps.example.com/schemeweaver");
        content.SetValue("softwareVersion", "1.2.0");
        TrySetHeroImage(content, "schemeweaver-mobile");
        await SaveAndPublishAsync(content);
    }

    private async Task CreateWebAppPage(int parentId, CancellationToken ct)
    {
        var content = _contentService.Create("SchemeWeaver Web", parentId, "webAppPage");
        content.SetValue("title", "SchemeWeaver Web");
        content.SetValue("description", "The online companion to SchemeWeaver — test and validate your structured data from any browser.");
        content.SetValue("browserRequirements", "Chrome 90+, Firefox 88+, Safari 14+, Edge 90+");
        content.SetValue("applicationCategory", "DeveloperApplication");
        content.SetValue("url", "https://app.schemeweaver.dev");
        TrySetHeroImage(content, "schemeweaver-web");
        await SaveAndPublishAsync(content);
    }

    // ──────────────────────────────────────────────────────────────
    // New subtype content creation methods (expanded demo — batch 2)
    // ──────────────────────────────────────────────────────────────

    // Shops
    private async Task CreateBookStorePage(int parentId, CancellationToken ct)
    {
        var content = _contentService.Create("Waterstones Leeds", parentId, "bookStorePage");
        content.SetValue("title", "Waterstones Leeds");
        content.SetValue("description", "One of the finest bookshops in Yorkshire, spread across three floors of beautiful architecture on Albion Street.");
        content.SetValue("telephone", "+44 113 244 4588");
        content.SetValue("streetAddress", "93-97 Albion Street");
        content.SetValue("addressLocality", "Leeds");
        content.SetValue("postalCode", "LS1 6AG");
        TrySetHeroImage(content, "waterstones-leeds");
        await SaveAndPublishAsync(content);
    }

    private async Task CreateElectronicsStorePage(int parentId, CancellationToken ct)
    {
        var content = _contentService.Create("Currys Leeds", parentId, "electronicsStorePage");
        content.SetValue("title", "Currys Leeds");
        content.SetValue("description", "Electrical retail superstore offering the latest tech, computing, and home appliances at Crown Point Retail Park.");
        content.SetValue("telephone", "+44 344 561 1234");
        content.SetValue("streetAddress", "Crown Point Retail Park");
        content.SetValue("addressLocality", "Leeds");
        content.SetValue("postalCode", "LS10 1ET");
        TrySetHeroImage(content, "currys-leeds");
        await SaveAndPublishAsync(content);
    }

    private async Task CreateClothingStorePage(int parentId, CancellationToken ct)
    {
        var content = _contentService.Create("Harvey Nichols Leeds", parentId, "clothingStorePage");
        content.SetValue("title", "Harvey Nichols Leeds");
        content.SetValue("description", "Luxury department store in the heart of the Victoria Quarter, offering designer fashion and fine dining.");
        content.SetValue("telephone", "+44 113 204 8888");
        content.SetValue("streetAddress", "107-111 Briggate");
        content.SetValue("addressLocality", "Leeds");
        content.SetValue("postalCode", "LS1 6AZ");
        TrySetHeroImage(content, "harvey-nichols-leeds");
        await SaveAndPublishAsync(content);
    }

    private async Task CreateGroceryStorePage(int parentId, CancellationToken ct)
    {
        var content = _contentService.Create("Marks & Spencer Leeds", parentId, "groceryStorePage");
        content.SetValue("title", "Marks & Spencer Leeds");
        content.SetValue("description", "Quality food hall and clothing on Briggate, a Leeds high-street staple since the company was founded here in 1884.");
        content.SetValue("telephone", "+44 113 242 3456");
        content.SetValue("streetAddress", "72 Briggate");
        content.SetValue("addressLocality", "Leeds");
        content.SetValue("postalCode", "LS1 6BR");
        TrySetHeroImage(content, "marks-spencer-leeds");
        await SaveAndPublishAsync(content);
    }

    private async Task CreateFurnitureStorePage(int parentId, CancellationToken ct)
    {
        var content = _contentService.Create("Heal's Leeds", parentId, "furnitureStorePage");
        content.SetValue("title", "Heal's Leeds");
        content.SetValue("description", "Contemporary furniture and homewares in the Victoria Quarter, blending timeless design with modern craftsmanship.");
        content.SetValue("telephone", "+44 113 245 7800");
        content.SetValue("streetAddress", "Victoria Quarter, Vicar Lane");
        content.SetValue("addressLocality", "Leeds");
        content.SetValue("postalCode", "LS1 6AX");
        TrySetHeroImage(content, "heal-s-leeds");
        await SaveAndPublishAsync(content);
    }

    private async Task CreateJewelryStorePage(int parentId, CancellationToken ct)
    {
        var content = _contentService.Create("Berry's Jewellers Leeds", parentId, "jewelryStorePage");
        content.SetValue("title", "Berry's Jewellers Leeds");
        content.SetValue("description", "Luxury jeweller in the Victoria Quarter, offering engagement rings, Swiss watches, and bespoke designs since 1897.");
        content.SetValue("telephone", "+44 113 246 8048");
        content.SetValue("streetAddress", "Victoria Quarter, Vicar Lane");
        content.SetValue("addressLocality", "Leeds");
        content.SetValue("postalCode", "LS1 6AX");
        TrySetHeroImage(content, "berry-s-jewellers-leeds");
        await SaveAndPublishAsync(content);
    }

    // Dining
    private async Task CreateBakeryPage(int parentId, CancellationToken ct)
    {
        var content = _contentService.Create("Bettys Tea Rooms", parentId, "bakeryPage");
        content.SetValue("title", "Bettys Tea Rooms");
        content.SetValue("description", "Iconic Yorkshire tea rooms serving handcrafted cakes, pastries, and afternoon tea since 1919.");
        content.SetValue("telephone", "+44 1423 814070");
        content.SetValue("streetAddress", "1 Parliament Street");
        content.SetValue("addressLocality", "Harrogate");
        content.SetValue("servesCuisine", "British, Patisserie");
        TrySetHeroImage(content, "bettys-tea-rooms");
        await SaveAndPublishAsync(content);
    }

    private async Task CreateCafePage(int parentId, CancellationToken ct)
    {
        var content = _contentService.Create("Laynes Espresso", parentId, "cafePage");
        content.SetValue("title", "Laynes Espresso");
        content.SetValue("description", "Speciality coffee shop near Leeds station, renowned for expertly crafted espresso and relaxed atmosphere.");
        content.SetValue("telephone", "+44 113 245 4454");
        content.SetValue("streetAddress", "16 New Station Street");
        content.SetValue("addressLocality", "Leeds");
        TrySetHeroImage(content, "laynes-espresso");
        await SaveAndPublishAsync(content);
    }

    private async Task CreateBarPage(int parentId, CancellationToken ct)
    {
        var content = _contentService.Create("The Midnight Bell", parentId, "barPage");
        content.SetValue("title", "The Midnight Bell");
        content.SetValue("description", "Award-winning Leeds Brewery pub on the waterfront, serving real ales, craft beers, and hearty pub grub.");
        content.SetValue("telephone", "+44 113 244 5044");
        content.SetValue("streetAddress", "101 Water Lane");
        content.SetValue("addressLocality", "Leeds");
        TrySetHeroImage(content, "the-midnight-bell");
        await SaveAndPublishAsync(content);
    }

    private async Task CreateFastFoodPage(int parentId, CancellationToken ct)
    {
        var content = _contentService.Create("Bulgogi Grill Leeds", parentId, "fastFoodPage");
        content.SetValue("title", "Bulgogi Grill Leeds");
        content.SetValue("description", "Quick-service Korean street food in Kirkgate Market, offering bibimbap bowls and bulgogi wraps.");
        content.SetValue("telephone", "+44 113 245 9876");
        content.SetValue("streetAddress", "Kirkgate Market");
        content.SetValue("servesCuisine", "Korean");
        TrySetHeroImage(content, "bulgogi-grill-leeds");
        await SaveAndPublishAsync(content);
    }

    private async Task CreateWineryPage(int parentId, CancellationToken ct)
    {
        var content = _contentService.Create("Leventhorpe Vineyard", parentId, "wineryPage");
        content.SetValue("title", "Leventhorpe Vineyard");
        content.SetValue("description", "One of the most northerly vineyards in England, producing award-winning wines on the outskirts of Leeds since 1986.");
        content.SetValue("telephone", "+44 113 288 0088");
        content.SetValue("streetAddress", "Bullerthorpe Lane");
        content.SetValue("addressLocality", "Woodlesford, Leeds");
        TrySetHeroImage(content, "leventhorpe-vineyard");
        await SaveAndPublishAsync(content);
    }

    private async Task CreateBreweryPage(int parentId, CancellationToken ct)
    {
        var content = _contentService.Create("Northern Monk Brewery", parentId, "breweryPage");
        content.SetValue("title", "Northern Monk Brewery");
        content.SetValue("description", "Independent craft brewery in Holbeck, Leeds, known for their Faith pale ale and rotating seasonal brews.");
        content.SetValue("telephone", "+44 113 243 6430");
        content.SetValue("streetAddress", "The Old Flax Store, Marshall Street");
        content.SetValue("addressLocality", "Leeds");
        TrySetHeroImage(content, "northern-monk-brewery");
        await SaveAndPublishAsync(content);
    }

    // Travel
    private async Task CreateFlightReservationPage(int parentId, CancellationToken ct)
    {
        var content = _contentService.Create("Leeds Bradford to Malaga", parentId, "flightReservationPage");
        content.SetValue("title", "Leeds Bradford to Malaga");
        content.SetValue("description", "Summer holiday flight reservation from Leeds Bradford Airport to Malaga, Spain.");
        content.SetValue("reservationId", "LBA-2026-001");
        content.SetValue("flightNumber", "FR2045");
        content.SetValue("departureAirport", "Leeds Bradford Airport (LBA)");
        content.SetValue("arrivalAirport", "Malaga Airport (AGP)");
        content.SetValue("departureTime", new DateTime(2026, 7, 20, 6, 30, 0));
        TrySetHeroImage(content, "leeds-bradford-to-malaga");
        await SaveAndPublishAsync(content);
    }

    private async Task CreateLodgingReservationPage(int parentId, CancellationToken ct)
    {
        var content = _contentService.Create("Grand Hotel Leeds Stay", parentId, "lodgingReservationPage");
        content.SetValue("title", "Grand Hotel Leeds Stay");
        content.SetValue("description", "Two-night stay at the Grand Hotel Leeds, including breakfast and spa access.");
        content.SetValue("reservationId", "GHL-2026-042");
        content.SetValue("checkinTime", new DateTime(2026, 7, 15, 15, 0, 0));
        content.SetValue("checkoutTime", new DateTime(2026, 7, 17, 11, 0, 0));
        content.SetValue("lodgingUnitDescription", "Deluxe King Room with City View");
        TrySetHeroImage(content, "grand-hotel-leeds-stay");
        await SaveAndPublishAsync(content);
    }

    private async Task CreateEventReservationPage(int parentId, CancellationToken ct)
    {
        var content = _contentService.Create("Wicked Musical Tickets", parentId, "eventReservationPage");
        content.SetValue("title", "Wicked Musical Tickets");
        content.SetValue("description", "Two stalls tickets for Wicked at Leeds Grand Theatre, Saturday evening performance.");
        content.SetValue("reservationId", "LGT-2026-088");
        content.SetValue("reservationFor", "Wicked at Leeds Grand Theatre");
        TrySetHeroImage(content, "wicked-musical-tickets");
        await SaveAndPublishAsync(content);
    }

    private async Task CreateRentalCarPage(int parentId, CancellationToken ct)
    {
        var content = _contentService.Create("Enterprise Leeds Airport", parentId, "rentalCarPage");
        content.SetValue("title", "Enterprise Leeds Airport Car Hire");
        content.SetValue("description", "Compact car rental from Enterprise at Leeds Bradford Airport for a weekend trip to the Dales.");
        content.SetValue("reservationId", "ENT-2026-LBA-055");
        content.SetValue("pickupLocation", "Leeds Bradford Airport, Whitehouse Lane");
        content.SetValue("pickupTime", new DateTime(2026, 8, 5, 10, 0, 0));
        TrySetHeroImage(content, "enterprise-leeds-airport");
        await SaveAndPublishAsync(content);
    }

    private async Task CreateFlightPage(int parentId, CancellationToken ct)
    {
        var content = _contentService.Create("Flight FR2045", parentId, "flightPage");
        content.SetValue("title", "Flight FR2045 — Leeds to Malaga");
        content.SetValue("description", "Direct flight from Leeds Bradford to Malaga Costa del Sol, operated by Ryanair.");
        content.SetValue("flightNumber", "FR2045");
        content.SetValue("departureAirport", "Leeds Bradford Airport (LBA)");
        content.SetValue("arrivalAirport", "Malaga Airport (AGP)");
        content.SetValue("departureTime", new DateTime(2026, 7, 20, 6, 30, 0));
        content.SetValue("arrivalTime", new DateTime(2026, 7, 20, 10, 45, 0));
        TrySetHeroImage(content, "flight-fr2045");
        await SaveAndPublishAsync(content);
    }

    private async Task CreateTouristAttractionPage(int parentId, CancellationToken ct)
    {
        var content = _contentService.Create("Fountains Abbey", parentId, "touristAttractionPage");
        content.SetValue("title", "Fountains Abbey");
        content.SetValue("description", "UNESCO World Heritage Site featuring the ruins of a 12th-century Cistercian monastery set in the stunning Studley Royal Water Garden.");
        content.SetValue("streetAddress", "Studley Royal Water Garden");
        content.SetValue("addressLocality", "Ripon, North Yorkshire");
        content.SetValue("telephone", "+44 1765 608888");
        TrySetHeroImage(content, "fountains-abbey");
        await SaveAndPublishAsync(content);
    }

    private async Task CreateBedAndBreakfastPage(int parentId, CancellationToken ct)
    {
        var content = _contentService.Create("The Arden House", parentId, "bedAndBreakfastPage");
        content.SetValue("title", "The Arden House");
        content.SetValue("description", "Charming bed and breakfast in Headingley, offering cosy rooms and a full Yorkshire breakfast just minutes from the cricket ground.");
        content.SetValue("telephone", "+44 113 275 2224");
        content.SetValue("streetAddress", "18 Shire Oak Road");
        content.SetValue("addressLocality", "Headingley, Leeds");
        content.SetValue("starRating", "4");
        TrySetHeroImage(content, "the-arden-house");
        await SaveAndPublishAsync(content);
    }

    private async Task CreateCampgroundPage(int parentId, CancellationToken ct)
    {
        var content = _contentService.Create("Moor Lodge Campsite", parentId, "campgroundPage");
        content.SetValue("title", "Moor Lodge Campsite");
        content.SetValue("description", "Family-friendly campsite on the edge of the Yorkshire Dales with panoramic moorland views and modern facilities.");
        content.SetValue("telephone", "+44 1756 720340");
        content.SetValue("streetAddress", "Moor Lane");
        content.SetValue("addressLocality", "Burnsall, North Yorkshire");
        TrySetHeroImage(content, "moor-lodge-campsite");
        await SaveAndPublishAsync(content);
    }

    // Healthcare
    private async Task CreateDrugPage(int parentId, CancellationToken ct)
    {
        var content = _contentService.Create("Ibuprofen 400mg", parentId, "drugPage");
        content.SetValue("title", "Ibuprofen 400mg");
        content.SetValue("description", "Non-steroidal anti-inflammatory drug used for pain relief, fever reduction, and reducing inflammation.");
        content.SetValue("activeIngredient", "Ibuprofen");
        content.SetValue("dosageForm", "Tablet");
        content.SetValue("administrationRoute", "Oral");
        content.SetValue("prescriptionStatus", "Over the counter");
        TrySetHeroImage(content, "ibuprofen-400mg");
        await SaveAndPublishAsync(content);
    }

    private async Task CreateMedicalConditionPage(int parentId, CancellationToken ct)
    {
        var content = _contentService.Create("Type 2 Diabetes", parentId, "medicalConditionPage");
        content.SetValue("title", "Type 2 Diabetes");
        content.SetValue("description", "A chronic condition affecting the way the body processes blood sugar (glucose), common in adults.");
        content.SetValue("possibleTreatment", "Metformin, lifestyle changes, insulin therapy");
        content.SetValue("riskFactor", "Obesity, sedentary lifestyle, family history");
        content.SetValue("signOrSymptom", "Increased thirst, frequent urination, fatigue");
        TrySetHeroImage(content, "type-2-diabetes");
        await SaveAndPublishAsync(content);
    }

    private async Task CreatePhysicianPage(int parentId, CancellationToken ct)
    {
        var content = _contentService.Create("Dr Sarah Mitchell", parentId, "physicianPage");
        content.SetValue("title", "Dr Sarah Mitchell");
        content.SetValue("description", "Experienced GP at Leeds Medical Centre, specialising in family medicine and preventive healthcare.");
        content.SetValue("telephone", "+44 113 295 4400");
        content.SetValue("streetAddress", "4 Great George Street");
        content.SetValue("addressLocality", "Leeds");
        content.SetValue("medicalSpecialty", "General Practice");
        TrySetHeroImage(content, "dr-sarah-mitchell");
        await SaveAndPublishAsync(content);
    }

    private async Task CreatePharmacyPage(int parentId, CancellationToken ct)
    {
        var content = _contentService.Create("Boots Pharmacy Briggate", parentId, "pharmacyPage");
        content.SetValue("title", "Boots Pharmacy Briggate");
        content.SetValue("description", "Central Leeds pharmacy offering prescriptions, health checks, and beauty consultations on Briggate.");
        content.SetValue("telephone", "+44 113 243 1771");
        content.SetValue("streetAddress", "15 Briggate");
        content.SetValue("addressLocality", "Leeds");
        TrySetHeroImage(content, "boots-pharmacy-briggate");
        await SaveAndPublishAsync(content);
    }

    private async Task CreateDentistPage(int parentId, CancellationToken ct)
    {
        var content = _contentService.Create("Park Row Dental Practice", parentId, "dentistPage");
        content.SetValue("title", "Park Row Dental Practice");
        content.SetValue("description", "Modern dental surgery in Leeds city centre providing NHS and private dental care for all the family.");
        content.SetValue("telephone", "+44 113 245 5522");
        content.SetValue("streetAddress", "11 Park Row");
        content.SetValue("addressLocality", "Leeds");
        TrySetHeroImage(content, "park-row-dental-practice");
        await SaveAndPublishAsync(content);
    }

    private async Task CreateMedicalClinicPage(int parentId, CancellationToken ct)
    {
        var content = _contentService.Create("Nuffield Health Leeds", parentId, "medicalClinicPage");
        content.SetValue("title", "Nuffield Health Leeds");
        content.SetValue("description", "Private medical clinic offering consultations, diagnostics, and outpatient treatments near the city centre.");
        content.SetValue("telephone", "+44 113 388 2000");
        content.SetValue("streetAddress", "2 Leighton Street");
        content.SetValue("addressLocality", "Leeds");
        content.SetValue("medicalSpecialty", "Orthopaedics, Cardiology, Dermatology");
        TrySetHeroImage(content, "nuffield-health-leeds");
        await SaveAndPublishAsync(content);
    }

    // Automotive
    private async Task CreateCarPage(int parentId, CancellationToken ct)
    {
        var content = _contentService.Create("2024 Volkswagen Golf", parentId, "carPage");
        content.SetValue("title", "2024 Volkswagen Golf");
        content.SetValue("description", "Well-maintained Volkswagen Golf TSI with low mileage, one owner from new, full service history.");
        content.SetValue("brand", "Volkswagen");
        content.SetValue("model", "Golf");
        content.SetValue("fuelType", "Petrol");
        content.SetValue("mileageFromOdometer", "15000 miles");
        content.SetValue("color", "Pure White");
        content.SetValue("numberOfDoors", "5");
        content.SetValue("vehicleTransmission", "Manual");
        TrySetHeroImage(content, "2024-volkswagen-golf");
        await SaveAndPublishAsync(content);
    }

    private async Task CreateMotorcyclePage(int parentId, CancellationToken ct)
    {
        var content = _contentService.Create("2023 Triumph Street Triple", parentId, "motorcyclePage");
        content.SetValue("title", "2023 Triumph Street Triple");
        content.SetValue("description", "Triumph Street Triple 765cc — the perfect balance of performance and everyday usability from a British icon.");
        content.SetValue("brand", "Triumph");
        content.SetValue("model", "Street Triple 765");
        content.SetValue("fuelType", "Petrol");
        content.SetValue("mileageFromOdometer", "4200 miles");
        content.SetValue("color", "Matt Jet Black");
        TrySetHeroImage(content, "2023-triumph-street-triple");
        await SaveAndPublishAsync(content);
    }

    private async Task CreateAutoDealerPage(int parentId, CancellationToken ct)
    {
        var content = _contentService.Create("JCT600 Leeds", parentId, "autoDealerPage");
        content.SetValue("title", "JCT600 Leeds");
        content.SetValue("description", "Family-owned motor group established in Bradford in 1946, offering new and used vehicles from leading brands.");
        content.SetValue("telephone", "+44 113 389 0600");
        content.SetValue("streetAddress", "Stonebridge Lane");
        content.SetValue("addressLocality", "Leeds");
        TrySetHeroImage(content, "jct600-leeds");
        await SaveAndPublishAsync(content);
    }

    private async Task CreateAutoRepairPage(int parentId, CancellationToken ct)
    {
        var content = _contentService.Create("Kwik Fit Leeds Central", parentId, "autoRepairPage");
        content.SetValue("title", "Kwik Fit Leeds Central");
        content.SetValue("description", "Full-service car repair and MOT testing centre in Leeds, offering tyres, brakes, exhausts, and servicing.");
        content.SetValue("telephone", "+44 113 242 8899");
        content.SetValue("streetAddress", "45 Wellington Road");
        content.SetValue("addressLocality", "Leeds");
        TrySetHeroImage(content, "kwik-fit-leeds-central");
        await SaveAndPublishAsync(content);
    }

    // Events (additional)
    private async Task CreateTheaterEventPage(int parentId, CancellationToken ct)
    {
        var content = _contentService.Create("Wicked at Leeds Grand", parentId, "theaterEventPage");
        content.SetValue("title", "Wicked at Leeds Grand");
        content.SetValue("description", "The smash-hit musical Wicked arrives at Leeds Grand Theatre for a limited summer run — discover the untold story of the witches of Oz.");
        content.SetValue("performer", "UK Touring Cast");
        content.SetValue("startDate", new DateTime(2026, 8, 10, 19, 30, 0));
        content.SetValue("locationName", "Leeds Grand Theatre");
        TrySetHeroImage(content, "wicked-at-leeds-grand");
        await SaveAndPublishAsync(content);
    }

    private async Task CreateScreeningEventPage(int parentId, CancellationToken ct)
    {
        var content = _contentService.Create("Hyde Park Picture House Screening", parentId, "screeningEventPage");
        content.SetValue("title", "Classic Film Night: Brief Encounter");
        content.SetValue("description", "Special screening of the 1945 classic Brief Encounter at the beautifully restored Hyde Park Picture House.");
        content.SetValue("startDate", new DateTime(2026, 9, 5, 20, 0, 0));
        content.SetValue("locationName", "Hyde Park Picture House, Leeds");
        content.SetValue("workPresented", "Brief Encounter (1945)");
        TrySetHeroImage(content, "hyde-park-picture-house-screening");
        await SaveAndPublishAsync(content);
    }

    // Entertainment
    private async Task CreateMusicGroupPage(int parentId, CancellationToken ct)
    {
        var content = _contentService.Create("Kaiser Chiefs", parentId, "musicGroupPage");
        content.SetValue("title", "Kaiser Chiefs");
        content.SetValue("description", "Indie rock band formed in Leeds in 2000, known for hits like I Predict a Riot and Ruby.");
        content.SetValue("genre", "Indie Rock");
        content.SetValue("foundingDate", "2000");
        TrySetHeroImage(content, "kaiser-chiefs");
        await SaveAndPublishAsync(content);
    }

    private async Task CreateZooPage(int parentId, CancellationToken ct)
    {
        var content = _contentService.Create("Yorkshire Wildlife Park", parentId, "zooPage");
        content.SetValue("title", "Yorkshire Wildlife Park");
        content.SetValue("description", "Award-winning wildlife park home to over 400 animals including polar bears, lions, and lemurs, set across 150 acres.");
        content.SetValue("telephone", "+44 1302 535057");
        content.SetValue("streetAddress", "Warning Tongue Lane");
        content.SetValue("addressLocality", "Doncaster");
        TrySetHeroImage(content, "yorkshire-wildlife-park");
        await SaveAndPublishAsync(content);
    }

    private async Task CreateMuseumPage(int parentId, CancellationToken ct)
    {
        var content = _contentService.Create("Royal Armouries Leeds", parentId, "museumPage");
        content.SetValue("title", "Royal Armouries Leeds");
        content.SetValue("description", "The national museum of arms and armour, free to visit, housing over 75,000 objects spanning five thousand years of history.");
        content.SetValue("telephone", "+44 113 220 1999");
        content.SetValue("streetAddress", "Armouries Drive");
        content.SetValue("addressLocality", "Leeds");
        TrySetHeroImage(content, "royal-armouries-leeds");
        await SaveAndPublishAsync(content);
    }

    private async Task CreateAmusementParkPage(int parentId, CancellationToken ct)
    {
        var content = _contentService.Create("Flamingo Land", parentId, "amusementParkPage");
        content.SetValue("title", "Flamingo Land");
        content.SetValue("description", "Theme park, zoo, and resort in the heart of the North York Moors offering thrilling rides and family entertainment.");
        content.SetValue("telephone", "+44 1653 668287");
        content.SetValue("streetAddress", "Kirby Misperton");
        content.SetValue("addressLocality", "Malton, North Yorkshire");
        TrySetHeroImage(content, "flamingo-land");
        await SaveAndPublishAsync(content);
    }

    // Services
    private async Task CreateAttorneyPage(int parentId, CancellationToken ct)
    {
        var content = _contentService.Create("Walker Morris LLP", parentId, "attorneyPage");
        content.SetValue("title", "Walker Morris LLP");
        content.SetValue("description", "Full-service commercial law firm headquartered in Leeds, providing legal services to businesses and individuals since 1872.");
        content.SetValue("telephone", "+44 113 283 2500");
        content.SetValue("streetAddress", "Kings Chambers, Wellington Place");
        content.SetValue("addressLocality", "Leeds");
        TrySetHeroImage(content, "walker-morris-llp");
        await SaveAndPublishAsync(content);
    }

    private async Task CreateAccountingPage(int parentId, CancellationToken ct)
    {
        var content = _contentService.Create("BDO Leeds", parentId, "accountingPage");
        content.SetValue("title", "BDO Leeds");
        content.SetValue("description", "Accountancy and business advisory firm offering audit, tax, and consulting services from their Leeds office.");
        content.SetValue("telephone", "+44 113 204 1220");
        content.SetValue("streetAddress", "55 Baker Street");
        content.SetValue("addressLocality", "Leeds");
        TrySetHeroImage(content, "bdo-leeds");
        await SaveAndPublishAsync(content);
    }

    private async Task CreateRealEstatePage(int parentId, CancellationToken ct)
    {
        var content = _contentService.Create("Dacre Son & Hartley", parentId, "realEstatePage");
        content.SetValue("title", "Dacre Son & Hartley");
        content.SetValue("description", "Leading Yorkshire estate agent with over 100 years' experience selling residential property across Leeds and the surrounding areas.");
        content.SetValue("telephone", "+44 113 246 1234");
        content.SetValue("streetAddress", "20 Park Row");
        content.SetValue("addressLocality", "Leeds");
        TrySetHeroImage(content, "dacre-son-hartley");
        await SaveAndPublishAsync(content);
    }

    private async Task CreateInsurancePage(int parentId, CancellationToken ct)
    {
        var content = _contentService.Create("Direct Line Leeds", parentId, "insurancePage");
        content.SetValue("title", "Direct Line Leeds");
        content.SetValue("description", "Insurance services covering home, motor, travel, and business from a well-known British insurer with a Leeds regional office.");
        content.SetValue("telephone", "+44 345 246 8704");
        content.SetValue("streetAddress", "3 Brewery Wharf");
        content.SetValue("addressLocality", "Leeds");
        TrySetHeroImage(content, "direct-line-leeds");
        await SaveAndPublishAsync(content);
    }

    private async Task CreateTravelAgencyPage(int parentId, CancellationToken ct)
    {
        var content = _contentService.Create("Hays Travel Leeds", parentId, "travelAgencyPage");
        content.SetValue("title", "Hays Travel Leeds");
        content.SetValue("description", "Independent travel agency on Briggate offering package holidays, cruises, and tailor-made trips worldwide.");
        content.SetValue("telephone", "+44 113 242 0099");
        content.SetValue("streetAddress", "56 Briggate");
        content.SetValue("addressLocality", "Leeds");
        TrySetHeroImage(content, "hays-travel-leeds");
        await SaveAndPublishAsync(content);
    }

    // Education
    private async Task CreateUniversityPage(int parentId, CancellationToken ct)
    {
        var content = _contentService.Create("University of Leeds", parentId, "universityPage");
        content.SetValue("title", "University of Leeds");
        content.SetValue("description", "Russell Group university established in 1904, consistently ranked among the top universities in the UK for research and teaching.");
        content.SetValue("streetAddress", "Woodhouse Lane");
        content.SetValue("addressLocality", "Leeds");
        content.SetValue("telephone", "+44 113 243 1751");
        content.SetValue("foundingDate", "1904");
        TrySetHeroImage(content, "university-of-leeds");
        await SaveAndPublishAsync(content);
    }

    private async Task CreateSchoolPage(int parentId, CancellationToken ct)
    {
        var content = _contentService.Create("Leeds Grammar School", parentId, "schoolPage");
        content.SetValue("title", "Leeds Grammar School");
        content.SetValue("description", "One of the oldest schools in the north of England, providing outstanding education for boys and girls aged 3 to 18.");
        content.SetValue("streetAddress", "Alwoodley Gates, Harrogate Road");
        content.SetValue("addressLocality", "Leeds");
        content.SetValue("telephone", "+44 113 229 1552");
        TrySetHeroImage(content, "leeds-grammar-school");
        await SaveAndPublishAsync(content);
    }

    private async Task CreateCourseInstancePage(int parentId, CancellationToken ct)
    {
        var content = _contentService.Create("Umbraco Certified Developer 2026", parentId, "courseInstancePage");
        content.SetValue("title", "Umbraco Certified Developer 2026");
        content.SetValue("description", "Official Umbraco certification course covering content modelling, custom extensions, and deployment best practices.");
        content.SetValue("startDate", new DateTime(2026, 9, 1, 9, 0, 0));
        content.SetValue("endDate", new DateTime(2026, 9, 5, 17, 0, 0));
        content.SetValue("locationName", "Platform, New Station Street, Leeds");
        content.SetValue("instructor", "Oliver Picton");
        TrySetHeroImage(content, "umbraco-certified-developer-2026");
        await SaveAndPublishAsync(content);
    }

    // Blog (additional)
    private async Task CreateBlogPageContent(int parentId, CancellationToken ct)
    {
        var content = _contentService.Create("SchemeWeaver Dev Blog", parentId, "blogPage");
        content.SetValue("title", "SchemeWeaver Dev Blog");
        content.SetValue("description", "Development updates, release notes, and insights from the SchemeWeaver team.");
        TrySetHeroImage(content, "schemeweaver-dev-blog");
        await SaveAndPublishAsync(content);
    }

    private async Task CreateLiveBlogPage(int parentId, CancellationToken ct)
    {
        var content = _contentService.Create("Umbraco UK Fest 2026 Live Blog", parentId, "liveBlogPage");
        content.SetValue("title", "Umbraco UK Fest 2026 Live Blog");
        content.SetValue("description", "Live coverage of all sessions, keynotes, and announcements from Umbraco UK Festival 2026 in London.");
        content.SetValue("bodyText", "<p>Follow along as we bring you live updates from the biggest Umbraco community event of the year.</p>");
        content.SetValue("publishDate", new DateTime(2026, 6, 12, 9, 0, 0));
        content.SetValue("authorName", "Oliver Picton");
        content.SetValue("coverageStartTime", new DateTime(2026, 6, 12, 9, 0, 0));
        content.SetValue("coverageEndTime", new DateTime(2026, 6, 12, 18, 0, 0));
        TrySetHeroImage(content, "umbraco-uk-fest-2026-live-blog");
        await SaveAndPublishAsync(content);
    }

    // Creative (additional)
    private async Task CreateReportPage(int parentId, CancellationToken ct)
    {
        var content = _contentService.Create("State of Structured Data 2026", parentId, "reportPage");
        content.SetValue("title", "State of Structured Data 2026");
        content.SetValue("description", "Annual report analysing the adoption of Schema.org structured data across the top 10,000 websites in the UK.");
        content.SetValue("bodyText", "<p>Our 2026 report reveals that structured data adoption has grown by 35% year on year, with JSON-LD now the dominant format across all sectors.</p>");
        content.SetValue("authorName", "Oliver Picton");
        content.SetValue("publishDate", new DateTime(2026, 3, 1));
        TrySetHeroImage(content, "state-of-structured-data-2026");
        await SaveAndPublishAsync(content);
    }

    private async Task CreateVideoGamePage(int parentId, CancellationToken ct)
    {
        var content = _contentService.Create("Schema Quest", parentId, "videoGamePage");
        content.SetValue("title", "Schema Quest");
        content.SetValue("description", "An RPG adventure where you map content types to Schema.org to save the web from unstructured data.");
        content.SetValue("applicationCategory", "Game");
        content.SetValue("operatingSystem", "Windows, macOS, Linux");
        content.SetValue("gamePlatform", "PC, PlayStation 5, Xbox Series X");
        content.SetValue("genre", "RPG");
        TrySetHeroImage(content, "schema-quest");
        await SaveAndPublishAsync(content);
    }

    private async Task CreateSourceCodePage(int parentId, CancellationToken ct)
    {
        var content = _contentService.Create("SchemeWeaver Source", parentId, "sourceCodePage");
        content.SetValue("title", "SchemeWeaver Source");
        content.SetValue("description", "Open-source repository for the Umbraco SchemeWeaver package — contributions welcome.");
        content.SetValue("programmingLanguage", "C#, TypeScript");
        content.SetValue("runtimePlatform", ".NET 10");
        content.SetValue("codeRepository", "https://github.com/EnjoyDigital/Umbraco.Community.SchemeWeaver/Umbraco.Community.SchemeWeaver");
        TrySetHeroImage(content, "schemeweaver-source");
        await SaveAndPublishAsync(content);
    }

    // Standalone
    private async Task CreateOccupationPage(int parentId, CancellationToken ct)
    {
        var content = _contentService.Create("Software Engineer", parentId, "occupationPage");
        content.SetValue("title", "Software Engineer");
        content.SetValue("description", "Designs, develops, and maintains software systems — one of the most in-demand occupations in the UK tech sector.");
        content.SetValue("occupationalCategory", "15-1256.00");
        content.SetValue("estimatedSalary", "45000-80000 GBP per year");
        content.SetValue("skills", "C#, TypeScript, SQL, Cloud Computing, Agile");
        TrySetHeroImage(content, "software-engineer");
        await SaveAndPublishAsync(content);
    }

    private async Task CreateWebPageContent(int parentId, CancellationToken ct)
    {
        var content = _contentService.Create("About This Site", parentId, "webPage");
        content.SetValue("title", "About This Site");
        content.SetValue("description", "Information about the SchemeWeaver demo site and its structured data capabilities.");
        content.SetValue("bodyText", "This site demonstrates how Umbraco content types can be mapped to Schema.org types to produce JSON-LD structured data automatically.");
        TrySetHeroImage(content, "about-this-site");
        await SaveAndPublishAsync(content);
    }

    private async Task CreateServicePageContent(int parentId, CancellationToken ct)
    {
        var content = _contentService.Create("Web Design Services", parentId, "servicePage");
        content.SetValue("title", "Web Design Services");
        content.SetValue("description", "Bespoke web design and development services for businesses in Leeds and across Yorkshire.");
        content.SetValue("serviceType", "Web Design");
        content.SetValue("provider", "Enjoy Digital");
        content.SetValue("areaServed", "Leeds, Yorkshire, United Kingdom");
        content.SetValue("price", "From £2,500");
        TrySetHeroImage(content, "web-design-services");
        await SaveAndPublishAsync(content);
    }

    private async Task CreateProfessionalServicePageContent(int parentId, CancellationToken ct)
    {
        var content = _contentService.Create("Leeds Consulting Group", parentId, "professionalServicePage");
        content.SetValue("title", "Leeds Consulting Group");
        content.SetValue("description", "Management consultancy firm providing strategic advisory services to businesses across the North of England.");
        content.SetValue("telephone", "+44 113 245 6789");
        content.SetValue("email", "enquiries@leedsconsulting.example.com");
        content.SetValue("streetAddress", "45 Park Row");
        content.SetValue("addressLocality", "Leeds");
        content.SetValue("postalCode", "LS1 5JL");
        content.SetValue("addressCountry", "GB");
        content.SetValue("priceRange", "$$$");
        TrySetHeroImage(content, "leeds-consulting-group");
        await SaveAndPublishAsync(content);
    }

    private async Task CreateLegalServicePageContent(int parentId, CancellationToken ct)
    {
        var content = _contentService.Create("Whitehall Legal Partners", parentId, "legalServicePage");
        content.SetValue("title", "Whitehall Legal Partners");
        content.SetValue("description", "Full-service law firm specialising in commercial, employment, and property law.");
        content.SetValue("telephone", "+44 113 246 0101");
        content.SetValue("email", "info@whitehall-legal.example.com");
        content.SetValue("streetAddress", "12 Whitehall Road");
        content.SetValue("addressLocality", "Leeds");
        content.SetValue("postalCode", "LS1 4AW");
        content.SetValue("addressCountry", "GB");
        TrySetHeroImage(content, "whitehall-legal-partners");
        await SaveAndPublishAsync(content);
    }

    private async Task CreateNotaryPageContent(int parentId, CancellationToken ct)
    {
        var content = _contentService.Create("Leeds Notary Public", parentId, "notaryPage");
        content.SetValue("title", "Leeds Notary Public");
        content.SetValue("description", "Experienced notary public providing document authentication, oath administration, and international notarial services for individuals and businesses.");
        content.SetValue("telephone", "+44 113 245 8800");
        content.SetValue("email", "notary@leedsnotary.example.com");
        content.SetValue("streetAddress", "15 Greek Street");
        content.SetValue("addressLocality", "Leeds");
        content.SetValue("postalCode", "LS1 5RU");
        content.SetValue("addressCountry", "GB");
        TrySetHeroImage(content, "leeds-notary-public");
        await SaveAndPublishAsync(content);
    }

    private async Task CreateCourthousePageContent(int parentId, CancellationToken ct)
    {
        var content = _contentService.Create("Leeds Combined Court Centre", parentId, "courthousePage");
        content.SetValue("title", "Leeds Combined Court Centre");
        content.SetValue("description", "Major court complex housing the Crown Court, County Court, and Family Court, handling civil, criminal, and family proceedings across West Yorkshire.");
        content.SetValue("telephone", "+44 113 306 2800");
        content.SetValue("streetAddress", "1 Oxford Row");
        content.SetValue("addressLocality", "Leeds");
        content.SetValue("postalCode", "LS1 3BG");
        content.SetValue("addressCountry", "GB");
        TrySetHeroImage(content, "leeds-combined-court-centre");
        await SaveAndPublishAsync(content);
    }

    private async Task CreateLegislationPageContent(int parentId, CancellationToken ct)
    {
        var content = _contentService.Create("Data Protection Act 2018", parentId, "legislationPage");
        content.SetValue("title", "Data Protection Act 2018");
        content.SetValue("description", "The Data Protection Act 2018 is the UK's implementation of the General Data Protection Regulation (GDPR). It controls how personal information is used by organisations, businesses, and the government.");
        content.SetValue("bodyText", "<p>The Data Protection Act 2018 sets out the framework for data protection law in the United Kingdom. It updates and replaces the Data Protection Act 1998 and supplements the EU General Data Protection Regulation (GDPR).</p><p>The Act covers the general processing of personal data, law enforcement processing, and intelligence services processing. It establishes the Information Commissioner as the UK's independent supervisory authority for data protection.</p>");
        content.SetValue("legislationIdentifier", "ukpga/2018/12");
        content.SetValue("legislationDate", new DateTime(2018, 5, 23));
        content.SetValue("legislationJurisdiction", "United Kingdom");
        content.SetValue("legislationType", "Act of Parliament");
        content.SetValue("legislationLegalForce", "InForce");
        TrySetHeroImage(content, "data-protection-act-2018");
        await SaveAndPublishAsync(content);
    }

    private async Task CreateFinancialServicePageContent(int parentId, CancellationToken ct)
    {
        var content = _contentService.Create("Northern Finance Advisors", parentId, "financialServicePage");
        content.SetValue("title", "Northern Finance Advisors");
        content.SetValue("description", "Independent financial advisors offering mortgage, pension, and investment guidance.");
        content.SetValue("telephone", "+44 113 247 3300");
        content.SetValue("email", "advice@northernfinance.example.com");
        content.SetValue("streetAddress", "8 Wellington Street");
        content.SetValue("addressLocality", "Leeds");
        content.SetValue("postalCode", "LS1 4LT");
        content.SetValue("addressCountry", "GB");
        TrySetHeroImage(content, "northern-finance-advisors");
        await SaveAndPublishAsync(content);
    }

    private async Task CreateGovernmentOrgPageContent(int parentId, CancellationToken ct)
    {
        var content = _contentService.Create("Leeds City Council", parentId, "governmentOrgPage");
        content.SetValue("title", "Leeds City Council");
        content.SetValue("description", "The metropolitan borough council governing the city of Leeds in West Yorkshire, England.");
        content.SetValue("telephone", "+44 113 222 4444");
        content.SetValue("email", "contact@leeds.gov.example.com");
        content.SetValue("streetAddress", "Civic Hall, Calverley Street");
        content.SetValue("addressLocality", "Leeds");
        content.SetValue("postalCode", "LS1 1UR");
        content.SetValue("addressCountry", "GB");
        content.SetValue("areaServed", "Leeds, West Yorkshire");
        TrySetHeroImage(content, "leeds-city-council");
        await SaveAndPublishAsync(content);
    }

    private async Task CreateLibraryPageContent(int parentId, CancellationToken ct)
    {
        var content = _contentService.Create("Leeds Central Library", parentId, "libraryPage");
        content.SetValue("title", "Leeds Central Library");
        content.SetValue("description", "Grade II listed library in the heart of Leeds offering over 300,000 volumes, public computers, and community events.");
        content.SetValue("telephone", "+44 113 247 8911");
        content.SetValue("streetAddress", "Calverley Street");
        content.SetValue("addressLocality", "Leeds");
        content.SetValue("postalCode", "LS1 3AB");
        content.SetValue("addressCountry", "GB");
        TrySetHeroImage(content, "leeds-central-library");
        await SaveAndPublishAsync(content);
    }

    private async Task CreateMovieTheaterPageContent(int parentId, CancellationToken ct)
    {
        var content = _contentService.Create("Everyman Cinema Leeds", parentId, "movieTheaterPage");
        content.SetValue("title", "Everyman Cinema Leeds");
        content.SetValue("description", "Boutique cinema experience with luxury seating, in-screen dining, and a curated film programme in Trinity Leeds.");
        content.SetValue("telephone", "+44 113 318 0009");
        content.SetValue("screenCount", "4");
        content.SetValue("streetAddress", "Trinity Leeds, Albion Street");
        content.SetValue("addressLocality", "Leeds");
        content.SetValue("postalCode", "LS1 5AT");
        content.SetValue("addressCountry", "GB");
        TrySetHeroImage(content, "everyman-cinema-leeds");
        await SaveAndPublishAsync(content);
    }

    private async Task CreateNightClubPageContent(int parentId, CancellationToken ct)
    {
        var content = _contentService.Create("Mint Warehouse", parentId, "nightClubPage");
        content.SetValue("title", "Mint Warehouse");
        content.SetValue("description", "One of Leeds' premier nightclub venues hosting electronic music events and club nights.");
        content.SetValue("telephone", "+44 113 244 0900");
        content.SetValue("streetAddress", "2 Whitehall Road");
        content.SetValue("addressLocality", "Leeds");
        content.SetValue("postalCode", "LS1 4AW");
        content.SetValue("addressCountry", "GB");
        TrySetHeroImage(content, "mint-warehouse");
        await SaveAndPublishAsync(content);
    }

    private async Task CreateStadiumPageContent(int parentId, CancellationToken ct)
    {
        var content = _contentService.Create("Elland Road", parentId, "stadiumPage");
        content.SetValue("title", "Elland Road");
        content.SetValue("description", "Home ground of Leeds United Football Club since 1919, with a capacity of over 37,000.");
        content.SetValue("telephone", "+44 113 367 6000");
        content.SetValue("maximumAttendeeCapacity", "37890");
        content.SetValue("streetAddress", "Elland Road");
        content.SetValue("addressLocality", "Leeds");
        content.SetValue("postalCode", "LS11 0ES");
        content.SetValue("addressCountry", "GB");
        TrySetHeroImage(content, "elland-road");
        await SaveAndPublishAsync(content);
    }

    private async Task CreateSkiResortPageContent(int parentId, CancellationToken ct)
    {
        var content = _contentService.Create("Xscape Yorkshire Ski Slope", parentId, "skiResortPage");
        content.SetValue("title", "Xscape Yorkshire Ski Slope");
        content.SetValue("description", "Indoor real snow ski slope near Leeds offering skiing, snowboarding, and lessons for all abilities.");
        content.SetValue("telephone", "+44 1onal 832 700");
        content.SetValue("streetAddress", "Colorado Way, Glasshoughton");
        content.SetValue("addressLocality", "Castleford");
        content.SetValue("postalCode", "WF10 4TA");
        content.SetValue("addressCountry", "GB");
        TrySetHeroImage(content, "xscape-yorkshire-ski-slope");
        await SaveAndPublishAsync(content);
    }

    private async Task CreateGolfCoursePageContent(int parentId, CancellationToken ct)
    {
        var content = _contentService.Create("Moortown Golf Club", parentId, "golfCoursePage");
        content.SetValue("title", "Moortown Golf Club");
        content.SetValue("description", "Historic golf club in North Leeds, venue for the first Ryder Cup on British soil in 1929.");
        content.SetValue("telephone", "+44 113 268 6521");
        content.SetValue("streetAddress", "Harrogate Road, Alwoodley");
        content.SetValue("addressLocality", "Leeds");
        content.SetValue("postalCode", "LS17 7DB");
        content.SetValue("addressCountry", "GB");
        TrySetHeroImage(content, "moortown-golf-club");
        await SaveAndPublishAsync(content);
    }

    private async Task CreateApartmentPageContent(int parentId, CancellationToken ct)
    {
        var content = _contentService.Create("Waterfront Apartment", parentId, "apartmentPage");
        content.SetValue("title", "Waterfront Apartment");
        content.SetValue("description", "Modern two-bedroom apartment overlooking the Leeds-Liverpool canal with open-plan living.");
        content.SetValue("numberOfRooms", "4");
        content.SetValue("floorSize", "75 sqm");
        content.SetValue("petsAllowed", "No");
        content.SetValue("streetAddress", "Granary Wharf");
        content.SetValue("addressLocality", "Leeds");
        content.SetValue("postalCode", "LS1 4BR");
        content.SetValue("addressCountry", "GB");
        TrySetHeroImage(content, "waterfront-apartment");
        await SaveAndPublishAsync(content);
    }

    private async Task CreateHousePageContent(int parentId, CancellationToken ct)
    {
        var content = _contentService.Create("Victorian Terrace Headingley", parentId, "housePage");
        content.SetValue("title", "Victorian Terrace Headingley");
        content.SetValue("description", "Beautifully restored four-bedroom Victorian terrace house in the heart of Headingley.");
        content.SetValue("numberOfRooms", "7");
        content.SetValue("floorSize", "140 sqm");
        content.SetValue("streetAddress", "28 St Michael's Lane");
        content.SetValue("addressLocality", "Leeds");
        content.SetValue("postalCode", "LS6 3AW");
        content.SetValue("addressCountry", "GB");
        TrySetHeroImage(content, "victorian-terrace-headingley");
        await SaveAndPublishAsync(content);
    }

    private async Task CreateLodgingBusinessPageContent(int parentId, CancellationToken ct)
    {
        var content = _contentService.Create("The Calls Boutique Hotel", parentId, "lodgingBusinessPage");
        content.SetValue("title", "The Calls Boutique Hotel");
        content.SetValue("description", "Boutique hotel in a converted 19th-century corn mill on the banks of the River Aire.");
        content.SetValue("telephone", "+44 113 244 0099");
        content.SetValue("starRating", "4");
        content.SetValue("checkinTime", "15:00");
        content.SetValue("checkoutTime", "11:00");
        content.SetValue("streetAddress", "42 The Calls");
        content.SetValue("addressLocality", "Leeds");
        content.SetValue("postalCode", "LS2 7EW");
        content.SetValue("addressCountry", "GB");
        TrySetHeroImage(content, "the-calls-boutique-hotel");
        await SaveAndPublishAsync(content);
    }

    private async Task CreateArticlePageContent(int parentId, CancellationToken ct)
    {
        var content = _contentService.Create("The Future of Structured Data", parentId, "articlePage");
        content.SetValue("title", "The Future of Structured Data");
        content.SetValue("description", "An in-depth look at how Schema.org structured data is evolving and what it means for web developers.");
        content.SetValue("bodyText", "Structured data has become an essential part of modern web development. Search engines rely on it to understand page content and deliver rich results. In this article we explore the latest developments in the Schema.org vocabulary and how tools like SchemeWeaver make adoption easier than ever.");
        content.SetValue("authorName", "Oliver Sheridan");
        content.SetValue("datePublished", "2026-03-15");
        content.SetValue("articleSection", "Technology");
        content.SetValue("wordCount", "1250");
        TrySetHeroImage(content, "the-future-of-structured-data");
        await SaveAndPublishAsync(content);
    }

    private async Task CreatePodcastSeriesPageContent(int parentId, CancellationToken ct)
    {
        var content = _contentService.Create("Schema Matters Podcast", parentId, "podcastSeriesPage");
        content.SetValue("title", "Schema Matters Podcast");
        content.SetValue("description", "Weekly podcast exploring structured data, SEO, and the semantic web with industry experts.");
        content.SetValue("webFeed", "https://schemamatters.example.com/feed.xml");
        content.SetValue("authorName", "Emma Richardson");
        TrySetHeroImage(content, "schema-matters-podcast");
        await SaveAndPublishAsync(content);
    }

    private async Task CreateMusicRecordingPageContent(int parentId, CancellationToken ct)
    {
        var content = _contentService.Create("Northern Lights", parentId, "musicRecordingPage");
        content.SetValue("title", "Northern Lights");
        content.SetValue("description", "Lead single from the album 'Yorkshire Skies' by The Aire Valley Band.");
        content.SetValue("duration", "PT4M32S");
        content.SetValue("byArtist", "The Aire Valley Band");
        content.SetValue("inAlbum", "Yorkshire Skies");
        content.SetValue("datePublished", "2025-11-01");
        TrySetHeroImage(content, "northern-lights");
        await SaveAndPublishAsync(content);
    }

    private async Task CreateOfferPageContent(int parentId, CancellationToken ct)
    {
        var content = _contentService.Create("Spring Sale — 25% Off", parentId, "offerPage");
        content.SetValue("title", "Spring Sale — 25% Off");
        content.SetValue("description", "Save 25% on all SchemeWeaver licences this spring. Limited time offer.");
        content.SetValue("price", "37.50");
        content.SetValue("priceCurrency", "GBP");
        content.SetValue("availability", "InStock");
        content.SetValue("validFrom", "2026-03-01");
        content.SetValue("validThrough", "2026-05-31");
        content.SetValue("itemOffered", "SchemeWeaver Professional Licence");
        TrySetHeroImage(content, "spring-sale-25-off");
        await SaveAndPublishAsync(content);
    }

    private async Task CreateRealEstateListingPageContent(int parentId, CancellationToken ct)
    {
        var content = _contentService.Create("3 Bed Semi-Detached, Roundhay", parentId, "realEstateListingPage");
        content.SetValue("title", "3 Bed Semi-Detached, Roundhay");
        content.SetValue("description", "Spacious three-bedroom semi-detached house with south-facing garden close to Roundhay Park.");
        content.SetValue("price", "325000");
        content.SetValue("priceCurrency", "GBP");
        content.SetValue("datePosted", "2026-02-20");
        content.SetValue("streetAddress", "14 Oakwood Lane");
        content.SetValue("addressLocality", "Leeds");
        content.SetValue("postalCode", "LS8 2PJ");
        content.SetValue("addressCountry", "GB");
        TrySetHeroImage(content, "3-bed-semi-detached-roundhay");
        await SaveAndPublishAsync(content);
    }

    private async Task CreateSingleFamilyResidencePageContent(int parentId, CancellationToken ct)
    {
        var content = _contentService.Create("Detached Family Home, Alwoodley", parentId, "singleFamilyResidencePage");
        content.SetValue("title", "Detached Family Home, Alwoodley");
        content.SetValue("description", "Stunning four-bedroom detached family home with double garage, landscaped gardens, and views over Eccup Reservoir.");
        content.SetValue("numberOfRooms", "8");
        content.SetValue("floorSize", "220 sqm");
        content.SetValue("numberOfBathroomsTotal", "3");
        content.SetValue("yearBuilt", "1998");
        content.SetValue("streetAddress", "42 Wigton Lane");
        content.SetValue("addressLocality", "Leeds");
        content.SetValue("postalCode", "LS17 8SA");
        content.SetValue("addressCountry", "GB");
        TrySetHeroImage(content, "detached-family-home-alwoodley");
        await SaveAndPublishAsync(content);
    }

    private async Task CreateApartmentComplexPageContent(int parentId, CancellationToken ct)
    {
        var content = _contentService.Create("Clarence Dock Apartments", parentId, "apartmentComplexPage");
        content.SetValue("title", "Clarence Dock Apartments");
        content.SetValue("description", "Modern waterside apartment complex with 200 units, residents' gym, concierge, and underground parking beside the Royal Armouries.");
        content.SetValue("numberOfAccommodationUnits", "200");
        content.SetValue("petsAllowed", "Yes — small dogs and cats");
        content.SetValue("telephone", "+44 113 245 8800");
        content.SetValue("streetAddress", "Clarence Dock");
        content.SetValue("addressLocality", "Leeds");
        content.SetValue("postalCode", "LS10 1JR");
        content.SetValue("addressCountry", "GB");
        TrySetHeroImage(content, "clarence-dock-apartments");
        await SaveAndPublishAsync(content);
    }

    private async Task CreateResidencePageContent(int parentId, CancellationToken ct)
    {
        var content = _contentService.Create("Chapel Allerton Townhouse", parentId, "residencePage");
        content.SetValue("title", "Chapel Allerton Townhouse");
        content.SetValue("description", "Elegant three-storey townhouse in the heart of Chapel Allerton with period features and a private courtyard garden.");
        content.SetValue("numberOfRooms", "6");
        content.SetValue("floorSize", "165 sqm");
        content.SetValue("streetAddress", "19 Stainbeck Lane");
        content.SetValue("addressLocality", "Leeds");
        content.SetValue("postalCode", "LS7 3QR");
        content.SetValue("addressCountry", "GB");
        TrySetHeroImage(content, "chapel-allerton-townhouse");
        await SaveAndPublishAsync(content);
    }

    private async Task CreateSuitePageContent(int parentId, CancellationToken ct)
    {
        var content = _contentService.Create("Queens Hotel Penthouse Suite", parentId, "suitePage");
        content.SetValue("title", "Queens Hotel Penthouse Suite");
        content.SetValue("description", "Luxury penthouse suite overlooking City Square with king-size bed, marble bathroom, and private balcony.");
        content.SetValue("numberOfRooms", "3");
        content.SetValue("floorSize", "85 sqm");
        content.SetValue("bedType", "King");
        content.SetValue("occupancy", "2 adults");
        content.SetValue("streetAddress", "City Square");
        content.SetValue("addressLocality", "Leeds");
        content.SetValue("postalCode", "LS1 1PJ");
        content.SetValue("addressCountry", "GB");
        TrySetHeroImage(content, "queens-hotel-penthouse-suite");
        await SaveAndPublishAsync(content);
    }

    private async Task CreateGatedResidenceCommunityPageContent(int parentId, CancellationToken ct)
    {
        var content = _contentService.Create("Bramham Park Gated Community", parentId, "gatedResidenceCommunityPage");
        content.SetValue("title", "Bramham Park Gated Community");
        content.SetValue("description", "Exclusive gated development of 12 luxury homes set within the Bramham Park estate with 24-hour security and communal grounds.");
        content.SetValue("numberOfAccommodationUnits", "12");
        content.SetValue("petsAllowed", "Yes");
        content.SetValue("streetAddress", "Bramham Park Estate");
        content.SetValue("addressLocality", "Wetherby");
        content.SetValue("postalCode", "LS23 6ND");
        content.SetValue("addressCountry", "GB");
        TrySetHeroImage(content, "bramham-park-gated-community");
        await SaveAndPublishAsync(content);
    }

    private async Task CreateAccommodationPageContent(int parentId, CancellationToken ct)
    {
        var content = _contentService.Create("Kirkstall Student Accommodation", parentId, "accommodationPage");
        content.SetValue("title", "Kirkstall Student Accommodation");
        content.SetValue("description", "Purpose-built student accommodation with en-suite rooms, shared kitchens, study spaces, and on-site laundry.");
        content.SetValue("numberOfRooms", "150");
        content.SetValue("floorSize", "18 sqm");
        content.SetValue("petsAllowed", "No");
        content.SetValue("tourBookingPage", "https://kirkstall-halls.example.com/book-tour");
        content.SetValue("streetAddress", "Abbey Road");
        content.SetValue("addressLocality", "Leeds");
        content.SetValue("postalCode", "LS5 3EH");
        content.SetValue("addressCountry", "GB");
        TrySetHeroImage(content, "kirkstall-student-accommodation");
        await SaveAndPublishAsync(content);
    }

    private async Task CreateDiagnosticLabPageContent(int parentId, CancellationToken ct)
    {
        var content = _contentService.Create("Leeds PathLab Diagnostics", parentId, "diagnosticLabPage");
        content.SetValue("title", "Leeds PathLab Diagnostics");
        content.SetValue("description", "NHS-accredited diagnostic laboratory providing blood tests, pathology services, and rapid COVID testing.");
        content.SetValue("telephone", "+44 113 392 5500");
        content.SetValue("streetAddress", "St James's University Hospital, Beckett Street");
        content.SetValue("addressLocality", "Leeds");
        content.SetValue("postalCode", "LS9 7TF");
        content.SetValue("addressCountry", "GB");
        TrySetHeroImage(content, "leeds-pathlab-diagnostics");
        await SaveAndPublishAsync(content);
    }

    private async Task CreateEducationalOrgPageContent(int parentId, CancellationToken ct)
    {
        var content = _contentService.Create("Leeds Arts University", parentId, "educationalOrgPage");
        content.SetValue("title", "Leeds Arts University");
        content.SetValue("description", "Specialist arts university offering undergraduate and postgraduate degrees in art, design, and creative disciplines.");
        content.SetValue("telephone", "+44 113 202 8000");
        content.SetValue("streetAddress", "Blenheim Walk");
        content.SetValue("addressLocality", "Leeds");
        content.SetValue("postalCode", "LS2 9AQ");
        content.SetValue("addressCountry", "GB");
        TrySetHeroImage(content, "leeds-arts-university");
        await SaveAndPublishAsync(content);
    }

    private async Task PublishContent(IContent content, CancellationToken cancellationToken)
    {
        try
        {
            var cultureSchedules = new List<CulturePublishScheduleModel>
            {
                new() { Culture = null, Schedule = null },
            };
            await _contentPublishingService.PublishAsync(content.Key, cultureSchedules, Constants.Security.SuperUserKey);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "TestDataSeeder: Could not publish {Name}", content.Name);
        }
    }

    // ──────────────────────────────────────────────────────────────
    // Block List JSON helpers
    // ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Builds a Block List JSON value string in Umbraco 17 format.
    /// Each item in <paramref name="blockItems"/> is a dictionary of property alias to value
    /// for a single block of the given element type.
    /// </summary>
    private static string BuildBlockListJson(Guid elementTypeKey, Dictionary<string, object>[] blockItems)
    {
        var layoutItems = new List<object>();
        var contentDataItems = new List<object>();
        var exposeItems = new List<object>();

        foreach (var block in blockItems)
        {
            var blockKey = Guid.NewGuid();

            layoutItems.Add(new { contentKey = blockKey, settingsKey = (Guid?)null });

            var values = block.Select(kvp => new
            {
                alias = kvp.Key,
                value = kvp.Value,
                culture = (string?)null,
                segment = (string?)null,
            }).ToList();

            contentDataItems.Add(new
            {
                key = blockKey,
                contentTypeKey = elementTypeKey,
                values,
            });

            exposeItems.Add(new
            {
                contentKey = blockKey,
                culture = (string?)null,
                segment = (string?)null,
            });
        }

        var blockValue = new
        {
            layout = new Dictionary<string, object>
            {
                ["Umbraco.BlockList"] = layoutItems,
            },
            contentData = contentDataItems,
            settingsData = Array.Empty<object>(),
            expose = exposeItems,
        };

        return JsonSerializer.Serialize(blockValue, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        });
    }

    /// <summary>
    /// Builds a Block Grid JSON value string. Each item in <paramref name="blocks"/> pairs an
    /// element type key with the block's property values. Layout uses a single 12-column row
    /// per block (simple vertical stack, sufficient for seeding demo content).
    /// </summary>
    private static string BuildBlockGridJson(IEnumerable<(Guid elementTypeKey, Dictionary<string, object> values)> blocks)
    {
        var layoutItems = new List<object>();
        var contentDataItems = new List<object>();
        var exposeItems = new List<object>();

        foreach (var (elementTypeKey, values) in blocks)
        {
            var blockKey = Guid.NewGuid();

            layoutItems.Add(new
            {
                contentKey = blockKey,
                settingsKey = (Guid?)null,
                columnSpan = 12,
                rowSpan = 1,
                areas = Array.Empty<object>(),
            });

            var blockValues = values.Select(kvp => new
            {
                alias = kvp.Key,
                value = kvp.Value,
                culture = (string?)null,
                segment = (string?)null,
            }).ToList();

            contentDataItems.Add(new
            {
                key = blockKey,
                contentTypeKey = elementTypeKey,
                values = blockValues,
            });

            exposeItems.Add(new
            {
                contentKey = blockKey,
                culture = (string?)null,
                segment = (string?)null,
            });
        }

        var blockValue = new
        {
            layout = new Dictionary<string, object>
            {
                ["Umbraco.BlockGrid"] = layoutItems,
            },
            contentData = contentDataItems,
            settingsData = Array.Empty<object>(),
            expose = exposeItems,
        };

        return JsonSerializer.Serialize(blockValue, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        });
    }

    // ──────────────────────────────────────────────────────────────
    // Schema mapping seeding
    // ──────────────────────────────────────────────────────────────

    private void SeedSchemaMappings(
        IContentType blogArticleCt, IContentType productPageCt, IContentType faqPageCt,
        IContentType eventPageCt, IContentType recipePageCt,
        IContentType homePageCt, IContentType aboutPageCt,
        IContentType blogListingCt, IContentType productListingCt,
        IContentType eventListingCt, IContentType recipeListingCt,
        IContentType newsArticleCt, IContentType techArticleCt,
        IContentType softwarePageCt, IContentType coursePageCt,
        IContentType howToPageCt, IContentType videoPageCt,
        IContentType jobPostingPageCt, IContentType profilePageCt,
        IContentType locationPageCt, IContentType restaurantPageCt,
        IContentType bookPageCt, IContentType contactPageCt,
        IContentType vehiclePageCt, IContentType financialProductPageCt,
        IContentType individualProductPageCt, IContentType productModelPageCt,
        IContentType musicEventPageCt, IContentType sportsEventPageCt,
        IContentType businessEventPageCt, IContentType foodEventPageCt,
        IContentType festivalPageCt, IContentType educationEventPageCt,
        IContentType organisationListingCt, IContentType corporationPageCt,
        IContentType sportsTeamPageCt, IContentType airlinePageCt, IContentType ngoPageCt,
        IContentType placesListingCt, IContentType hotelPageCt,
        IContentType storePageCt, IContentType hospitalPageCt, IContentType gymPageCt,
        IContentType creativeListingCt, IContentType moviePageCt,
        IContentType musicAlbumPageCt, IContentType podcastEpisodePageCt,
        IContentType photographPageCt, IContentType datasetPageCt,
        IContentType mobileAppPageCt, IContentType webAppPageCt,
        IContentType shopsListingCt, IContentType bookStorePageCt, IContentType electronicsStorePageCt, IContentType clothingStorePageCt, IContentType groceryStorePageCt, IContentType furnitureStorePageCt, IContentType jewelryStorePageCt,
        IContentType diningListingCt, IContentType bakeryPageCt, IContentType cafePageCt, IContentType barPageCt, IContentType fastFoodPageCt, IContentType wineryPageCt, IContentType breweryPageCt,
        IContentType travelListingCt, IContentType flightReservationPageCt, IContentType lodgingReservationPageCt, IContentType eventReservationPageCt, IContentType rentalCarPageCt, IContentType flightPageCt, IContentType touristAttractionPageCt, IContentType bedAndBreakfastPageCt, IContentType campgroundPageCt,
        IContentType healthcareListingCt, IContentType drugPageCt, IContentType medicalConditionPageCt, IContentType physicianPageCt, IContentType pharmacyPageCt, IContentType dentistPageCt, IContentType medicalClinicPageCt,
        IContentType automotiveListingCt, IContentType carPageCt, IContentType motorcyclePageCt, IContentType autoDealerPageCt, IContentType autoRepairPageCt,
        IContentType theaterEventPageCt, IContentType screeningEventPageCt,
        IContentType entertainmentListingCt, IContentType musicGroupPageCt, IContentType zooPageCt, IContentType museumPageCt, IContentType amusementParkPageCt,
        IContentType servicesListingCt, IContentType attorneyPageCt, IContentType accountingPageCt, IContentType realEstatePageCt, IContentType insurancePageCt, IContentType travelAgencyPageCt,
        IContentType educationListingCt, IContentType universityPageCt, IContentType schoolPageCt, IContentType courseInstancePageCt,
        IContentType blogPageCt, IContentType liveBlogPageCt,
        IContentType reportPageCt, IContentType videoGamePageCt, IContentType sourceCodePageCt,
        IContentType occupationPageCt,
        IContentType servicePageCt, IContentType professionalServicePageCt, IContentType legalServicePageCt, IContentType financialServicePageCt, IContentType governmentOrgPageCt,
        // Legal
        IContentType legalListingCt, IContentType notaryPageCt, IContentType courthousePageCt, IContentType legislationPageCt,
        IContentType libraryPageCt, IContentType movieTheaterPageCt, IContentType nightClubPageCt, IContentType stadiumPageCt, IContentType skiResortPageCt, IContentType golfCoursePageCt,
        IContentType apartmentPageCt, IContentType housePageCt, IContentType lodgingBusinessPageCt,
        IContentType articlePageCt, IContentType podcastSeriesPageCt, IContentType musicRecordingPageCt,
        IContentType offerPageCt,
        IContentType diagnosticLabPageCt,
        IContentType educationalOrgPageCt,
        IContentType webPageCt,
        IContentType realEstateListingPageCt,
        IContentType propertyListingCt, IContentType singleFamilyResidencePageCt, IContentType apartmentComplexPageCt, IContentType residencePageCt, IContentType suitePageCt, IContentType gatedResidenceCommunityPageCt, IContentType accommodationPageCt,
        IContentType organisationParentCt, IContentType localBusinessChildCt, IContentType departmentPageCt,
        // Landing page (BlockGrid demo)
        IContentType landingPageCt,
        // Variant (culture-varying)
        IContentType variantArticleCt)
    {
        using var scope = _scopeFactory.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<ISchemaMappingRepository>();

        // Existing mappings
        SeedFaqPageMapping(faqPageCt, repo);
        SeedProductPageMapping(productPageCt, repo);
        SeedRecipePageMapping(recipePageCt, repo);
        SeedBlogArticleMapping(blogArticleCt, repo);
        SeedEventPageMapping(eventPageCt, repo);

        // Variant article mapping
        SeedSimpleMapping(repo, variantArticleCt, "Article", ("Name", "title"), ("ArticleBody", "bodyText"), ("Url", "__url"));

        // New mappings — simple property types
        // Home page gets a custom seed (not SeedInheritedMapping) so we can add the
        // BlockGrid mainEntity wiring that promotes the home content grid into JSON-LD.
        SeedHomePageMapping(homePageCt, repo);
        SeedSimpleMapping(repo, aboutPageCt, "AboutPage", ("Name", "title"), ("Description", "description"), ("Url", "__url"), ("DateModified", "__updateDate"));
        SeedSimpleMapping(repo, newsArticleCt, "NewsArticle", ("Headline", "title"), ("Description", "description"), ("ArticleBody", "bodyText"), ("DatePublished", "publishDate"), ("Keywords", "keywords"), ("Dateline", "dateline"), ("Url", "__url"));
        SeedSimpleMapping(repo, techArticleCt, "TechArticle", ("Headline", "title"), ("Description", "description"), ("ArticleBody", "bodyText"), ("DatePublished", "publishDate"), ("ProficiencyLevel", "proficiencyLevel"), ("Url", "__url"));
        SeedSimpleMapping(repo, softwarePageCt, "SoftwareApplication", ("Name", "title"), ("Description", "description"), ("Image", "heroImage"), ("ApplicationCategory", "applicationCategory"), ("OperatingSystem", "operatingSystem"), ("SoftwareVersion", "softwareVersion"), ("DownloadUrl", "downloadUrl"), ("Url", "__url"));
        SeedSimpleMapping(repo, coursePageCt, "Course", ("Name", "title"), ("Description", "description"), ("CourseCode", "courseCode"), ("Url", "__url"));
        SeedSimpleMapping(repo, videoPageCt, "VideoObject", ("Name", "title"), ("Description", "description"), ("ThumbnailUrl", "thumbnailUrl"), ("UploadDate", "uploadDate"), ("Duration", "duration"), ("ContentUrl", "contentUrl"), ("EmbedUrl", "embedUrl"), ("Url", "__url"));
        SeedSimpleMapping(repo, jobPostingPageCt, "JobPosting", ("Title", "title"), ("Description", "description"), ("DatePosted", "datePosted"), ("ValidThrough", "validThrough"), ("EmploymentType", "employmentType"), ("BaseSalary", "salary"), ("Url", "__url"));
        SeedSimpleMapping(repo, profilePageCt, "ProfilePage", ("Name", "title"), ("Description", "description"), ("Url", "__url"));
        SeedSimpleMapping(repo, bookPageCt, "Book", ("Name", "title"), ("Description", "description"), ("Isbn", "isbn"), ("BookFormat", "bookFormat"), ("NumberOfPages", "numberOfPages"), ("DatePublished", "datePublished"), ("Url", "__url"));
        SeedSimpleMapping(repo, contactPageCt, "ContactPage", ("Name", "title"), ("Telephone", "telephone"), ("Email", "email"), ("Url", "__url"));
        SeedSimpleMapping(repo, locationPageCt, "LocalBusiness", ("Name", "title"), ("Description", "description"), ("Telephone", "telephone"), ("Email", "email"), ("PriceRange", "priceRange"), ("Url", "__url"));
        SeedSimpleMapping(repo, restaurantPageCt, "Restaurant", ("Name", "title"), ("Description", "description"), ("Telephone", "telephone"), ("ServesCuisine", "servesCuisine"), ("Menu", "menu"), ("PriceRange", "priceRange"), ("Url", "__url"));

        // HowTo — needs blockContent for steps and tools
        try
        {
            SeedHowToMapping(howToPageCt, repo);
            _logger.LogInformation("TestDataSeeder: HowTo mapping seeded successfully");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "TestDataSeeder: Failed to seed HowTo mapping");
        }

        // Listing pages
        SeedSimpleMapping(repo, blogListingCt, "CollectionPage", ("Name", "title"), ("Description", "description"), ("Url", "__url"));
        SeedSimpleMapping(repo, productListingCt, "CollectionPage", ("Name", "title"), ("Description", "description"), ("Url", "__url"));
        SeedSimpleMapping(repo, eventListingCt, "CollectionPage", ("Name", "title"), ("Description", "description"), ("Url", "__url"));
        SeedSimpleMapping(repo, recipeListingCt, "CollectionPage", ("Name", "title"), ("Description", "description"), ("Url", "__url"));

        // New product subtype mappings
        SeedSimpleMapping(repo, vehiclePageCt, "Vehicle", ("Name", "title"), ("Description", "description"), ("Image", "heroImage"), ("Brand", "brand"), ("Model", "model"), ("FuelType", "fuelType"), ("MileageFromOdometer", "mileageFromOdometer"), ("VehicleEngine", "vehicleEngine"), ("Color", "color"), ("NumberOfDoors", "numberOfDoors"), ("Url", "__url"));
        SeedSimpleMapping(repo, financialProductPageCt, "FinancialProduct", ("Name", "title"), ("Description", "description"), ("Image", "heroImage"), ("FeesAndCommissionsSpecification", "feesAndCommissionsSpecification"), ("InterestRate", "interestRate"), ("AnnualPercentageRate", "annualPercentageRate"), ("Url", "__url"));
        SeedSimpleMapping(repo, individualProductPageCt, "IndividualProduct", ("Name", "title"), ("Description", "description"), ("Image", "heroImage"), ("SerialNumber", "serialNumber"), ("Sku", "sku"), ("Color", "color"), ("Weight", "weight"), ("Url", "__url"));
        SeedSimpleMapping(repo, productModelPageCt, "ProductModel", ("Name", "title"), ("Description", "description"), ("Image", "heroImage"), ("Url", "__url"));

        // New event subtype mappings
        SeedSimpleMapping(repo, musicEventPageCt, "MusicEvent", ("Name", "title"), ("Description", "description"), ("Image", "heroImage"), ("Performer", "performer"), ("StartDate", "startDate"), ("EndDate", "endDate"), ("Url", "__url"));
        SeedSimpleMapping(repo, sportsEventPageCt, "SportsEvent", ("Name", "title"), ("Description", "description"), ("Image", "heroImage"), ("Competitor", "competitor"), ("StartDate", "startDate"), ("Sport", "sport"), ("Url", "__url"));
        SeedSimpleMapping(repo, businessEventPageCt, "BusinessEvent", ("Name", "title"), ("Description", "description"), ("Image", "heroImage"), ("StartDate", "startDate"), ("Sponsor", "sponsor"), ("Url", "__url"));
        SeedSimpleMapping(repo, foodEventPageCt, "FoodEvent", ("Name", "title"), ("Description", "description"), ("Image", "heroImage"), ("StartDate", "startDate"), ("Url", "__url"));
        SeedSimpleMapping(repo, festivalPageCt, "Festival", ("Name", "title"), ("Description", "description"), ("Image", "heroImage"), ("StartDate", "startDate"), ("EndDate", "endDate"), ("Url", "__url"));
        SeedSimpleMapping(repo, educationEventPageCt, "EducationEvent", ("Name", "title"), ("Description", "description"), ("Image", "heroImage"), ("StartDate", "startDate"), ("Url", "__url"));

        // New organisation subtype mappings
        SeedSimpleMapping(repo, organisationListingCt, "CollectionPage", ("Name", "title"), ("Description", "description"), ("Url", "__url"));
        SeedSimpleMapping(repo, corporationPageCt, "Corporation", ("Name", "title"), ("Description", "description"), ("TickerSymbol", "tickerSymbol"), ("LegalName", "legalName"), ("FoundingDate", "foundingDate"), ("NumberOfEmployees", "numberOfEmployees"), ("Url", "__url"));
        SeedSimpleMapping(repo, sportsTeamPageCt, "SportsTeam", ("Name", "title"), ("Description", "description"), ("Sport", "sport"), ("Coach", "coach"), ("Url", "__url"));
        SeedSimpleMapping(repo, airlinePageCt, "Airline", ("Name", "title"), ("Description", "description"), ("IataCode", "iataCode"), ("FoundingDate", "foundingDate"), ("Url", "__url"));
        SeedSimpleMapping(repo, ngoPageCt, "NGO", ("Name", "title"), ("Description", "description"), ("FoundingDate", "foundingDate"), ("AreaServed", "areaServed"), ("Url", "__url"));

        // New places subtype mappings
        SeedSimpleMapping(repo, placesListingCt, "CollectionPage", ("Name", "title"), ("Description", "description"), ("Url", "__url"));
        SeedSimpleMapping(repo, hotelPageCt, "Hotel", ("Name", "title"), ("Description", "description"), ("Telephone", "telephone"), ("StarRating", "starRating"), ("CheckinTime", "checkinTime"), ("CheckoutTime", "checkoutTime"), ("Url", "__url"));
        SeedSimpleMapping(repo, storePageCt, "Store", ("Name", "title"), ("Description", "description"), ("Telephone", "telephone"), ("PaymentAccepted", "paymentAccepted"), ("Url", "__url"));
        SeedSimpleMapping(repo, hospitalPageCt, "Hospital", ("Name", "title"), ("Description", "description"), ("Telephone", "telephone"), ("MedicalSpecialty", "medicalSpecialty"), ("Url", "__url"));
        SeedSimpleMapping(repo, gymPageCt, "ExerciseGym", ("Name", "title"), ("Description", "description"), ("Telephone", "telephone"), ("OpeningHours", "openingHours"), ("Url", "__url"));

        // New creative subtype mappings
        SeedSimpleMapping(repo, creativeListingCt, "CollectionPage", ("Name", "title"), ("Description", "description"), ("Url", "__url"));
        SeedSimpleMapping(repo, moviePageCt, "Movie", ("Name", "title"), ("Description", "description"), ("Director", "director"), ("Actor", "actor"), ("Duration", "duration"), ("DateCreated", "dateCreated"), ("Url", "__url"));
        SeedSimpleMapping(repo, musicAlbumPageCt, "MusicAlbum", ("Name", "title"), ("Description", "description"), ("ByArtist", "byArtist"), ("NumTracks", "numTracks"), ("DatePublished", "datePublished"), ("Url", "__url"));
        SeedSimpleMapping(repo, podcastEpisodePageCt, "Episode", ("Name", "title"), ("Description", "description"), ("Duration", "duration"), ("DatePublished", "datePublished"), ("Url", "__url"));
        SeedSimpleMapping(repo, photographPageCt, "Photograph", ("Name", "title"), ("Description", "description"), ("Creator", "creator"), ("DateCreated", "dateCreated"), ("ContentUrl", "contentUrl"), ("Url", "__url"));
        SeedSimpleMapping(repo, datasetPageCt, "Dataset", ("Name", "title"), ("Description", "description"), ("Distribution", "distribution"), ("License", "license"), ("Url", "__url"));

        // New standalone page mappings
        SeedSimpleMapping(repo, mobileAppPageCt, "MobileApplication", ("Name", "title"), ("Description", "description"), ("OperatingSystem", "operatingSystem"), ("ApplicationCategory", "applicationCategory"), ("DownloadUrl", "downloadUrl"), ("SoftwareVersion", "softwareVersion"), ("Url", "__url"));
        SeedSimpleMapping(repo, webAppPageCt, "WebApplication", ("Name", "title"), ("Description", "description"), ("BrowserRequirements", "browserRequirements"), ("ApplicationCategory", "applicationCategory"), ("Url", "__url"));

        // New shops subtype mappings
        SeedSimpleMapping(repo, shopsListingCt, "CollectionPage", ("Name", "title"), ("Description", "description"), ("Url", "__url"));
        SeedSimpleMapping(repo, bookStorePageCt, "BookStore", ("Name", "title"), ("Description", "description"), ("Telephone", "telephone"), ("Url", "__url"));
        SeedSimpleMapping(repo, electronicsStorePageCt, "ElectronicsStore", ("Name", "title"), ("Description", "description"), ("Telephone", "telephone"), ("Url", "__url"));
        SeedSimpleMapping(repo, clothingStorePageCt, "ClothingStore", ("Name", "title"), ("Description", "description"), ("Telephone", "telephone"), ("Url", "__url"));
        SeedSimpleMapping(repo, groceryStorePageCt, "GroceryStore", ("Name", "title"), ("Description", "description"), ("Telephone", "telephone"), ("Url", "__url"));
        SeedSimpleMapping(repo, furnitureStorePageCt, "FurnitureStore", ("Name", "title"), ("Description", "description"), ("Telephone", "telephone"), ("Url", "__url"));
        SeedSimpleMapping(repo, jewelryStorePageCt, "JewelryStore", ("Name", "title"), ("Description", "description"), ("Telephone", "telephone"), ("Url", "__url"));

        // New dining subtype mappings
        SeedSimpleMapping(repo, diningListingCt, "CollectionPage", ("Name", "title"), ("Description", "description"), ("Url", "__url"));
        SeedSimpleMapping(repo, bakeryPageCt, "Bakery", ("Name", "title"), ("Description", "description"), ("Telephone", "telephone"), ("ServesCuisine", "servesCuisine"), ("Url", "__url"));
        SeedSimpleMapping(repo, cafePageCt, "CafeOrCoffeeShop", ("Name", "title"), ("Description", "description"), ("Telephone", "telephone"), ("Url", "__url"));
        SeedSimpleMapping(repo, barPageCt, "BarOrPub", ("Name", "title"), ("Description", "description"), ("Telephone", "telephone"), ("Url", "__url"));
        SeedSimpleMapping(repo, fastFoodPageCt, "FastFoodRestaurant", ("Name", "title"), ("Description", "description"), ("Telephone", "telephone"), ("ServesCuisine", "servesCuisine"), ("Url", "__url"));
        SeedSimpleMapping(repo, wineryPageCt, "Winery", ("Name", "title"), ("Description", "description"), ("Telephone", "telephone"), ("Url", "__url"));
        SeedSimpleMapping(repo, breweryPageCt, "Brewery", ("Name", "title"), ("Description", "description"), ("Telephone", "telephone"), ("Url", "__url"));

        // New travel subtype mappings
        SeedSimpleMapping(repo, travelListingCt, "CollectionPage", ("Name", "title"), ("Description", "description"), ("Url", "__url"));
        SeedSimpleMapping(repo, flightReservationPageCt, "FlightReservation", ("Name", "title"), ("Description", "description"), ("ReservationId", "reservationId"), ("Url", "__url"));
        SeedSimpleMapping(repo, lodgingReservationPageCt, "LodgingReservation", ("Name", "title"), ("Description", "description"), ("ReservationId", "reservationId"), ("CheckinTime", "checkinTime"), ("CheckoutTime", "checkoutTime"), ("LodgingUnitDescription", "lodgingUnitDescription"), ("Url", "__url"));
        SeedSimpleMapping(repo, eventReservationPageCt, "EventReservation", ("Name", "title"), ("Description", "description"), ("ReservationId", "reservationId"), ("ReservationFor", "reservationFor"), ("Url", "__url"));
        SeedSimpleMapping(repo, rentalCarPageCt, "RentalCarReservation", ("Name", "title"), ("Description", "description"), ("ReservationId", "reservationId"), ("PickupLocation", "pickupLocation"), ("PickupTime", "pickupTime"), ("Url", "__url"));
        SeedSimpleMapping(repo, flightPageCt, "Flight", ("Name", "title"), ("Description", "description"), ("FlightNumber", "flightNumber"), ("DepartureAirport", "departureAirport"), ("ArrivalAirport", "arrivalAirport"), ("DepartureTime", "departureTime"), ("ArrivalTime", "arrivalTime"), ("Url", "__url"));
        SeedSimpleMapping(repo, touristAttractionPageCt, "TouristAttraction", ("Name", "title"), ("Description", "description"), ("Telephone", "telephone"), ("Url", "__url"));
        SeedSimpleMapping(repo, bedAndBreakfastPageCt, "BedAndBreakfast", ("Name", "title"), ("Description", "description"), ("Telephone", "telephone"), ("StarRating", "starRating"), ("Url", "__url"));
        SeedSimpleMapping(repo, campgroundPageCt, "Campground", ("Name", "title"), ("Description", "description"), ("Telephone", "telephone"), ("Url", "__url"));

        // New healthcare subtype mappings
        SeedSimpleMapping(repo, healthcareListingCt, "CollectionPage", ("Name", "title"), ("Description", "description"), ("Url", "__url"));
        SeedSimpleMapping(repo, drugPageCt, "Drug", ("Name", "title"), ("Description", "description"), ("ActiveIngredient", "activeIngredient"), ("DosageForm", "dosageForm"), ("AdministrationRoute", "administrationRoute"), ("PrescriptionStatus", "prescriptionStatus"), ("Url", "__url"));
        SeedSimpleMapping(repo, medicalConditionPageCt, "MedicalCondition", ("Name", "title"), ("Description", "description"), ("PossibleTreatment", "possibleTreatment"), ("RiskFactor", "riskFactor"), ("SignOrSymptom", "signOrSymptom"), ("Url", "__url"));
        SeedSimpleMapping(repo, physicianPageCt, "Physician", ("Name", "title"), ("Description", "description"), ("Telephone", "telephone"), ("MedicalSpecialty", "medicalSpecialty"), ("Url", "__url"));
        SeedSimpleMapping(repo, pharmacyPageCt, "Pharmacy", ("Name", "title"), ("Description", "description"), ("Telephone", "telephone"), ("Url", "__url"));
        SeedSimpleMapping(repo, dentistPageCt, "Dentist", ("Name", "title"), ("Description", "description"), ("Telephone", "telephone"), ("Url", "__url"));
        SeedSimpleMapping(repo, medicalClinicPageCt, "MedicalClinic", ("Name", "title"), ("Description", "description"), ("Telephone", "telephone"), ("MedicalSpecialty", "medicalSpecialty"), ("Url", "__url"));

        // New automotive subtype mappings
        SeedSimpleMapping(repo, automotiveListingCt, "CollectionPage", ("Name", "title"), ("Description", "description"), ("Url", "__url"));
        SeedSimpleMapping(repo, carPageCt, "Car", ("Name", "title"), ("Description", "description"), ("Brand", "brand"), ("Model", "model"), ("FuelType", "fuelType"), ("MileageFromOdometer", "mileageFromOdometer"), ("Color", "color"), ("NumberOfDoors", "numberOfDoors"), ("VehicleTransmission", "vehicleTransmission"), ("Url", "__url"));
        SeedSimpleMapping(repo, motorcyclePageCt, "Motorcycle", ("Name", "title"), ("Description", "description"), ("Brand", "brand"), ("Model", "model"), ("FuelType", "fuelType"), ("MileageFromOdometer", "mileageFromOdometer"), ("Color", "color"), ("Url", "__url"));
        SeedSimpleMapping(repo, autoDealerPageCt, "AutoDealer", ("Name", "title"), ("Description", "description"), ("Telephone", "telephone"), ("Url", "__url"));
        SeedSimpleMapping(repo, autoRepairPageCt, "AutoRepair", ("Name", "title"), ("Description", "description"), ("Telephone", "telephone"), ("Url", "__url"));

        // New event subtype mappings (additional)
        SeedSimpleMapping(repo, theaterEventPageCt, "TheaterEvent", ("Name", "title"), ("Description", "description"), ("Image", "heroImage"), ("Performer", "performer"), ("StartDate", "startDate"), ("Url", "__url"));
        SeedSimpleMapping(repo, screeningEventPageCt, "ScreeningEvent", ("Name", "title"), ("Description", "description"), ("Image", "heroImage"), ("StartDate", "startDate"), ("WorkPresented", "workPresented"), ("Url", "__url"));

        // New entertainment subtype mappings
        SeedSimpleMapping(repo, entertainmentListingCt, "CollectionPage", ("Name", "title"), ("Description", "description"), ("Url", "__url"));
        SeedSimpleMapping(repo, musicGroupPageCt, "MusicGroup", ("Name", "title"), ("Description", "description"), ("Genre", "genre"), ("FoundingDate", "foundingDate"), ("Url", "__url"));
        SeedSimpleMapping(repo, zooPageCt, "Zoo", ("Name", "title"), ("Description", "description"), ("Telephone", "telephone"), ("Url", "__url"));
        SeedSimpleMapping(repo, museumPageCt, "Museum", ("Name", "title"), ("Description", "description"), ("Telephone", "telephone"), ("Url", "__url"));
        SeedSimpleMapping(repo, amusementParkPageCt, "AmusementPark", ("Name", "title"), ("Description", "description"), ("Telephone", "telephone"), ("Url", "__url"));

        // New services subtype mappings
        SeedSimpleMapping(repo, servicesListingCt, "CollectionPage", ("Name", "title"), ("Description", "description"), ("Url", "__url"));
        SeedSimpleMapping(repo, attorneyPageCt, "Attorney", ("Name", "title"), ("Description", "description"), ("Telephone", "telephone"), ("Url", "__url"));
        SeedSimpleMapping(repo, accountingPageCt, "AccountingService", ("Name", "title"), ("Description", "description"), ("Telephone", "telephone"), ("Url", "__url"));
        SeedSimpleMapping(repo, realEstatePageCt, "RealEstateAgent", ("Name", "title"), ("Description", "description"), ("Telephone", "telephone"), ("Url", "__url"));
        SeedSimpleMapping(repo, insurancePageCt, "InsuranceAgency", ("Name", "title"), ("Description", "description"), ("Telephone", "telephone"), ("Url", "__url"));
        SeedSimpleMapping(repo, travelAgencyPageCt, "TravelAgency", ("Name", "title"), ("Description", "description"), ("Telephone", "telephone"), ("Url", "__url"));

        // New education subtype mappings
        SeedSimpleMapping(repo, educationListingCt, "CollectionPage", ("Name", "title"), ("Description", "description"), ("Url", "__url"));
        SeedSimpleMapping(repo, universityPageCt, "CollegeOrUniversity", ("Name", "title"), ("Description", "description"), ("Telephone", "telephone"), ("FoundingDate", "foundingDate"), ("Url", "__url"));
        SeedSimpleMapping(repo, schoolPageCt, "School", ("Name", "title"), ("Description", "description"), ("Telephone", "telephone"), ("Url", "__url"));
        SeedSimpleMapping(repo, courseInstancePageCt, "CourseInstance", ("Name", "title"), ("Description", "description"), ("StartDate", "startDate"), ("EndDate", "endDate"), ("Instructor", "instructor"), ("Url", "__url"));

        // New blog subtype mappings (additional)
        SeedSimpleMapping(repo, blogPageCt, "Blog", ("Name", "title"), ("Description", "description"), ("Url", "__url"));
        SeedSimpleMapping(repo, liveBlogPageCt, "LiveBlogPosting", ("Headline", "title"), ("Description", "description"), ("ArticleBody", "bodyText"), ("DatePublished", "publishDate"), ("CoverageStartTime", "coverageStartTime"), ("CoverageEndTime", "coverageEndTime"), ("Url", "__url"));

        // New creative subtype mappings (additional)
        SeedSimpleMapping(repo, reportPageCt, "Report", ("Name", "title"), ("Description", "description"), ("ArticleBody", "bodyText"), ("DatePublished", "publishDate"), ("Url", "__url"));
        SeedSimpleMapping(repo, videoGamePageCt, "VideoGame", ("Name", "title"), ("Description", "description"), ("ApplicationCategory", "applicationCategory"), ("OperatingSystem", "operatingSystem"), ("GamePlatform", "gamePlatform"), ("Genre", "genre"), ("Url", "__url"));
        SeedSimpleMapping(repo, sourceCodePageCt, "SoftwareSourceCode", ("Name", "title"), ("Description", "description"), ("ProgrammingLanguage", "programmingLanguage"), ("RuntimePlatform", "runtimePlatform"), ("CodeRepository", "codeRepository"), ("Url", "__url"));

        // New standalone mapping
        SeedSimpleMapping(repo, occupationPageCt, "Occupation", ("Name", "title"), ("Description", "description"), ("OccupationalCategory", "occupationalCategory"), ("EstimatedSalary", "estimatedSalary"), ("Skills", "skills"), ("Url", "__url"));

        // New expanded schema types
        SeedSimpleMapping(repo, webPageCt, "WebPage", ("Name", "title"), ("Description", "description"), ("Url", "__url"));
        SeedSimpleMapping(repo, servicePageCt, "Service", ("Name", "title"), ("Description", "description"), ("ServiceType", "serviceType"), ("Provider", "provider"), ("AreaServed", "areaServed"), ("Url", "__url"));
        SeedSimpleMapping(repo, offerPageCt, "Offer", ("Name", "title"), ("Description", "description"), ("Image", "heroImage"), ("Price", "price"), ("PriceCurrency", "priceCurrency"), ("Availability", "availability"), ("Url", "__url"));
        SeedSimpleMapping(repo, professionalServicePageCt, "ProfessionalService", ("Name", "title"), ("Description", "description"), ("Telephone", "telephone"), ("Email", "email"), ("PriceRange", "priceRange"), ("Url", "__url"));
        SeedSimpleMapping(repo, legalServicePageCt, "LegalService", ("Name", "title"), ("Description", "description"), ("Telephone", "telephone"), ("Email", "email"), ("Url", "__url"));
        SeedSimpleMapping(repo, financialServicePageCt, "FinancialService", ("Name", "title"), ("Description", "description"), ("Telephone", "telephone"), ("Email", "email"), ("Url", "__url"));

        // New legal category mappings
        SeedSimpleMapping(repo, legalListingCt, "CollectionPage", ("Name", "title"), ("Description", "description"), ("Url", "__url"));
        SeedSimpleMapping(repo, notaryPageCt, "Notary", ("Name", "title"), ("Description", "description"), ("Telephone", "telephone"), ("Email", "email"), ("Url", "__url"));
        SeedSimpleMapping(repo, courthousePageCt, "Courthouse", ("Name", "title"), ("Description", "description"), ("Telephone", "telephone"), ("Url", "__url"));
        SeedSimpleMapping(repo, legislationPageCt, "Legislation", ("Name", "title"), ("Description", "description"), ("LegislationIdentifier", "legislationIdentifier"), ("LegislationDate", "legislationDate"), ("LegislationJurisdiction", "legislationJurisdiction"), ("LegislationType", "legislationType"), ("Url", "__url"));
        SeedSimpleMapping(repo, governmentOrgPageCt, "GovernmentOrganization", ("Name", "title"), ("Description", "description"), ("Telephone", "telephone"), ("AreaServed", "areaServed"), ("Url", "__url"));
        SeedSimpleMapping(repo, libraryPageCt, "Library", ("Name", "title"), ("Description", "description"), ("Telephone", "telephone"), ("Url", "__url"));
        SeedSimpleMapping(repo, movieTheaterPageCt, "MovieTheater", ("Name", "title"), ("Description", "description"), ("Telephone", "telephone"), ("ScreenCount", "screenCount"), ("Url", "__url"));
        SeedSimpleMapping(repo, nightClubPageCt, "NightClub", ("Name", "title"), ("Description", "description"), ("Telephone", "telephone"), ("Url", "__url"));
        SeedSimpleMapping(repo, stadiumPageCt, "StadiumOrArena", ("Name", "title"), ("Description", "description"), ("Telephone", "telephone"), ("MaximumAttendeeCapacity", "maximumAttendeeCapacity"), ("Url", "__url"));
        SeedSimpleMapping(repo, skiResortPageCt, "SkiResort", ("Name", "title"), ("Description", "description"), ("Telephone", "telephone"), ("Url", "__url"));
        SeedSimpleMapping(repo, golfCoursePageCt, "GolfCourse", ("Name", "title"), ("Description", "description"), ("Telephone", "telephone"), ("Url", "__url"));
        SeedSimpleMapping(repo, apartmentPageCt, "Apartment", ("Name", "title"), ("Description", "description"), ("NumberOfRooms", "numberOfRooms"), ("FloorSize", "floorSize"), ("PetsAllowed", "petsAllowed"), ("Url", "__url"));
        SeedSimpleMapping(repo, housePageCt, "House", ("Name", "title"), ("Description", "description"), ("NumberOfRooms", "numberOfRooms"), ("FloorSize", "floorSize"), ("Url", "__url"));
        SeedSimpleMapping(repo, lodgingBusinessPageCt, "LodgingBusiness", ("Name", "title"), ("Description", "description"), ("Telephone", "telephone"), ("StarRating", "starRating"), ("CheckinTime", "checkinTime"), ("CheckoutTime", "checkoutTime"), ("Url", "__url"));
        SeedSimpleMapping(repo, articlePageCt, "Article", ("Headline", "title"), ("Description", "description"), ("ArticleBody", "bodyText"), ("Author", "authorName"), ("DatePublished", "datePublished"), ("ArticleSection", "articleSection"), ("Url", "__url"));
        SeedSimpleMapping(repo, podcastSeriesPageCt, "PodcastSeries", ("Name", "title"), ("Description", "description"), ("WebFeed", "webFeed"), ("Author", "authorName"), ("Url", "__url"));
        SeedSimpleMapping(repo, musicRecordingPageCt, "MusicRecording", ("Name", "title"), ("Description", "description"), ("Duration", "duration"), ("ByArtist", "byArtist"), ("InAlbum", "inAlbum"), ("DatePublished", "datePublished"), ("Url", "__url"));
        SeedSimpleMapping(repo, diagnosticLabPageCt, "DiagnosticLab", ("Name", "title"), ("Description", "description"), ("Telephone", "telephone"), ("Url", "__url"));
        SeedSimpleMapping(repo, educationalOrgPageCt, "EducationalOrganization", ("Name", "title"), ("Description", "description"), ("Telephone", "telephone"), ("Url", "__url"));
        SeedSimpleMapping(repo, realEstateListingPageCt, "RealEstateListing", ("Name", "title"), ("Description", "description"), ("Price", "price"), ("PriceCurrency", "priceCurrency"), ("DatePosted", "datePosted"), ("Url", "__url"));

        // New Property (Real Estate) listing + subtypes
        SeedSimpleMapping(repo, propertyListingCt, "CollectionPage", ("Name", "title"), ("Description", "description"), ("Url", "__url"));
        SeedSimpleMapping(repo, singleFamilyResidencePageCt, "SingleFamilyResidence", ("Name", "title"), ("Description", "description"), ("NumberOfRooms", "numberOfRooms"), ("FloorSize", "floorSize"), ("NumberOfBathroomsTotal", "numberOfBathroomsTotal"), ("YearBuilt", "yearBuilt"), ("Url", "__url"));
        SeedSimpleMapping(repo, apartmentComplexPageCt, "ApartmentComplex", ("Name", "title"), ("Description", "description"), ("NumberOfAccommodationUnits", "numberOfAccommodationUnits"), ("PetsAllowed", "petsAllowed"), ("Telephone", "telephone"), ("Url", "__url"));
        SeedSimpleMapping(repo, residencePageCt, "Residence", ("Name", "title"), ("Description", "description"), ("NumberOfRooms", "numberOfRooms"), ("FloorSize", "floorSize"), ("Url", "__url"));
        SeedSimpleMapping(repo, suitePageCt, "Suite", ("Name", "title"), ("Description", "description"), ("NumberOfRooms", "numberOfRooms"), ("FloorSize", "floorSize"), ("Bed", "bedType"), ("Occupancy", "occupancy"), ("Url", "__url"));
        SeedSimpleMapping(repo, gatedResidenceCommunityPageCt, "GatedResidenceCommunity", ("Name", "title"), ("Description", "description"), ("NumberOfAccommodationUnits", "numberOfAccommodationUnits"), ("PetsAllowed", "petsAllowed"), ("Url", "__url"));
        SeedSimpleMapping(repo, accommodationPageCt, "Accommodation", ("Name", "title"), ("Description", "description"), ("NumberOfRooms", "numberOfRooms"), ("FloorSize", "floorSize"), ("PetsAllowed", "petsAllowed"), ("TourBookingPage", "tourBookingPage"), ("Url", "__url"));

        SeedSimpleMapping(repo, organisationParentCt, "Organization", ("Name", "title"), ("Description", "description"), ("Telephone", "telephone"), ("Email", "email"), ("Url", "__url"));

        // Landing page — WebPage with nested ImageObject (primaryImageOfPage) + BlockGrid mainEntity
        try
        {
            SeedLandingPageMapping(landingPageCt, repo);
            _logger.LogInformation("TestDataSeeder: Landing page mapping seeded successfully");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "TestDataSeeder: Failed to seed Landing page mapping");
        }

        // Hierarchy mappings (parent/ancestor/sibling)
        SeedLocalBusinessChildMapping(localBusinessChildCt, repo);
        SeedDepartmentPageMapping(departmentPageCt, repo);

        // Category ancestor mappings for product schemas
        AddCategoryMapping(repo, "productPage", "productListing");
        AddCategoryMapping(repo, "softwarePage", "productListing");
        AddCategoryMapping(repo, "vehiclePage", "productListing");
        AddCategoryMapping(repo, "financialProductPage", "productListing");
        AddCategoryMapping(repo, "individualProductPage", "productListing");
        AddCategoryMapping(repo, "productModelPage", "productListing");
        AddCategoryMapping(repo, "offerPage", "productListing");

        // Category ancestor mappings for event schemas
        AddCategoryMapping(repo, "eventPage", "eventListing");
        AddCategoryMapping(repo, "musicEventPage", "eventListing");
        AddCategoryMapping(repo, "sportsEventPage", "eventListing");
        AddCategoryMapping(repo, "businessEventPage", "eventListing");
        AddCategoryMapping(repo, "foodEventPage", "eventListing");
        AddCategoryMapping(repo, "festivalPage", "eventListing");
        AddCategoryMapping(repo, "educationEventPage", "eventListing");
        AddCategoryMapping(repo, "theaterEventPage", "eventListing");
        AddCategoryMapping(repo, "screeningEventPage", "eventListing");

        _logger.LogInformation("TestDataSeeder: seeded {Count} schema mappings", 143);
    }

    private void SeedLandingPageMapping(IContentType ct, ISchemaMappingRepository repo)
    {
        var mapping = repo.Save(new SchemaMapping
        {
            ContentTypeAlias = ct.Alias,
            ContentTypeKey = ct.Key,
            SchemaTypeName = "WebPage",
            IsEnabled = true,
        });

        repo.SavePropertyMappings(mapping.Id, new[]
        {
            new PropertyMapping
            {
                SchemaPropertyName = "Name",
                SourceType = "property",
                ContentTypePropertyAlias = "pageTitle",
            },
            new PropertyMapping
            {
                SchemaPropertyName = "Description",
                SourceType = "property",
                ContentTypePropertyAlias = "metaDescription",
            },
            // Complex type — nested ImageObject wrapping the Media Picker value.
            // Exercises the nested ranking UX from Part A; resolver expands the media
            // into an ImageObject with URL, caption, width, and height.
            new PropertyMapping
            {
                SchemaPropertyName = "PrimaryImageOfPage",
                SourceType = "property",
                ContentTypePropertyAlias = "heroImage",
                NestedSchemaTypeName = "ImageObject",
            },
            new PropertyMapping
            {
                SchemaPropertyName = "Url",
                SourceType = "property",
                ContentTypePropertyAlias = "__url",
            },
            // BlockGrid content — emits each block as its own nested entity on mainEntity.
            // The blockContent resolver walks contentData and applies the nested mappings
            // per block type. The schemeWeaver blockContent source handles BlockGrid JSON
            // the same way it handles BlockList (both formats share the contentData shape).
            new PropertyMapping
            {
                SchemaPropertyName = "MainEntity",
                SourceType = "blockContent",
                ContentTypePropertyAlias = "contentGrid",
                NestedSchemaTypeName = "WebPageElement",
                ResolverConfig = JsonSerializer.Serialize(new
                {
                    nestedMappings = new object[]
                    {
                        new { schemaProperty = "Name", contentProperty = "title" },
                        new { schemaProperty = "Headline", contentProperty = "subtitle" },
                        new { schemaProperty = "Description", contentProperty = "description" },
                        new { schemaProperty = "Image", contentProperty = "heroImage" },
                        new { schemaProperty = "Image", contentProperty = "featureImage" },
                        new { schemaProperty = "Text", contentProperty = "quote" },
                        new { schemaProperty = "Author", contentProperty = "attribution", wrapInType = "Person", wrapInProperty = "Name" },
                    }
                }),
            },
        });
    }

    /// <summary>
    /// Home page → WebSite mapping with the BlockGrid promoted to mainEntity. Seeds the same
    /// property mappings the legacy <c>SeedInheritedMapping</c> call produced (Name, Description,
    /// Url, plus the universal heroImage → Image fallback) and adds the BlockContent resolver so
    /// each contentGrid block becomes a WebPageElement on the JSON-LD output.
    /// </summary>
    private void SeedHomePageMapping(IContentType ct, ISchemaMappingRepository repo)
    {
        var mapping = repo.Save(new SchemaMapping
        {
            ContentTypeAlias = ct.Alias,
            ContentTypeKey = ct.Key,
            SchemaTypeName = "WebSite",
            IsEnabled = true,
            IsInherited = true,
        });

        repo.SavePropertyMappings(mapping.Id, new[]
        {
            new PropertyMapping { SchemaPropertyName = "Name", SourceType = "property", ContentTypePropertyAlias = "siteName" },
            new PropertyMapping { SchemaPropertyName = "Description", SourceType = "property", ContentTypePropertyAlias = "siteDescription" },
            new PropertyMapping { SchemaPropertyName = "Url", SourceType = "property", ContentTypePropertyAlias = "__url" },
            new PropertyMapping { SchemaPropertyName = "Image", SourceType = "property", ContentTypePropertyAlias = "heroImage" },
            new PropertyMapping
            {
                SchemaPropertyName = "MainEntity",
                SourceType = "blockContent",
                ContentTypePropertyAlias = "contentGrid",
                NestedSchemaTypeName = "WebPageElement",
                ResolverConfig = JsonSerializer.Serialize(new
                {
                    nestedMappings = new object[]
                    {
                        new { schemaProperty = "Name", contentProperty = "title" },
                        new { schemaProperty = "Headline", contentProperty = "subtitle" },
                        new { schemaProperty = "Description", contentProperty = "description" },
                        new { schemaProperty = "Image", contentProperty = "heroImage" },
                        new { schemaProperty = "Image", contentProperty = "featureImage" },
                        new { schemaProperty = "Text", contentProperty = "quote" },
                        new { schemaProperty = "Author", contentProperty = "attribution", wrapInType = "Person", wrapInProperty = "Name" },
                    }
                }),
            },
        });
    }

    private void SeedHowToMapping(IContentType ct, ISchemaMappingRepository repo)
    {
        var mapping = repo.Save(new SchemaMapping
        {
            ContentTypeAlias = ct.Alias,
            ContentTypeKey = ct.Key,
            SchemaTypeName = "HowTo",
            IsEnabled = true,
        });

        repo.SavePropertyMappings(mapping.Id, new[]
        {
            new PropertyMapping { SchemaPropertyName = "Name", SourceType = "property", ContentTypePropertyAlias = "title" },
            new PropertyMapping { SchemaPropertyName = "Description", SourceType = "property", ContentTypePropertyAlias = "description" },
            new PropertyMapping { SchemaPropertyName = "TotalTime", SourceType = "property", ContentTypePropertyAlias = "totalTime" },
            new PropertyMapping { SchemaPropertyName = "EstimatedCost", SourceType = "property", ContentTypePropertyAlias = "estimatedCost" },
            new PropertyMapping { SchemaPropertyName = "Url", SourceType = "property", ContentTypePropertyAlias = "__url" },
            new PropertyMapping
            {
                SchemaPropertyName = "Step",
                SourceType = "blockContent",
                ContentTypePropertyAlias = "howToSteps",
                NestedSchemaTypeName = "HowToStep",
                ResolverConfig = JsonSerializer.Serialize(new
                {
                    nestedMappings = new[]
                    {
                        new { schemaProperty = "Name", contentProperty = "stepName" },
                        new { schemaProperty = "Text", contentProperty = "stepText" },
                    }
                }),
            },
            new PropertyMapping
            {
                SchemaPropertyName = "Tool",
                SourceType = "blockContent",
                ContentTypePropertyAlias = "howToTools",
                ResolverConfig = JsonSerializer.Serialize(new
                {
                    extractAs = "stringList",
                    contentProperty = "toolName",
                }),
            },
        });
    }

    private void SeedSimpleMapping(ISchemaMappingRepository repo, IContentType ct, string schemaTypeName, params (string schemaProperty, string contentProperty)[] mappings)
    {
        SeedSimpleMappingCore(repo, ct, schemaTypeName, false, mappings);
    }

    private void SeedInheritedMapping(ISchemaMappingRepository repo, IContentType ct, string schemaTypeName, params (string schemaProperty, string contentProperty)[] mappings)
    {
        SeedSimpleMappingCore(repo, ct, schemaTypeName, true, mappings);
    }

    private void SeedSimpleMappingCore(ISchemaMappingRepository repo, IContentType ct, string schemaTypeName, bool isInherited, (string schemaProperty, string contentProperty)[] mappings)
    {
        var mapping = repo.Save(new SchemaMapping
        {
            ContentTypeAlias = ct.Alias,
            ContentTypeKey = ct.Key,
            SchemaTypeName = schemaTypeName,
            IsEnabled = true,
            IsInherited = isInherited,
        });

        var propertyMappings = mappings.Select(m => new PropertyMapping
        {
            SchemaPropertyName = m.schemaProperty,
            SourceType = "property",
            ContentTypePropertyAlias = m.contentProperty,
        }).ToList();

        // Universal Image → heroImage mapping. CreateContentType adds heroImage to every doctype,
        // so this pair always resolves to something when the seeder populates a hero image.
        // Skip if the caller already explicitly mapped "Image" (e.g. productPage → productImage).
        var hasImage = propertyMappings.Any(m =>
            string.Equals(m.SchemaPropertyName, "Image", StringComparison.OrdinalIgnoreCase));
        if (!hasImage && ct.PropertyTypes.Any(p => string.Equals(p.Alias, "heroImage", StringComparison.OrdinalIgnoreCase)))
        {
            propertyMappings.Add(new PropertyMapping
            {
                SchemaPropertyName = "Image",
                SourceType = "property",
                ContentTypePropertyAlias = "heroImage",
            });
        }

        repo.SavePropertyMappings(mapping.Id, propertyMappings);
    }

    private void AddCategoryMapping(ISchemaMappingRepository repo, string contentTypeAlias, string parentContentTypeAlias)
    {
        var mapping = repo.GetByContentTypeAlias(contentTypeAlias);
        if (mapping != null)
        {
            var existingMappings = repo.GetPropertyMappings(mapping.Id).ToList();
            existingMappings.Add(new PropertyMapping
            {
                SchemaPropertyName = "Category",
                // Use "parent" (immediate content parent) rather than "ancestor" — the ancestor
                // walk filters by SourceContentTypeAlias AND requires the property to be non-null,
                // which was silently skipping the intermediate category nodes ("Electronics",
                // "Software", …) and resolving to the root "Products" listing instead.
                SourceType = "parent",
                ContentTypePropertyAlias = "title",
                SourceContentTypeAlias = parentContentTypeAlias,
            });
            repo.SavePropertyMappings(mapping.Id, existingMappings);
        }
    }

    private void SeedFaqPageMapping(IContentType ct, ISchemaMappingRepository _mappingRepository)
    {
        var mapping = _mappingRepository.Save(new SchemaMapping
        {
            ContentTypeAlias = "faqPage",
            ContentTypeKey = ct.Key,
            SchemaTypeName = "FAQPage",
            IsEnabled = true,
        });

        _mappingRepository.SavePropertyMappings(mapping.Id, new[]
        {
            new PropertyMapping
            {
                SchemaPropertyName = "Name",
                SourceType = "property",
                ContentTypePropertyAlias = "title",
            },
            new PropertyMapping
            {
                SchemaPropertyName = "Description",
                SourceType = "property",
                ContentTypePropertyAlias = "description",
            },
            new PropertyMapping
            {
                SchemaPropertyName = "MainEntity",
                SourceType = "blockContent",
                ContentTypePropertyAlias = "faqItems",
                NestedSchemaTypeName = "Question",
                ResolverConfig = JsonSerializer.Serialize(new
                {
                    nestedMappings = new object[]
                    {
                        new { schemaProperty = "Name", contentProperty = "question" },
                        new { schemaProperty = "AcceptedAnswer", contentProperty = "answer", wrapInType = "Answer", wrapInProperty = "Text" },
                    }
                }),
            },
        });
    }

    private void SeedProductPageMapping(IContentType ct, ISchemaMappingRepository _mappingRepository)
    {
        var mapping = _mappingRepository.Save(new SchemaMapping
        {
            ContentTypeAlias = "productPage",
            ContentTypeKey = ct.Key,
            SchemaTypeName = "Product",
            IsEnabled = true,
        });

        _mappingRepository.SavePropertyMappings(mapping.Id, new[]
        {
            new PropertyMapping
            {
                SchemaPropertyName = "Name",
                SourceType = "property",
                ContentTypePropertyAlias = "title",
            },
            new PropertyMapping
            {
                SchemaPropertyName = "Description",
                SourceType = "property",
                ContentTypePropertyAlias = "description",
            },
            new PropertyMapping
            {
                SchemaPropertyName = "Sku",
                SourceType = "property",
                ContentTypePropertyAlias = "sku",
            },
            new PropertyMapping
            {
                SchemaPropertyName = "Brand",
                SourceType = "property",
                ContentTypePropertyAlias = "brand",
            },
            new PropertyMapping
            {
                SchemaPropertyName = "Image",
                SourceType = "property",
                ContentTypePropertyAlias = "productImage",
            },
            new PropertyMapping
            {
                SchemaPropertyName = "Review",
                SourceType = "blockContent",
                ContentTypePropertyAlias = "reviews",
                NestedSchemaTypeName = "Review",
                ResolverConfig = JsonSerializer.Serialize(new
                {
                    nestedMappings = new object[]
                    {
                        new { schemaProperty = "Author", contentProperty = "reviewAuthor", wrapInType = "Person", wrapInProperty = "Name" },
                        new { schemaProperty = "ReviewRating", contentProperty = "ratingValue" },
                        new { schemaProperty = "ReviewBody", contentProperty = "reviewBody" },
                        new { schemaProperty = "DatePublished", contentProperty = "reviewDate" },
                    }
                }),
            },
        });
    }

    private void SeedRecipePageMapping(IContentType ct, ISchemaMappingRepository _mappingRepository)
    {
        var mapping = _mappingRepository.Save(new SchemaMapping
        {
            ContentTypeAlias = "recipePage",
            ContentTypeKey = ct.Key,
            SchemaTypeName = "Recipe",
            IsEnabled = true,
        });

        _mappingRepository.SavePropertyMappings(mapping.Id, new[]
        {
            new PropertyMapping
            {
                SchemaPropertyName = "Name",
                SourceType = "property",
                ContentTypePropertyAlias = "title",
            },
            new PropertyMapping
            {
                SchemaPropertyName = "Description",
                SourceType = "property",
                ContentTypePropertyAlias = "description",
            },
            new PropertyMapping
            {
                SchemaPropertyName = "Image",
                SourceType = "property",
                ContentTypePropertyAlias = "heroImage",
            },
            new PropertyMapping
            {
                SchemaPropertyName = "PrepTime",
                SourceType = "property",
                ContentTypePropertyAlias = "prepTime",
            },
            new PropertyMapping
            {
                SchemaPropertyName = "CookTime",
                SourceType = "property",
                ContentTypePropertyAlias = "cookTime",
            },
            new PropertyMapping
            {
                SchemaPropertyName = "TotalTime",
                SourceType = "property",
                ContentTypePropertyAlias = "totalTime",
            },
            new PropertyMapping
            {
                SchemaPropertyName = "RecipeYield",
                SourceType = "property",
                ContentTypePropertyAlias = "recipeYield",
            },
            new PropertyMapping
            {
                SchemaPropertyName = "RecipeIngredient",
                SourceType = "blockContent",
                ContentTypePropertyAlias = "ingredients",
                ResolverConfig = JsonSerializer.Serialize(new
                {
                    extractAs = "stringList",
                    contentProperty = "ingredient",
                }),
            },
            new PropertyMapping
            {
                SchemaPropertyName = "RecipeInstructions",
                SourceType = "blockContent",
                ContentTypePropertyAlias = "instructions",
                NestedSchemaTypeName = "HowToStep",
                ResolverConfig = JsonSerializer.Serialize(new
                {
                    nestedMappings = new[]
                    {
                        new { schemaProperty = "Name", contentProperty = "stepName" },
                        new { schemaProperty = "Text", contentProperty = "stepText" },
                    }
                }),
            },
            new PropertyMapping
            {
                SchemaPropertyName = "Author",
                SourceType = "complexType",
                ContentTypePropertyAlias = "authorName",
                NestedSchemaTypeName = "Person",
                ResolverConfig = JsonSerializer.Serialize(new
                {
                    complexTypeMappings = new[]
                    {
                        new { schemaProperty = "Name", sourceType = "property", contentTypePropertyAlias = "authorName" },
                    }
                }),
            },
        });
    }

    private void SeedBlogArticleMapping(IContentType ct, ISchemaMappingRepository _mappingRepository)
    {
        var mapping = _mappingRepository.Save(new SchemaMapping
        {
            ContentTypeAlias = "blogArticle",
            ContentTypeKey = ct.Key,
            SchemaTypeName = "BlogPosting",
            IsEnabled = true,
        });

        _mappingRepository.SavePropertyMappings(mapping.Id, new[]
        {
            new PropertyMapping
            {
                SchemaPropertyName = "Headline",
                SourceType = "property",
                ContentTypePropertyAlias = "title",
            },
            new PropertyMapping
            {
                SchemaPropertyName = "ArticleBody",
                SourceType = "property",
                ContentTypePropertyAlias = "bodyText",
            },
            new PropertyMapping
            {
                SchemaPropertyName = "Description",
                SourceType = "property",
                ContentTypePropertyAlias = "description",
            },
            new PropertyMapping
            {
                SchemaPropertyName = "DatePublished",
                SourceType = "property",
                ContentTypePropertyAlias = "publishDate",
            },
            new PropertyMapping
            {
                SchemaPropertyName = "Keywords",
                SourceType = "property",
                ContentTypePropertyAlias = "keywords",
            },
            new PropertyMapping
            {
                SchemaPropertyName = "Author",
                SourceType = "complexType",
                ContentTypePropertyAlias = "authorName",
                NestedSchemaTypeName = "Person",
                ResolverConfig = JsonSerializer.Serialize(new
                {
                    complexTypeMappings = new[]
                    {
                        new { schemaProperty = "Name", sourceType = "property", contentTypePropertyAlias = "authorName" },
                    }
                }),
            },
            new PropertyMapping
            {
                SchemaPropertyName = "Publisher",
                SourceType = "ancestor",
                ContentTypePropertyAlias = "organisationName",
                SourceContentTypeAlias = "homePage",
            },
            new PropertyMapping
            {
                SchemaPropertyName = "Image",
                SourceType = "property",
                ContentTypePropertyAlias = "heroImage",
            },
        });
    }

    private void SeedLocalBusinessChildMapping(IContentType ct, ISchemaMappingRepository repo)
    {
        var mapping = repo.Save(new SchemaMapping
        {
            ContentTypeAlias = ct.Alias,
            ContentTypeKey = ct.Key,
            SchemaTypeName = "LocalBusiness",
            IsEnabled = true,
        });

        repo.SavePropertyMappings(mapping.Id, new[]
        {
            new PropertyMapping { SchemaPropertyName = "Name", SourceType = "property", ContentTypePropertyAlias = "title" },
            new PropertyMapping { SchemaPropertyName = "Description", SourceType = "property", ContentTypePropertyAlias = "description" },
            new PropertyMapping { SchemaPropertyName = "Telephone", SourceType = "property", ContentTypePropertyAlias = "telephone" },
            new PropertyMapping { SchemaPropertyName = "PriceRange", SourceType = "property", ContentTypePropertyAlias = "priceRange" },
            new PropertyMapping { SchemaPropertyName = "Url", SourceType = "property", ContentTypePropertyAlias = "__url" },
            new PropertyMapping
            {
                SchemaPropertyName = "ParentOrganization",
                SourceType = "parent",
                ContentTypePropertyAlias = "title",
            },
        });
    }

    private void SeedDepartmentPageMapping(IContentType ct, ISchemaMappingRepository repo)
    {
        var mapping = repo.Save(new SchemaMapping
        {
            ContentTypeAlias = ct.Alias,
            ContentTypeKey = ct.Key,
            SchemaTypeName = "Organization",
            IsEnabled = true,
        });

        repo.SavePropertyMappings(mapping.Id, new[]
        {
            new PropertyMapping { SchemaPropertyName = "Name", SourceType = "property", ContentTypePropertyAlias = "title" },
            new PropertyMapping { SchemaPropertyName = "Description", SourceType = "property", ContentTypePropertyAlias = "description" },
            new PropertyMapping { SchemaPropertyName = "Url", SourceType = "property", ContentTypePropertyAlias = "__url" },
            new PropertyMapping
            {
                SchemaPropertyName = "Location",
                SourceType = "sibling",
                ContentTypePropertyAlias = "streetAddress",
                SourceContentTypeAlias = "localBusinessChild",
            },
        });
    }

    private void SeedEventPageMapping(IContentType ct, ISchemaMappingRepository _mappingRepository)
    {
        var mapping = _mappingRepository.Save(new SchemaMapping
        {
            ContentTypeAlias = "eventPage",
            ContentTypeKey = ct.Key,
            SchemaTypeName = "Event",
            IsEnabled = true,
        });

        _mappingRepository.SavePropertyMappings(mapping.Id, new[]
        {
            new PropertyMapping
            {
                SchemaPropertyName = "Name",
                SourceType = "property",
                ContentTypePropertyAlias = "title",
            },
            new PropertyMapping
            {
                SchemaPropertyName = "Description",
                SourceType = "property",
                ContentTypePropertyAlias = "description",
            },
            new PropertyMapping
            {
                SchemaPropertyName = "Image",
                SourceType = "property",
                ContentTypePropertyAlias = "heroImage",
            },
            new PropertyMapping
            {
                SchemaPropertyName = "StartDate",
                SourceType = "property",
                ContentTypePropertyAlias = "startDate",
            },
            new PropertyMapping
            {
                SchemaPropertyName = "EndDate",
                SourceType = "property",
                ContentTypePropertyAlias = "endDate",
            },
            new PropertyMapping
            {
                SchemaPropertyName = "Location",
                SourceType = "complexType",
                NestedSchemaTypeName = "Place",
                ResolverConfig = JsonSerializer.Serialize(new
                {
                    complexTypeMappings = new[]
                    {
                        new { schemaProperty = "Name", sourceType = "property", contentTypePropertyAlias = "locationName" },
                        new { schemaProperty = "Address", sourceType = "property", contentTypePropertyAlias = "locationAddress" },
                    }
                }),
            },
            new PropertyMapping
            {
                SchemaPropertyName = "Organizer",
                SourceType = "complexType",
                NestedSchemaTypeName = "Organization",
                ResolverConfig = JsonSerializer.Serialize(new
                {
                    complexTypeMappings = new[]
                    {
                        new { schemaProperty = "Name", sourceType = "property", contentTypePropertyAlias = "organiserName" },
                    }
                }),
            },
        });
    }
}
