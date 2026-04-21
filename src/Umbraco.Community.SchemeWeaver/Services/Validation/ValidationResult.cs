namespace Umbraco.Community.SchemeWeaver.Services.Validation;

/// <summary>
/// Aggregated validator output for one JSON-LD document (a Thing or a @graph).
/// </summary>
public sealed record ValidationResult(
    IReadOnlyList<ValidationIssue> Issues)
{
    public int CriticalCount => Issues.Count(i => i.Severity == ValidationSeverity.Critical);
    public int WarningCount => Issues.Count(i => i.Severity == ValidationSeverity.Warning);
    public bool HasCritical => CriticalCount > 0;
    public bool IsValid => !HasCritical;

    public static ValidationResult Empty { get; } = new(Array.Empty<ValidationIssue>());
}
