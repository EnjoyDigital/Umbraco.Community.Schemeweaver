using Umbraco.Community.SchemeWeaver.Models.Api;

namespace Umbraco.Community.SchemeWeaver.Services.Validation;

/// <summary>
/// Structural pre-check that warns when a property mapping points a Schema.org
/// property at an object type the property's range does not accept. At
/// generation time such a value is silently dropped by
/// <see cref="SchemaPropertySetter"/> (no implicit conversion exists), so this
/// validator surfaces the problem on Save and Preview before the editor ever
/// renders an empty field.
/// </summary>
public interface ISchemaRangeValidator
{
    /// <summary>
    /// Returns one <see cref="ValidationIssue"/> (Severity = Warning) per
    /// property mapping whose chosen object type is not in the target Schema.org
    /// property's range. Block-content mappings emit one warning per offending
    /// route, keyed by the route's block alias. Scalar / static / reference
    /// mappings are skipped.
    /// </summary>
    IReadOnlyList<ValidationIssue> Validate(SchemaMappingDto mapping);
}
