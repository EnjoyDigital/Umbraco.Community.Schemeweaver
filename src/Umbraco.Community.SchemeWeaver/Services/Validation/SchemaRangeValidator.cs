using System.Text.Json;
using Umbraco.Cms.Core.Services;
using Umbraco.Community.SchemeWeaver.Models.Api;
using Umbraco.Community.SchemeWeaver.Services.Resolvers;

namespace Umbraco.Community.SchemeWeaver.Services.Validation;

/// <inheritdoc />
public class SchemaRangeValidator : ISchemaRangeValidator
{
    private readonly ISchemaTypeRegistry _registry;
    private readonly ISchemaRangeChecker _rangeChecker;
    private readonly IContentTypeService? _contentTypeService;

    private static HashSet<string> MediaPickerAliases => SchemeWeaverConstants.PropertyEditors.MediaPickerAliases;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    // Deterministic preference order for the "map it here instead" hint. These
    // are the catch-all Schema.org properties whose range is broad enough to
    // accept almost any Thing, so they're the safest re-home for a value that
    // doesn't fit its current property.
    private static readonly string[] PreferredAlternatives = ["about", "mainEntity", "mentions"];

    /// <param name="registry">Schema registry for property/CLR-type lookup.</param>
    /// <param name="rangeChecker">Range checker for Schema.NET assignability.</param>
    /// <param name="contentTypeService">Optional: enables editor-alias lookup for the inner
    /// complexTypeMappings inspection (media-picker-onto-string detection). When null those
    /// editor-dependent checks are skipped; everything else still validates.</param>
    public SchemaRangeValidator(
        ISchemaTypeRegistry registry,
        ISchemaRangeChecker rangeChecker,
        IContentTypeService? contentTypeService = null)
    {
        _registry = registry;
        _rangeChecker = rangeChecker;
        _contentTypeService = contentTypeService;
    }

    public IReadOnlyList<ValidationIssue> Validate(SchemaMappingDto mapping)
    {
        var issues = new List<ValidationIssue>();
        if (mapping is null || string.IsNullOrWhiteSpace(mapping.SchemaTypeName))
            return issues;

        var schemaProps = _registry.GetProperties(mapping.SchemaTypeName).ToList();
        if (schemaProps.Count == 0)
            return issues;

        foreach (var pm in mapping.PropertyMappings)
        {
            // reference: the type lives in another graph piece (TargetPieceKey);
            // resolving its range needs whole-graph context we don't have here.
            // Deferred for v1.
            if (string.Equals(pm.SourceType, "reference", StringComparison.OrdinalIgnoreCase))
                continue;

            var targetProp = schemaProps.FirstOrDefault(
                p => string.Equals(p.Name, pm.SchemaPropertyName, StringComparison.OrdinalIgnoreCase));
            if (targetProp is null)
                continue; // unknown target property — nothing to reason about

            if (string.Equals(pm.SourceType, "blockContent", StringComparison.OrdinalIgnoreCase))
            {
                ValidateBlockRoutes(pm, mapping, targetProp, schemaProps, issues);
                continue;
            }

            // complexType / nested type / content-picker store the chosen object
            // type in NestedSchemaTypeName. static / plain scalar mappings leave it
            // null — there's no object type to range-check, so skip (false-positive
            // guard for auto-wrapped scalars like textbox -> author).
            if (string.IsNullOrWhiteSpace(pm.NestedSchemaTypeName))
                continue;

            var chosenClr = _registry.GetClrType(pm.NestedSchemaTypeName);
            if (chosenClr is null)
                continue; // typo / unknown chosen type — skip gracefully

            if (!_rangeChecker.IsInRange(chosenClr, targetProp.AcceptedTypes))
            {
                issues.Add(BuildIssue(
                    mapping, pm.SchemaPropertyName, targetProp, pm.NestedSchemaTypeName!,
                    chosenClr, schemaProps, blockAlias: null));
                continue;
            }

            // Outer type is in range — but the INNER complexTypeMappings can still be
            // broken (unknown sub-properties, media pickers bound onto string-only
            // sub-properties, out-of-range nested sub-types). Inspect them too.
            ValidateComplexTypeConfig(pm, mapping, issues);
        }

        return issues;
    }

