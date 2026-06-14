using Microsoft.Extensions.DependencyInjection;
using Umbraco.AI.Core.Tools;
using Umbraco.Community.SchemeWeaver.Services;

namespace Umbraco.Community.SchemeWeaver.AI.Tools;

public record ListSchemaMappingsResult(
    bool Success,
    IReadOnlyList<MappingSummary>? Mappings,
    string? Error);

public record MappingSummary(
    string ContentTypeAlias,
    string SchemaTypeName,
    int PropertyCount,
    bool IsEnabled);

/// <summary>
/// Copilot tool that lists all existing Schema.org mappings.
/// </summary>
[AITool("schemeweaver_list_mappings", "List Schema.org Mappings",
    ScopeId = SchemeWeaverMappingScope.ScopeId)]
public class ListSchemaMappingsTool : AIToolBase
{
    private readonly IServiceScopeFactory _scopeFactory;

    public ListSchemaMappingsTool(IServiceScopeFactory scopeFactory)
    {
        _scopeFactory = scopeFactory;
    }

    public override string Description =>
        "Lists all existing Schema.org mappings configured in SchemeWeaver. " +
        "Returns each mapping's content type alias, schema type, and property count.";

    protected override Task<object> ExecuteAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var service = scope.ServiceProvider.GetRequiredService<ISchemeWeaverService>();

            var mappings = service.GetAllMappings()
                .Select(m => new MappingSummary(
                    m.ContentTypeAlias,
                    m.SchemaTypeName,
                    m.PropertyMappings.Count,
                    m.IsEnabled))
                .ToList();

            return Task.FromResult<object>(new ListSchemaMappingsResult(true, mappings, null));
        }
        catch (Exception ex)
        {
            return Task.FromResult<object>(new ListSchemaMappingsResult(false, null, ex.Message));
        }
    }
}
