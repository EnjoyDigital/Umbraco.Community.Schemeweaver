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

        // 2. Create BlockList data types
        var faqBlockList = await CreateBlockListDataType("FAQ Items Block List", [faqItem], cancellationToken);
        var reviewsBlockList = await CreateBlockListDataType("Reviews Block List", [reviewItem], cancellationToken);
        var ingredientsBlockList = await CreateBlockListDataType("Ingredients Block List", [recipeIngredient], cancellationToken);
        var instructionsBlockList = await CreateBlockListDataType("Instructions Block List", [recipeStep], cancellationToken);

        // 3. Create content types
        var blogArticleCt = await CreateBlogArticle(textboxDataType, descDataType, bodyDataType, dateTimeDataType, mediaPickerDataType, cancellationToken);
        var productPageCt = await CreateProductPage(textboxDataType, descDataType, bodyDataType, mediaPickerDataType, reviewsBlockList, cancellationToken);
        var faqPageCt = await CreateFaqPage(textboxDataType, descDataType, faqBlockList, cancellationToken);
        await CreateContactPage(textboxDataType, cancellationToken);
        var eventPageCt = await CreateEventPage(textboxDataType, descDataType, dateTimeDataType, mediaPickerDataType, cancellationToken);
        var recipePageCt = await CreateRecipePage(textboxDataType, descDataType, mediaPickerDataType, ingredientsBlockList, instructionsBlockList, cancellationToken);

        // 4. Create and publish sample content
        await CreateSampleContent(
            faqItem, reviewItem, recipeIngredient, recipeStep,
            cancellationToken);

        // 5. Seed default schema mappings
        SeedSchemaMappings(blogArticleCt, productPageCt, faqPageCt, eventPageCt, recipePageCt);
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

        _propertyEditors.TryGet(Constants.PropertyEditors.Aliases.BlockList, out var blockListEditor);

        var dataType = new DataType(blockListEditor, _configSerializer, -1)
        {
            Name = name,
            DatabaseType = ValueStorageType.Ntext,
            ConfigurationData = configData,
        };

        await _dataTypeService.CreateAsync(dataType, Constants.Security.SuperUserKey).ConfigureAwait(false);
        return dataType;
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
        CancellationToken cancellationToken)
    {
        await CreateFaqContent(faqItemType, cancellationToken);
        await CreateProductContent(reviewItemType, cancellationToken);
        await CreateRecipeContent(recipeIngredientType, recipeStepType, cancellationToken);
        await CreateBlogContent(cancellationToken);
        await CreateEventContent(cancellationToken);

        _logger.LogInformation("TestDataSeeder: created and published sample content nodes");
    }

    private async Task CreateFaqContent(IContentType faqItemType, CancellationToken cancellationToken)
    {
        var content = _contentService.Create("Frequently Asked Questions", Constants.System.Root, "faqPage");
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

    private async Task CreateProductContent(IContentType reviewItemType, CancellationToken cancellationToken)
    {
        var content = _contentService.Create("Wireless Headphones Pro", Constants.System.Root, "productPage");
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
        CancellationToken cancellationToken)
    {
        var content = _contentService.Create("Classic Victoria Sponge", Constants.System.Root, "recipePage");
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

    private async Task CreateBlogContent(CancellationToken cancellationToken)
    {
        var content = _contentService.Create("Getting Started with Schema.org", Constants.System.Root, "blogArticle");
        content.SetValue("title", "Getting Started with Schema.org");
        content.SetValue("description", "Learn how structured data helps search engines understand your content.");
        content.SetValue("bodyText", "<p>Schema.org provides a shared vocabulary for structured data markup on web pages.</p>");
        content.SetValue("authorName", "Oliver");
        content.SetValue("publishDate", DateTime.Now);
        content.SetValue("keywords", "schema.org, structured data, SEO");
        content.SetValue("category", "Technology");

        await SaveAndPublishAsync(content);
    }

    private async Task CreateEventContent(CancellationToken cancellationToken)
    {
        var content = _contentService.Create("Umbraco UK Festival 2026", Constants.System.Root, "eventPage");
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
        IContentType blogArticleCt,
        IContentType productPageCt,
        IContentType faqPageCt,
        IContentType eventPageCt,
        IContentType recipePageCt)
    {
        using var scope = _scopeFactory.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<ISchemaMappingRepository>();
        SeedFaqPageMapping(faqPageCt, repo);
        SeedProductPageMapping(productPageCt, repo);
        SeedRecipePageMapping(recipePageCt, repo);
        SeedBlogArticleMapping(blogArticleCt, repo);
        SeedEventPageMapping(eventPageCt, repo);

        _logger.LogInformation("TestDataSeeder: seeded default schema mappings");
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