    /// <summary>
    /// Inspects a complexType mapping's inner <c>complexTypeMappings</c> (the
    /// <see cref="ComplexTypeConfigModel"/> shape persisted in ResolverConfig JSON) and warns on:
    /// sub-properties that don't exist on the nested type; property-sourced sub-mappings that
    /// bind a media picker onto a sub-property accepting neither ImageObject nor URL (the
    /// resolved ImageObject is silently dropped, leaving an empty nested shell); and
    /// complexType-sourced entries whose SelectedSubType is outside the sub-property's range
    /// (one level of recursion, mirroring <see cref="ValidateBlockRoutes"/>).
    /// </summary>
    private void ValidateComplexTypeConfig(
        PropertyMappingDto pm,
        SchemaMappingDto mapping,
        List<ValidationIssue> issues)
    {
        ComplexTypeConfigModel? config;
        try
        {
            config = string.IsNullOrEmpty(pm.ResolverConfig)
                ? null
                : JsonSerializer.Deserialize<ComplexTypeConfigModel>(pm.ResolverConfig, JsonOptions);
        }
        catch (JsonException)
        {
            return;
        }

        if (config?.ComplexTypeMappings is not { Count: > 0 } entries)
            return;

        var nestedProps = _registry.GetProperties(pm.NestedSchemaTypeName!).ToList();
        if (nestedProps.Count == 0)
            return;

        foreach (var entry in entries)
        {
            var subProp = nestedProps.FirstOrDefault(
                p => string.Equals(p.Name, entry.SchemaProperty, StringComparison.OrdinalIgnoreCase));
            if (subProp is null)
            {
                issues.Add(new ValidationIssue(
                    ValidationSeverity.Warning, mapping.SchemaTypeName, pm.SchemaPropertyName,
                    $"'{pm.SchemaPropertyName}' has an inner mapping for '{entry.SchemaProperty}', " +
                    $"which does not exist on '{pm.NestedSchemaTypeName}' — the value will be dropped."));
                continue;
            }

            if (string.Equals(entry.SourceType, "property", StringComparison.OrdinalIgnoreCase))
            {
                ValidateMediaOntoSubProperty(pm, mapping, entry, subProp, issues);
            }
            else if (string.Equals(entry.SourceType, "complexType", StringComparison.OrdinalIgnoreCase))
            {
                ValidateNestedSubType(pm, mapping, entry, subProp, issues);
            }
        }
    }

    /// <summary>
    /// Warns when a property-sourced sub-mapping binds a media-picker content property onto a
    /// nested sub-property that accepts neither ImageObject nor URL — the resolver's ImageObject
    /// is dropped by the strongly-typed setter (e.g. string-only ImageObject.Name), so the
    /// nested object renders as an empty shell. The media property should be mapped directly
    /// to the outer schema property as a plain property source instead.
    /// </summary>
    private void ValidateMediaOntoSubProperty(
        PropertyMappingDto pm,
        SchemaMappingDto mapping,
        ComplexTypeMappingEntry entry,
        SchemaPropertyInfo subProp,
        List<ValidationIssue> issues)
    {
        var editorAlias = GetEditorAlias(mapping.ContentTypeAlias, entry.ContentTypePropertyAlias);
        if (editorAlias is null || !MediaPickerAliases.Contains(editorAlias))
            return;

        if (AcceptsMedia(subProp.AcceptedTypes))
            return;

        issues.Add(new ValidationIssue(
            ValidationSeverity.Warning, mapping.SchemaTypeName, pm.SchemaPropertyName,
            $"'{pm.SchemaPropertyName}' binds media picker '{entry.ContentTypePropertyAlias}' onto " +
            $"'{pm.NestedSchemaTypeName}.{subProp.Name}', which accepts neither an ImageObject nor a URL — " +
            "the resolved media is dropped and an empty shell is emitted. " +
            $"Map '{entry.ContentTypePropertyAlias}' directly to '{pm.SchemaPropertyName}' as a property source instead."));
    }

    /// <summary>
    /// Range-checks a complexType-sourced sub-entry's SelectedSubType against the sub-property's
    /// accepted types — one level of recursion, mirroring <see cref="ValidateBlockRoutes"/>.
    /// </summary>
    private void ValidateNestedSubType(
        PropertyMappingDto pm,
        SchemaMappingDto mapping,
        ComplexTypeMappingEntry entry,
        SchemaPropertyInfo subProp,
        List<ValidationIssue> issues)
    {
        ComplexTypeConfigModel? subConfig;
        try
        {
            subConfig = string.IsNullOrEmpty(entry.ResolverConfig)
                ? null
                : JsonSerializer.Deserialize<ComplexTypeConfigModel>(entry.ResolverConfig, JsonOptions);
        }
        catch (JsonException)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(subConfig?.SelectedSubType))
            return;

        var subClr = _registry.GetClrType(subConfig.SelectedSubType);
        if (subClr is null)
            return;

        if (_rangeChecker.IsInRange(subClr, subProp.AcceptedTypes))
            return;

        var acceptedList = subProp.AcceptedTypes.Count > 0
            ? string.Join(", ", subProp.AcceptedTypes)
            : "(no object types)";

