using System.ComponentModel;
using Microsoft.Extensions.DependencyInjection;
using Umbraco.AI.Core.Tools;
using Umbraco.Community.SchemeWeaver.AI.Models;
using Umbraco.Community.SchemeWeaver.AI.Services;

namespace Umbraco.Community.SchemeWeaver.AI.Tools;

public record SuggestSchemaTypeArgs(
    [property: Description("The alias of the Umbraco content type to analyse (e.g., 'blogPost', 'homePage')")]
    string ContentTypeAlias);

public record SuggestSchemaTypeResult(
    bool Success,
    SchemaTypeSuggestion[]? Suggestions,
    string? Error);

/// <summary>
/// Copilot tool that suggests Schema.org types for an Umbraco content type.
/// </summary>
[AITool("schemeweaver_suggest_schema_type", "Suggest Schema.org Type",
    ScopeId = SchemeWeaverMappingScope.ScopeId)]
public class SuggestSchemaTypeTool : AIToolBase<SuggestSchemaTypeArgs>
{
    private readonly IServiceScopeFactory _scopeFactory;

    public SuggestSchemaTypeTool(IServiceScopeFactory scopeFactory)
    {
        _scopeFactory = scopeFactory;
    }

    public override string Description =>
        "Analyses an Umbraco content type and suggests the most appropriate Schema.org types. " +
        "Returns ranked suggestions with confidence scores and reasoning.";

    protected override async Task<object> ExecuteAsync(
        SuggestSchemaTypeArgs args, CancellationToken cancellationToken = default)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var mapper = scope.ServiceProvider.GetRequiredService<IAISchemaMapper>();
            var suggestions = await mapper.SuggestSchemaTypesAsync(
                args.ContentTypeAlias, cancellationToken);

            return new SuggestSchemaTypeResult(true, suggestions, null);
        }
        catch (Exception ex)
        {
            return new SuggestSchemaTypeResult(false, null, ex.Message);
        }
    }
}
