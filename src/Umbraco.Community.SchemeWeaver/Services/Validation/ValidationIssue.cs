namespace Umbraco.Community.SchemeWeaver.Services.Validation;

/// <summary>
/// One finding against a single JSON-LD node. <paramref name="Path"/> is a
/// JSON-Pointer-ish locator (e.g. <c>@graph[2].offers.price</c>) so the
/// audit report can point at the exact field.
/// </summary>
public sealed record ValidationIssue(
    ValidationSeverity Severity,
    string SchemaType,
    string Path,
    string Message);