        issues.Add(new ValidationIssue(
            ValidationSeverity.Warning, mapping.SchemaTypeName, pm.SchemaPropertyName,
            $"'{pm.SchemaPropertyName}': inner '{pm.NestedSchemaTypeName}.{subProp.Name}' accepts {acceptedList} " +
            $"but is mapped to '{subConfig.SelectedSubType}', which is not in that range — the value will be dropped."));
    }

    /// <summary>
    /// Resolves the editor alias behind a content property via the mapping's content type.
    /// Returns null when no <see cref="IContentTypeService"/> was injected or nothing matches.
    /// </summary>
    private string? GetEditorAlias(string? contentTypeAlias, string? propertyAlias)
    {
        if (_contentTypeService is null
            || string.IsNullOrWhiteSpace(contentTypeAlias)
            || string.IsNullOrWhiteSpace(propertyAlias))
            return null;

        return _contentTypeService.Get(contentTypeAlias)?
            .CompositionPropertyTypes
            .FirstOrDefault(p => string.Equals(p.Alias, propertyAlias, StringComparison.OrdinalIgnoreCase))?
            .PropertyEditorAlias;
    }

    /// <summary>
    /// True when the sub-property can carry resolved media: an ImageObject fits its range
    /// (directly or via a MediaObject/CreativeWork base) or it accepts a plain URL.
    /// </summary>
    private bool AcceptsMedia(IReadOnlyList<string> acceptedTypes) =>
        _rangeChecker.IsInRange("ImageObject", acceptedTypes)
        || acceptedTypes.Any(t => string.Equals(t, "Uri", StringComparison.OrdinalIgnoreCase)
                                  || string.Equals(t, "URL", StringComparison.OrdinalIgnoreCase));

    private void ValidateBlockRoutes(
        PropertyMappingDto pm,
        SchemaMappingDto mapping,
        SchemaPropertyInfo targetProp,
        List<SchemaPropertyInfo> schemaProps,
        List<ValidationIssue> issues)
    {
        // A block list is heterogeneous: the single target property is fed by
        // several routes, one per block element type. We must range-check each
        // route's NestedSchemaType individually — NestedSchemaTypeName is null
        // for blockContent mappings.
        ResolverConfigModel? config;
        try
        {
            config = string.IsNullOrEmpty(pm.ResolverConfig)
                ? null
                : JsonSerializer.Deserialize<ResolverConfigModel>(pm.ResolverConfig, JsonOptions);
        }
        catch (JsonException)
        {
            return;
        }

        if (config?.Routes is not { Count: > 0 } routes)
            return;

        foreach (var route in routes.Where(r => !string.IsNullOrWhiteSpace(r.NestedSchemaType)))
        {
            var chosenClr = _registry.GetClrType(route.NestedSchemaType!);
            if (chosenClr is null)
                continue;

            if (_rangeChecker.IsInRange(chosenClr, targetProp.AcceptedTypes))
                continue;

            issues.Add(BuildIssue(
                mapping, pm.SchemaPropertyName, targetProp, route.NestedSchemaType!,
                chosenClr, schemaProps, route.BlockAlias));
        }
    }

    private ValidationIssue BuildIssue(
        SchemaMappingDto mapping,
        string schemaPropertyName,
        SchemaPropertyInfo targetProp,
        string chosenType,
        Type chosenClr,
        List<SchemaPropertyInfo> schemaProps,
        string? blockAlias)
    {
        var acceptedList = targetProp.AcceptedTypes.Count > 0
            ? string.Join(", ", targetProp.AcceptedTypes)
            : "(no object types)";

        var suggestion = SuggestAlternative(targetProp, chosenClr, schemaProps);

        var prefix = string.IsNullOrWhiteSpace(blockAlias)
            ? string.Empty
            : $"Block '{blockAlias}': ";

        var message =
            $"{prefix}'{schemaPropertyName}' accepts {acceptedList} but is mapped to '{chosenType}', " +
            "which is not in that range — the value will be dropped. " +
            $"Map it to {suggestion} instead, or change the block/nested type.";

        // Path == the stored SchemaPropertyName so the frontend can key the
        // warning back to its mapping row.
        return new ValidationIssue(ValidationSeverity.Warning, mapping.SchemaTypeName, schemaPropertyName, message);
    }

    private string SuggestAlternative(SchemaPropertyInfo currentProp, Type chosenClr, List<SchemaPropertyInfo> schemaProps)
    {
        foreach (var name in PreferredAlternatives)
        {
            var prop = schemaProps.FirstOrDefault(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));
            if (prop is not null && !ReferenceEquals(prop, currentProp) && _rangeChecker.IsInRange(chosenClr, prop.AcceptedTypes))
                return $"'{prop.Name}'";
        }

        var fallback = schemaProps.FirstOrDefault(
            p => !ReferenceEquals(p, currentProp) && _rangeChecker.IsInRange(chosenClr, p.AcceptedTypes));
        if (fallback is not null)
            return $"'{fallback.Name}'";

        return "about or mainEntity";
    }
}
