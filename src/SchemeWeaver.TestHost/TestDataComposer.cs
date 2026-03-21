using System.Text.Json;
using Umbraco.Cms.Core;
using Umbraco.Cms.Core.Composing;
using Umbraco.Cms.Core.Models;
using Umbraco.Cms.Core.Services;
using Umbraco.Cms.Core.Strings;

namespace SchemeWeaver.TestHost;

/// <summary>
/// Seeds document types for e2e testing.
/// Creates element types, BlockList data types, and sample document types
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

    public TestDataSeeder(
        IContentTypeService contentTypeService,
        IDataTypeService dataTypeService,
        IShortStringHelper shortStringHelper)
    {
        _contentTypeService = contentTypeService;
        _dataTypeService = dataTypeService;
        _shortStringHelper = shortStringHelper;
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

        // 2. Create BlockList data types
        var faqBlockList = await CreateBlockListDataType("FAQ Items Block List", [faqItem], cancellationToken);
        var reviewsBlockList = await CreateBlockListDataType("Reviews Block List", [reviewItem], cancellationToken);
        var ingredientsBlockList = await CreateBlockListDataType("Ingredients Block List", [recipeIngredient], cancellationToken);
        var instructionsBlockList = await CreateBlockListDataType("Instructions Block List", [recipeStep], cancellationToken);

        // 3. Create content types
        await CreateBlogArticle(textboxDataType, descDataType, bodyDataType, dateTimeDataType, mediaPickerDataType, cancellationToken);
        await CreateProductPage(textboxDataType, descDataType, bodyDataType, mediaPickerDataType, reviewsBlockList, cancellationToken);
        await CreateFaqPage(textboxDataType, descDataType, faqBlockList, cancellationToken);
        await CreateContactPage(textboxDataType, cancellationToken);
        await CreateEventPage(textboxDataType, descDataType, dateTimeDataType, mediaPickerDataType, cancellationToken);
        await CreateRecipePage(textboxDataType, descDataType, mediaPickerDataType, ingredientsBlockList, instructionsBlockList, cancellationToken);
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

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

    private async Task<IDataType> CreateBlockListDataType(
        string name,
        IContentType[] elementTypes,
        CancellationToken cancellationToken)
    {
        var blocks = elementTypes.Select(et => new
        {
            contentElementTypeKey = et.Key,
            label = et.Name,
        });

        var configData = new Dictionary<string, object>
        {
            ["blocks"] = JsonSerializer.Serialize(blocks),
            ["validationLimit"] = JsonSerializer.Serialize(new { min = 0, max = 0 }),
        };

        var dataType = new DataType(_shortStringHelper, -1)
        {
            Name = name,
            EditorAlias = Constants.PropertyEditors.Aliases.BlockList,
            DatabaseType = ValueStorageType.Ntext,
            ConfigurationData = configData,
        };

        await _dataTypeService.CreateAsync(dataType, Constants.Security.SuperUserKey).ConfigureAwait(false);
        return dataType;
    }

    private async Task CreateBlogArticle(
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
    }

    private async Task CreateProductPage(
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
    }

    private async Task CreateFaqPage(
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
    }

    private async Task CreateContactPage(
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
    }

    private async Task CreateEventPage(
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
    }

    private async Task CreateRecipePage(
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
}
