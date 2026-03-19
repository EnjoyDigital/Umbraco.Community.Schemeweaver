using Umbraco.Cms.Core;
using Umbraco.Cms.Core.Composing;
using Umbraco.Cms.Core.Models;
using Umbraco.Cms.Core.Services;
using Umbraco.Cms.Core.Strings;

namespace SchemeWeaver.TestHost;

/// <summary>
/// Seeds document types for e2e testing.
/// Creates a few sample document types so the SchemeWeaver dashboard has data to display.
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

        // Use textbox as fallback if others are not found
        var bodyDataType = richtextDataType ?? textareaDataType ?? textboxDataType;
        var descDataType = textareaDataType ?? textboxDataType;

        if (textboxDataType is null) return; // Can't seed without at least textbox

        await CreateContentType("blogArticle", "Blog Article", "icon-edit", textboxDataType, descDataType, bodyDataType, cancellationToken);
        await CreateContentType("productPage", "Product Page", "icon-box", textboxDataType, descDataType, bodyDataType, cancellationToken);
        await CreateContentType("faqPage", "FAQ Page", "icon-help-alt", textboxDataType, descDataType, bodyDataType, cancellationToken);
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    private async Task CreateContentType(
        string alias, string name, string icon,
        IDataType textboxDataType, IDataType? descDataType, IDataType? bodyDataType,
        CancellationToken cancellationToken)
    {
        var ct = new ContentType(_shortStringHelper, -1)
        {
            Alias = alias,
            Name = name,
            Icon = icon,
            AllowedAsRoot = true,
        };

        ct.AddPropertyGroup("content", "Content");

        ct.AddPropertyType(new PropertyType(_shortStringHelper, textboxDataType)
        {
            Alias = "title",
            Name = "Title",
            Mandatory = true,
        }, "content", "Content");

        if (descDataType is not null)
        {
            ct.AddPropertyType(new PropertyType(_shortStringHelper, descDataType)
            {
                Alias = "description",
                Name = "Description",
            }, "content", "Content");
        }

        if (bodyDataType is not null)
        {
            ct.AddPropertyType(new PropertyType(_shortStringHelper, bodyDataType)
            {
                Alias = "bodyText",
                Name = "Body Text",
            }, "content", "Content");
        }

        await _contentTypeService.CreateAsync(ct, Constants.Security.SuperUserKey).ConfigureAwait(false);
    }
}
