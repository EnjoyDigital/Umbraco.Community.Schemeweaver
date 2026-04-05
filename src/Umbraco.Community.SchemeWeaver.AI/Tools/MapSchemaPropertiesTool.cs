using System.ComponentModel;
using Microsoft.Extensions.DependencyInjection;
using Umbraco.AI.Core.Tools;
using Umbraco.Community.SchemeWeaver.AI.Services;
using Umbraco.Community.SchemeWeaver.Models.Api;

namespace Umbraco.Community.SchemeWeaver.AI.Tools;

public record MapSchemaPropertiesArgs(
    [property: Description("The alias of the Umbraco content type (e.g., 'blogPost')")]
    string ContentTypeAlias,
    [property: Description("The Schema.org type name to map to (e.g., 'BlogPosting')")]
    string SchemaTypeName);

public record MapSchemaPropertiesResult(
    bool Success,
    PropertyMappingSuggestion[]? Suggestions,
    string? Error);

/// <summary>
/// Copilot tool that suggests property mappings between a content type and a Schema.org type.
/// </summary>
[AITool("schemeweaver_map_properties", "Map Schema.org Properties",
    ScopeId = SchemeWeaverMappingScope.ScopeId)]
public class MapSchemaPropertiesTool : AIToolBase<MapSchemaPropertiesArgs>
{
    private readonly IServiceScopeFactory _scopeFactory;

    public MapSchemaPropertiesTool(IServiceScopeFactory scopeFactory)
    {
        _scopeFactory = scopeFactory;
    }

    public override string Description =>
        "Suggests property mappings between an Umbraco content type and a Schema.org type. " +
        "Returns a list of property mapping suggestions with confidence scores.";

    protected override async Task<object> ExecuteAsync(
        MapSchemaPropertiesArgs args, CancellationToken cancellationToken = default)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var mapper = scope.ServiceProvider.GetRequiredService<IAISchemaMapper>();
            var suggestions = await mapper.SuggestPropertyMappingsAsync(
                args.ContentTypeAlias, args.SchemaTypeName, cancellationToken);

            return new MapSchemaPropertiesResult(true, suggestions, null);
        }
        catch (Exception ex)
        {
            return new MapSchemaPropertiesResult(false, null, ex.Message);
        }
    }
}
