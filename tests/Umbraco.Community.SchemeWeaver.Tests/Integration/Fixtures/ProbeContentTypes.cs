using Microsoft.Extensions.DependencyInjection;
using Umbraco.Cms.Core;
using Umbraco.Cms.Core.Models;
using Umbraco.Cms.Core.Services;
using Umbraco.Cms.Core.Strings;

namespace Umbraco.Community.SchemeWeaver.Tests.Integration.Fixtures;

/// <summary>
/// Creates small real content types in an integration host so the auto-map endpoint has
/// something to map. The integration hosts boot without the uSync fixture import, so the
/// only content types they know are the ones a test makes; this keeps that to one call.
/// </summary>
/// <remarks>
/// Idempotent: an existing alias is left as it is, so the shared host can be probed by more
/// than one test. The TestHost's <c>appsettings.json</c> sets <c>uSync:Settings:ExportOnSave</c>
/// off, so creating a content type here never writes into the repo's uSync fixture folder.
/// </remarks>
internal static class ProbeContentTypes
{
    /// <summary>
    /// Ensures a root-level content type with the given text-box properties exists and returns it.
    /// </summary>
    public static async Task<IContentType> EnsureAsync(
        IServiceProvider services,
        string alias,
        string name,
        params (string Alias, string Name)[] textProperties)
    {
        using var scope = services.CreateScope();
        var contentTypeService = scope.ServiceProvider.GetRequiredService<IContentTypeService>();

        var existing = contentTypeService.Get(alias);
        if (existing is not null)
            return existing;

        var shortStringHelper = scope.ServiceProvider.GetRequiredService<IShortStringHelper>();
        var contentType = new ContentType(shortStringHelper, -1)
        {
            Alias = alias,
            Name = name,
            Icon = "icon-document",
            AllowedAsRoot = true,
        };
        contentType.AddPropertyGroup("content", "Content");
        foreach (var (propertyAlias, propertyName) in textProperties)
        {
            var propertyType = new PropertyType(shortStringHelper, Constants.PropertyEditors.Aliases.TextBox, ValueStorageType.Nvarchar, propertyAlias)
            {
                Name = propertyName,
                DataTypeId = Constants.DataTypes.Textbox,
            };
            contentType.AddPropertyType(propertyType, "content");
        }

        var attempt = await contentTypeService.CreateAsync(contentType, Constants.Security.SuperUserKey);
        if (!attempt.Success)
            throw new InvalidOperationException($"Could not create probe content type '{alias}': {attempt.Result}.");

        return contentTypeService.Get(alias)
            ?? throw new InvalidOperationException($"Probe content type '{alias}' was created but cannot be read back.");
    }
}
