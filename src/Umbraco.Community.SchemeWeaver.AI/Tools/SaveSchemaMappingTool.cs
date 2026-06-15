using System.ComponentModel;
using Microsoft.Extensions.DependencyInjection;
using Umbraco.AI.Core.Tools;
using Umbraco.Cms.Core.Services;
using Umbraco.Community.SchemeWeaver.Models.Api;
using Umbraco.Community.SchemeWeaver.Services;

namespace Umbraco.Community.SchemeWeaver.AI.Tools;

public record SaveSchemaMappingArgs(
    [property: Description("The alias of the Umbraco content type (e.g., 'blogPost')")]
    string ContentTypeAlias,
    [property: Description("The Schema.org type name (e.g., 'BlogPosting')")]
    string SchemaTypeName,
    [property: Description(
        "Array of property mappings. Each entry maps one Schema.org property to one Umbraco property. " +
        "Use the suggestions from schemeweaver_map_properties: transfer suggestedContentTypePropertyAlias " +
        "→ contentTypePropertyAlias and suggestedSourceType → sourceType. " +
        "Omit entries where confidence is 0 and no mapping is appropriate.")]
    SaveSchemaMappingPropertyArg[] PropertyMappings);

public record SaveSchemaMappingPropertyArg(
    [property: Description("The Schema.org property name (e.g., 'headline')")]
    string SchemaPropertyName,
    [property: Description(
        "The Umbraco content type property alias to read the value from (e.g., 'title', '__name', '__url'). " +
        "Required when sourceType is 'property'. Set to null when sourceType is 'static'.")]
    string? ContentTypePropertyAlias,
    [property: Description(
        "How to resolve the value. Use 'property' to read from a property on the current content node " +
        "(including built-ins __name, __url, __createDate, __updateDate). " +
        "Use 'static' to store a hardcoded literal value — set staticValue instead of contentTypePropertyAlias.")]
    string SourceType = "property",
    [property: Description("The hardcoded literal value to store when sourceType is 'static' (e.g., 'en-GB', 'https://schema.org/InStock').")]
    string? StaticValue = null);

public record SaveSchemaMappingResult(
    bool Success,
    string? Message);

/// <summary>
/// Copilot tool that saves a schema mapping. This is a destructive operation.
/// </summary>
[AITool("schemeweaver_save_mapping", "Save Schema.org Mapping",
    ScopeId = SchemeWeaverMappingScope.ScopeId, IsDestructive = true)]
public class SaveSchemaMappingTool : AIToolBase<SaveSchemaMappingArgs>
{
    private readonly IServiceScopeFactory _scopeFactory;

    public SaveSchemaMappingTool(IServiceScopeFactory scopeFactory)
    {
        _scopeFactory = scopeFactory;
    }

    public override string Description =>
        "Saves a Schema.org mapping for an Umbraco content type, creating or overwriting any existing mapping. " +
        "Always call schemeweaver_map_properties first to obtain property suggestions, then pass those suggestions " +
        "as propertyMappings (transferring suggestedContentTypePropertyAlias → contentTypePropertyAlias and " +
        "suggestedSourceType → sourceType). Only supported source types are 'property' and 'static'; " +
        "entries with an empty contentTypePropertyAlias and sourceType 'property' are ignored.";

    protected override Task<object> ExecuteAsync(
        SaveSchemaMappingArgs args, CancellationToken cancellationToken = default)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var service = scope.ServiceProvider.GetRequiredService<ISchemeWeaverService>();
            var contentTypeService = scope.ServiceProvider.GetRequiredService<IContentTypeService>();

            var contentType = contentTypeService.Get(args.ContentTypeAlias);
            var dto = new SchemaMappingDto
            {
                ContentTypeAlias = args.ContentTypeAlias,
                ContentTypeKey = contentType?.Key ?? Guid.Empty,
                SchemaTypeName = args.SchemaTypeName,
                IsEnabled = true,
                IsInherited = false,
                PropertyMappings = args.PropertyMappings
                    .Where(p => !string.IsNullOrEmpty(p.ContentTypePropertyAlias) || p.SourceType == "static")
                    .Select(p => new PropertyMappingDto
                    {
                        SchemaPropertyName = p.SchemaPropertyName,
                        ContentTypePropertyAlias = p.ContentTypePropertyAlias,
                        SourceType = p.SourceType,
                        StaticValue = p.StaticValue,
                        IsAutoMapped = true,
                    })
                    .ToList(),
            };

            service.SaveMapping(dto);
            return Task.FromResult<object>(new SaveSchemaMappingResult(true,
                $"Saved mapping: {args.ContentTypeAlias} → {args.SchemaTypeName} with {dto.PropertyMappings.Count} properties."));
        }
        catch (Exception ex)
        {
            return Task.FromResult<object>(new SaveSchemaMappingResult(false, ex.Message));
        }
    }
}
