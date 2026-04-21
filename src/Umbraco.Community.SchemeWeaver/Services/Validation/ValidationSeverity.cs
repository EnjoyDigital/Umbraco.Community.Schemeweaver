namespace Umbraco.Community.SchemeWeaver.Services.Validation;

/// <summary>
/// Severity of a Google Rich Results compliance issue. Critical means the
/// type is ineligible as a rich result; Warning is a recommended field
/// Google suggests but doesn't require.
/// </summary>
public enum ValidationSeverity
{
    Info,
    Warning,
    Critical,
}
