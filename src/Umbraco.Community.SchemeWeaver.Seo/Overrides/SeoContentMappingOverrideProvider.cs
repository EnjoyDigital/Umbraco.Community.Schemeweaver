using Microsoft.Extensions.Logging;
using Umbraco.Cms.Core.Models.PublishedContent;
using Umbraco.Community.SchemeWeaver.Models.Entities;
using Umbraco.Community.SchemeWeaver.Services;
using Umbraco.Community.SchemeWeaver.Services.Validation;

namespace Umbraco.Community.SchemeWeaver.Seo.Overrides;

/// <summary>
/// The core's <see cref="IContentMappingOverrideProvider"/> seam, backed by the override table.
/// A retype is honoured only when the override's type is still a subtype of the mapping's
/// type (a developer may have changed the type since the SEO set the override).
/// </summary>
public sealed class SeoContentMappingOverrideProvider : IContentMappingOverrideProvider
{
    private readonly ISeoPageOverrideRepository _repository;
    private readonly ISchemaTypeRegistry _registry;
    private readonly ISchemaRangeChecker _rangeChecker;
    private readonly ILogger<SeoContentMappingOverrideProvider> _logger;

    public SeoContentMappingOverrideProvider(
        ISeoPageOverrideRepository repository,
        ISchemaTypeRegistry registry,
        ISchemaRangeChecker rangeChecker,
        ILogger<SeoContentMappingOverrideProvider> logger)
    {
        _repository = repository;
        _registry = registry;
        _rangeChecker = rangeChecker;
        _logger = logger;
    }

    public MappingOverrideResult Resolve(IPublishedContent content, SchemaMapping mapping, string? culture)
    {
        SeoPageOverride? @override;
        try
        {
            @override = _repository.Get(content.Key);
        }
        catch (Exception ex)
        {
            // Never break a render over an override lookup.
            _logger.LogWarning(ex, "SchemeWeaver SEO override lookup failed for {ContentKey}", content.Key);
            return MappingOverrideResult.None;
        }

        if (@override is null)
            return MappingOverrideResult.None;

        if (@override.IsExcluded)
            return MappingOverrideResult.Excluded;

        var type = ResolveEffectiveType(mapping.SchemaTypeName, @override.SchemaTypeName, out var invalid);
        if (invalid)
        {
            _logger.LogWarning(
                "SchemeWeaver SEO page override {Type} on {ContentKey} is not a subtype of the mapped {Base}; ignoring",
                @override.SchemaTypeName, content.Key, mapping.SchemaTypeName);
        }

        if (type is null)
            return MappingOverrideResult.None;

        var clone = mapping.Clone();
        clone.SchemaTypeName = type;
        return MappingOverrideResult.Retyped(clone);
    }

    /// <summary>
    /// The one rule for "does this override change the emitted type": returns the override type
    /// when it is a known subtype of <paramref name="mappedType"/> and differs from it; null
    /// otherwise. <paramref name="invalid"/> is true when a non-empty override was rejected.
    /// Used by the render path above and by the page summary so they can never disagree.
    /// </summary>
    public string? ResolveEffectiveType(string mappedType, string? overrideType, out bool invalid)
    {
        invalid = false;
        var type = overrideType?.Trim();
        if (string.IsNullOrEmpty(type) || string.Equals(type, mappedType, StringComparison.OrdinalIgnoreCase))
            return null;

        if (_registry.GetClrType(type) is null || !_rangeChecker.IsInRange(type, [mappedType]))
        {
            invalid = true;
            return null;
        }

        return type;
    }
}
