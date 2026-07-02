using System.Text.Json;
using Umbraco.Community.SchemeWeaver.Models.Api;
using Umbraco.Community.SchemeWeaver.Services.Resolvers;

namespace Umbraco.Community.SchemeWeaver.Services.Validation;

/// <inheritdoc />
public class SchemaRangeValidator : ISchemaRangeValidator
{
    private readonly ISchemaTypeRegistry _registry;
    private readonly ISchemaRangeChecker _rangeChecker;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    // Deterministic preference order for the "map it here instead" hint. These
    // are the catch-all Schema.org properties whose range is broad enough to
    // accept almost any Thing, so they're the safest re-home for a value that
    // doesn't fit its current property.
    private static readonly string[] PreferredAlternatives = ["about", "mainEntity", "mentions"];

    public SchemaRangeValidator(ISchemaTypeRegistry registry, ISchemaRangeChecker rangeChecker)
    {
        _registry = registry;
        _rangeChecker = rangeChecker;
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

            if (_rangeChecker.IsInRange(chosenClr, targetProp.AcceptedTypes))
                continue;

            issues.Add(BuildIssue(
                mapping, pm.SchemaPropertyName, targetProp, pm.NestedSchemaTypeName!,
                chosenClr, schemaProps, blockAlias: null));
        }

        return issues;
    }

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
