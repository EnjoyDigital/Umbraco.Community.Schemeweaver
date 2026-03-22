using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Umbraco.Cms.Core;
using Umbraco.Cms.Core.Composing;
using Umbraco.Cms.Core.Models;
using Umbraco.Cms.Core.Models.ContentPublishing;
using Umbraco.Cms.Core.PropertyEditors;
using Umbraco.Cms.Core.Serialization;
using Umbraco.Cms.Core.Services;
using Umbraco.Cms.Core.Strings;
using Umbraco.Community.SchemeWeaver.Models.Entities;
using Umbraco.Community.SchemeWeaver.Persistence;

namespace SchemeWeaver.TestHost;

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
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<TestDataSeeder> _logger;

    public TestDataSeeder(
        IContentTypeService contentTypeService,
        IDataTypeService dataTypeService,
        IShortStringHelper shortStringHelper,
        PropertyEditorCollection propertyEditors,
        IConfigurationEditorJsonSerializer configSerializer,
        IContentService contentService,
        IContentPublishingService contentPublishingService,
        IFileService fileService,
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
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        // Only seed if no content types exist yet
        var existing = _contentTypeService.GetAll();
        if (existing.Any())
            return;

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

        // 2. Create BlockList data types
        var faqBlockList = await CreateBlockListDataType("FAQ Items Block List", [faqItem], cancellationToken);
        var reviewsBlockList = await CreateBlockListDataType("Reviews Block List", [reviewItem], cancellationToken);
        var ingredientsBlockList = await CreateBlockListDataType("Ingredients Block List", [recipeIngredient], cancellationToken);
        var instructionsBlockList = await CreateBlockListDataType("Instructions Block List", [recipeStep], cancellationToken);
        var howToStepsBlockList = await CreateBlockListDataType("HowTo Steps Block List", [howToStepEl], cancellationToken);
        var howToToolsBlockList = await CreateBlockListDataType("HowTo Tools Block List", [howToToolEl], cancellationToken);
        var openingHoursBlockList = await CreateBlockListDataType("Opening Hours Block List", [openingHoursEl], cancellationToken);

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
        }, cancellationToken);

        var aboutPageCt = await CreateContentType("aboutPage", "About Page", "icon-info", new[]
        {
            ("title", "Title", textboxDataType),
            ("description", "Description", descDataType),
            ("bodyText", "Body Text", bodyDataType),
            ("organisationName", "Organisation Name", textboxDataType),
            ("foundingDate", "Founding Date", textboxDataType),
            ("numberOfEmployees", "Number of Employees", textboxDataType),
        }, cancellationToken);

        var listingProps = new[]
        {
            ("title", "Title", textboxDataType),
            ("description", "Description", descDataType),
        };
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

        // 3b. Create master template and assign templates to content types
        if (_fileService.GetTemplate("master") is null)
        {
            var masterTemplate = new Umbraco.Cms.Core.Models.Template(_shortStringHelper, "Master", "master");
            _fileService.SaveTemplate(masterTemplate);
            _logger.LogInformation("TestDataSeeder: Created master template");
        }

        var allContentTypes = new IContentType[]
        {
            blogArticleCt, productPageCt, faqPageCt, eventPageCt, recipePageCt,
            homePageCt, aboutPageCt, blogListingCt, productListingCt, eventListingCt, recipeListingCt,
            newsArticleCt, techArticleCt, softwarePageCt, coursePageCt,
            howToPageCt, videoPageCt, jobPostingPageCt, profilePageCt,
            locationPageCt, restaurantPageCt, bookPageCt, contactPageCt,
        };

        foreach (var ct in allContentTypes)
        {
            await AssignTemplate(ct);
        }

        // 4. Create and publish sample content (hierarchical site tree)
        await CreateSampleContent(
            faqItem, reviewItem, recipeIngredient, recipeStep,
            howToStepEl, howToToolEl, openingHoursEl,
            cancellationToken);

        // 5. Seed default schema mappings
        SeedSchemaMappings(blogArticleCt, productPageCt, faqPageCt, eventPageCt, recipePageCt,
            homePageCt, aboutPageCt, blogListingCt, productListingCt, eventListingCt, recipeListingCt,
            newsArticleCt, techArticleCt, softwarePageCt, coursePageCt,
            howToPageCt, videoPageCt, jobPostingPageCt, profilePageCt,
            locationPageCt, restaurantPageCt, bookPageCt, contactPageCt);
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

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
        (string alias, string name, IDataType dataType)[] properties,
        CancellationToken cancellationToken,
        params (string alias, string name, IDataType dataType)[] extraProperties)
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
            ct.AddPropertyType(new PropertyType(_shortStringHelper, dataType)
            {
                Alias = propAlias,
                Name = propName,
            }, "content", "Content");
        }

        foreach (var (propAlias, propName, dataType) in extraProperties)
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

    private async Task AssignTemplate(IContentType ct)
    {
        var template = _fileService.GetTemplate(ct.Alias);

        // Create the template if it doesn't exist (Umbraco stores templates in DB)
        if (template is null)
        {
            var viewContent = $@"@inherits Umbraco.Cms.Web.Common.Views.UmbracoViewPage
@{{
    Layout = ""master.cshtml"";
}}";
            template = new Umbraco.Cms.Core.Models.Template(_shortStringHelper, ct.Name, ct.Alias)
            {
                Content = viewContent,
            };
            _fileService.SaveTemplate(template);
            _logger.LogInformation("TestDataSeeder: Created template {Alias}", ct.Alias);
        }

        ct.AllowedTemplates = new[] { template };
        ct.SetDefaultTemplate(template);
        _contentTypeService.Save(ct);
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
        };

        ct.AddPropertyGroup("content", "Content");
        ct.AddPropertyGroup("metadata", "Metadata");

        AddProperty(ct, "title", "Title", textbox, "content", true);
        AddProperty(ct, "description", "Description", desc, "content");
        AddProperty(ct, "bodyText", "Body Text", body, "content");
        AddProperty(ct, "authorName", "Author Name", textbox, "metadata");
        AddProperty(ct, "publishDate", "Publish Date", dateTime ?? textbox, "metadata");
        AddProperty(ct, "featuredImage", "Featured Image", mediaPicker ?? textbox, "content");
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
        };

        ct.AddPropertyGroup("content", "Content");
        ct.AddPropertyGroup("product", "Product Details");

        AddProperty(ct, "title", "Title", textbox, "content", true);
        AddProperty(ct, "description", "Description", desc, "content");
        AddProperty(ct, "bodyText", "Body Text", body, "content");
        AddProperty(ct, "price", "Price", textbox, "product");
        AddProperty(ct, "sku", "SKU", textbox, "product");
        AddProperty(ct, "brand", "Brand", textbox, "product");
        AddProperty(ct, "availability", "Availability", textbox, "product");
        AddProperty(ct, "currency", "Currency", textbox, "product");
        AddProperty(ct, "productImage", "Product Image", mediaPicker ?? textbox, "content");
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
        };

        ct.AddPropertyGroup("content", "Content");

        AddProperty(ct, "title", "Title", textbox, "content", true);
        AddProperty(ct, "description", "Description", desc, "content");
        AddProperty(ct, "faqItems", "FAQ Items", faqBlockList, "content");

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
        };

        ct.AddPropertyGroup("content", "Content");
        ct.AddPropertyGroup("event", "Event Details");

        AddProperty(ct, "title", "Title", textbox, "content", true);
        AddProperty(ct, "description", "Description", desc, "content");
        AddProperty(ct, "startDate", "Start Date", dateTime ?? textbox, "event");
        AddProperty(ct, "endDate", "End Date", dateTime ?? textbox, "event");
        AddProperty(ct, "locationName", "Location Name", textbox, "event");
        AddProperty(ct, "locationAddress", "Location Address", textbox, "event");
        AddProperty(ct, "organiserName", "Organiser Name", textbox, "event");
        AddProperty(ct, "ticketPrice", "Ticket Price", textbox, "event");
        AddProperty(ct, "ticketUrl", "Ticket URL", textbox, "event");
        AddProperty(ct, "eventImage", "Event Image", mediaPicker ?? textbox, "content");

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
        };

        ct.AddPropertyGroup("content", "Content");
        ct.AddPropertyGroup("recipe", "Recipe Details");
        ct.AddPropertyGroup("ingredients", "Ingredients & Instructions");

        AddProperty(ct, "title", "Title", textbox, "content", true);
        AddProperty(ct, "description", "Description", desc, "content");
        AddProperty(ct, "prepTime", "Prep Time", textbox, "recipe");
        AddProperty(ct, "cookTime", "Cook Time", textbox, "recipe");
        AddProperty(ct, "totalTime", "Total Time", textbox, "recipe");
        AddProperty(ct, "recipeYield", "Servings", textbox, "recipe");
        AddProperty(ct, "calories", "Calories", textbox, "recipe");
        AddProperty(ct, "recipeCategory", "Category", textbox, "recipe");
        AddProperty(ct, "recipeCuisine", "Cuisine", textbox, "recipe");
        AddProperty(ct, "authorName", "Author Name", textbox, "recipe");
        AddProperty(ct, "recipeImage", "Recipe Image", mediaPicker ?? textbox, "content");
        AddProperty(ct, "ingredients", "Ingredients", ingredientsBlockList, "ingredients");
        AddProperty(ct, "instructions", "Instructions", instructionsBlockList, "ingredients");

        await _contentTypeService.CreateAsync(ct, Constants.Security.SuperUserKey).ConfigureAwait(false);
        return ct;
    }

    private void AddProperty(
        ContentType ct, string alias, string name,
        IDataType? dataType, string groupAlias,
        bool mandatory = false)
    {
        if (dataType is null) return;

        ct.AddPropertyType(new PropertyType(_shortStringHelper, dataType)
        {
            Alias = alias,
            Name = name,
            Mandatory = mandatory,
        }, groupAlias);
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
        CancellationToken cancellationToken)
    {
        // Create home page as site root
        var home = _contentService.Create("SchemeWeaver Demo Site", Constants.System.Root, "homePage");
        home.SetValue("siteName", "SchemeWeaver Demo Site");
        home.SetValue("siteDescription", "Comprehensive Schema.org structured data demo — covering 30+ schema types with working JSON-LD output.");
        home.SetValue("organisationName", "Enjoy Digital");
        home.SetValue("organisationEmail", "hello@enjoydigital.co.uk");
        home.SetValue("organisationTelephone", "+44 113 357 0000");
        home.SetValue("sameAs", "https://twitter.com/enjoydigital,https://github.com/nickyoliverpicton");
        _contentService.Save(home);
        await PublishContent(home, cancellationToken);

        // Create listing pages under home
        var blogListing = CreateAndPublishSimple("Blog", home.Id, "blogListing", "Blog", "Articles about Schema.org, structured data, and SEO.", cancellationToken);
        var productListing = CreateAndPublishSimple("Products", home.Id, "productListing", "Products", "Our software products and tools.", cancellationToken);
        var eventListing = CreateAndPublishSimple("Events", home.Id, "eventListing", "Events", "Upcoming community events and conferences.", cancellationToken);
        var recipeListing = CreateAndPublishSimple("Recipes", home.Id, "recipeListing", "Recipes", "Delicious recipes with structured data.", cancellationToken);

        // Create content under listings (existing content, now as children)
        await CreateFaqContent(faqItemType, home.Id, cancellationToken);
        await CreateProductContent(reviewItemType, await productListing, cancellationToken);
        await CreateRecipeContent(recipeIngredientType, recipeStepType, await recipeListing, cancellationToken);
        await CreateBlogContent(await blogListing, cancellationToken);
        await CreateEventContent(await eventListing, cancellationToken);

        // Create new content types
        await CreateNewsArticle(await blogListing, cancellationToken);
        await CreateTechArticle(await blogListing, cancellationToken);
        await CreateSoftwarePage(await productListing, cancellationToken);
        await CreateCoursePage(home.Id, cancellationToken);
        await CreateHowToPage(howToStepType, howToToolType, home.Id, cancellationToken);
        await CreateVideoPage(home.Id, cancellationToken);
        await CreateJobPostingPage(home.Id, cancellationToken);
        await CreateProfilePage(home.Id, cancellationToken);
        await CreateLocationPage(openingHoursType, home.Id, cancellationToken);
        await CreateRestaurantPage(openingHoursType, home.Id, cancellationToken);
        await CreateBookPage(home.Id, cancellationToken);
        await CreateAboutPage(home.Id, cancellationToken);
        await CreateContactContent(home.Id, cancellationToken);

        _logger.LogInformation("TestDataSeeder: created and published {Count} sample content nodes", 25);
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
        var content = _contentService.Create("Frequently Asked Questions", parentId, "faqPage");
        content.SetValue("title", "Frequently Asked Questions");
        content.SetValue("description", "Common questions about our services");

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

        await SaveAndPublishAsync(content);
    }

    private async Task CreateProductContent(IContentType reviewItemType, int parentId, CancellationToken cancellationToken)
    {
        var content = _contentService.Create("Wireless Headphones Pro", parentId, "productPage");
        content.SetValue("title", "Wireless Headphones Pro");
        content.SetValue("description", "Premium noise-cancelling wireless headphones");
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

        await SaveAndPublishAsync(content);
    }

    private async Task CreateRecipeContent(
        IContentType recipeIngredientType,
        IContentType recipeStepType,
        int parentId,
        CancellationToken cancellationToken)
    {
        var content = _contentService.Create("Classic Victoria Sponge", parentId, "recipePage");
        content.SetValue("title", "Classic Victoria Sponge");
        content.SetValue("description", "A traditional British cake recipe");
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

        await SaveAndPublishAsync(content);
    }

    private async Task CreateBlogContent(int parentId, CancellationToken cancellationToken)
    {
        var content = _contentService.Create("Getting Started with Schema.org", parentId, "blogArticle");
        content.SetValue("title", "Getting Started with Schema.org");
        content.SetValue("description", "Learn how structured data helps search engines understand your content.");
        content.SetValue("bodyText", "<p>Schema.org provides a shared vocabulary for structured data markup on web pages.</p>");
        content.SetValue("authorName", "Oliver");
        content.SetValue("publishDate", DateTime.Now);
        content.SetValue("keywords", "schema.org, structured data, SEO");
        content.SetValue("category", "Technology");

        await SaveAndPublishAsync(content);
    }

    private async Task CreateEventContent(int parentId, CancellationToken cancellationToken)
    {
        var content = _contentService.Create("Umbraco UK Festival 2026", parentId, "eventPage");
        content.SetValue("title", "Umbraco UK Festival 2026");
        content.SetValue("description", "The premier Umbraco community event in the UK");
        content.SetValue("startDate", DateTime.Now.AddMonths(2));
        content.SetValue("endDate", DateTime.Now.AddMonths(2).AddDays(1));
        content.SetValue("locationName", "etc.venues");
        content.SetValue("locationAddress", "155 Bishopsgate, London EC2M 3YD");
        content.SetValue("organiserName", "Umbraco Community");
        content.SetValue("ticketPrice", "199.00");
        content.SetValue("ticketUrl", "https://umbracofestival.co.uk/tickets");

        await SaveAndPublishAsync(content);
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
        content.SetValue("email", "oliver@enjoydigital.co.uk");
        content.SetValue("worksFor", "Enjoy Digital");
        content.SetValue("sameAs", "https://github.com/nickyoliverpicton,https://twitter.com/oliverpicton");
        await SaveAndPublishAsync(content);
    }

    private async Task CreateLocationPage(IContentType openingHoursType, int parentId, CancellationToken ct)
    {
        var content = _contentService.Create("Enjoy Digital", parentId, "locationPage");
        content.SetValue("title", "Enjoy Digital");
        content.SetValue("description", "Digital agency specialising in Umbraco CMS development and structured data solutions.");
        content.SetValue("telephone", "+44 113 357 0000");
        content.SetValue("email", "hello@enjoydigital.co.uk");
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
        await SaveAndPublishAsync(content);
    }

    private async Task CreateAboutPage(int parentId, CancellationToken ct)
    {
        var content = _contentService.Create("About Us", parentId, "aboutPage");
        content.SetValue("title", "About Enjoy Digital");
        content.SetValue("description", "We are a digital agency specialising in Umbraco CMS development and structured data solutions.");
        content.SetValue("bodyText", "<p>Enjoy Digital has been building exceptional digital experiences since 2006. We specialise in Umbraco CMS, .NET development, and helping organisations improve their search visibility through structured data.</p>");
        content.SetValue("organisationName", "Enjoy Digital");
        content.SetValue("foundingDate", "2006");
        content.SetValue("numberOfEmployees", "50");
        await SaveAndPublishAsync(content);
    }

    private async Task CreateContactContent(int parentId, CancellationToken ct)
    {
        var content = _contentService.Create("Contact Us", parentId, "contactPage");
        content.SetValue("title", "Contact Us");
        content.SetValue("telephone", "+44 113 357 0000");
        content.SetValue("email", "hello@enjoydigital.co.uk");
        content.SetValue("streetAddress", "7 Park Row");
        content.SetValue("addressLocality", "Leeds");
        content.SetValue("postalCode", "LS1 5HD");
        content.SetValue("openingHours", "Monday-Friday 09:00-17:30");
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
        IContentType bookPageCt, IContentType contactPageCt)
    {
        using var scope = _scopeFactory.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<ISchemaMappingRepository>();

        // Existing mappings
        SeedFaqPageMapping(faqPageCt, repo);
        SeedProductPageMapping(productPageCt, repo);
        SeedRecipePageMapping(recipePageCt, repo);
        SeedBlogArticleMapping(blogArticleCt, repo);
        SeedEventPageMapping(eventPageCt, repo);

        // New mappings — simple property types
        SeedSimpleMapping(repo, homePageCt, "WebSite", ("Name", "siteName"), ("Description", "siteDescription"), ("Url", "__url"));
        SeedSimpleMapping(repo, aboutPageCt, "AboutPage", ("Name", "title"), ("Description", "description"), ("Url", "__url"), ("DateModified", "__updateDate"));
        SeedSimpleMapping(repo, newsArticleCt, "NewsArticle", ("Headline", "title"), ("Description", "description"), ("ArticleBody", "bodyText"), ("DatePublished", "publishDate"), ("Keywords", "keywords"), ("Dateline", "dateline"), ("Url", "__url"));
        SeedSimpleMapping(repo, techArticleCt, "TechArticle", ("Headline", "title"), ("Description", "description"), ("ArticleBody", "bodyText"), ("DatePublished", "publishDate"), ("ProficiencyLevel", "proficiencyLevel"), ("Url", "__url"));
        SeedSimpleMapping(repo, softwarePageCt, "SoftwareApplication", ("Name", "title"), ("Description", "description"), ("ApplicationCategory", "applicationCategory"), ("OperatingSystem", "operatingSystem"), ("SoftwareVersion", "softwareVersion"), ("DownloadUrl", "downloadUrl"), ("Url", "__url"));
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

        _logger.LogInformation("TestDataSeeder: seeded {Count} schema mappings", 23);
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
        var mapping = repo.Save(new SchemaMapping
        {
            ContentTypeAlias = ct.Alias,
            ContentTypeKey = ct.Key,
            SchemaTypeName = schemaTypeName,
            IsEnabled = true,
        });

        repo.SavePropertyMappings(mapping.Id, mappings.Select(m => new PropertyMapping
        {
            SchemaPropertyName = m.schemaProperty,
            SourceType = "property",
            ContentTypePropertyAlias = m.contentProperty,
        }).ToArray());
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
