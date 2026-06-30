using System.Collections.Concurrent;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Umbraco.Cms.Core.Services;

namespace Umbraco.Community.SchemeWeaver.Services.ValueSchemas;

/// <inheritdoc />
public sealed class PropertyValueSchemaService : IPropertyValueSchemaService
{
    private readonly IPropertyEditorSchemaService? _schemaService;
    private readonly ILogger<PropertyValueSchemaService> _logger;
    private readonly ConcurrentDictionary<Guid, string?> _cache = new();

    public PropertyValueSchemaService(IServiceProvider serviceProvider, ILogger<PropertyValueSchemaService> logger)
    {
        // IPropertyEditorSchemaService ships with Umbraco 17.4+ (the package floor), so it is
        // normally present. Resolved OPTIONALLY anyway as a defensive measure: if some host variant
        // doesn't register it, value schemas degrade to null (callers fall back to editor-alias
        // behaviour) rather than the whole package failing to start on a missing DI dependency.
        _schemaService = serviceProvider.GetService<IPropertyEditorSchemaService>();
        _logger = logger;
    }

    /// <inheritdoc />
    public bool IsAvailable => _schemaService is not null;

    /// <inheritdoc />
    public async Task<string?> GetDataTypeValueSchemaAsync(Guid dataTypeKey)
    {
        if (_schemaService is null || dataTypeKey == Guid.Empty)
            return null;

        if (_cache.TryGetValue(dataTypeKey, out var cached))
            return cached;

        var schema = await ResolveAsync(dataTypeKey).ConfigureAwait(false);
        _cache[dataTypeKey] = schema;
        return schema;
    }

    private async Task<string?> ResolveAsync(Guid dataTypeKey)
    {
        try
        {
            // GetSchemaAsync fails (not throws) when the data type is unknown or its editor does not
            // implement IValueSchemaProvider — both surface as a null schema here.
            var attempt = await _schemaService!.GetSchemaAsync(dataTypeKey).ConfigureAwait(false);
            return attempt.Success ? attempt.Result?.JsonSchema?.ToJsonString() : null;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to resolve value schema for data type {DataTypeKey}", dataTypeKey);
            return null;
        }
    }
}
