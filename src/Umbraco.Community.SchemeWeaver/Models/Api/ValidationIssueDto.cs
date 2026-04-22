namespace Umbraco.Community.SchemeWeaver.Models.Api;

/// <summary>
/// One Google Rich Results compliance finding on a preview response.
/// Serialised camelCase like every other DTO. <see cref="Severity"/> is the
/// lowercase form of the internal enum (<c>critical</c> / <c>warning</c> /
/// <c>info</c>) so the frontend can switch on the string directly without
/// re-serialising.
/// </summary>
public sealed record ValidationIssueDto(
    string Severity,
    string SchemaType,
    string Path,
    string Message);
