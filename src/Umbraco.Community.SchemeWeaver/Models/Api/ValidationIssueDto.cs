namespace Umbraco.Community.SchemeWeaver.Models.Api;

/// <summary>
/// One finding on a mapping or preview response. Serialised camelCase like every other DTO.
/// <see cref="Severity"/> is a lowercase string the frontend switches on directly:
/// <c>critical</c> / <c>warning</c> / <c>info</c> (from the internal <c>ValidationSeverity</c> enum),
/// plus <c>suggestion</c> — a non-blocking improvement the mapping advisor emits as a literal string
/// (it never flows through the enum).
/// </summary>
public sealed record ValidationIssueDto(
    string Severity,
    string SchemaType,
    string Path,
    string Message);
