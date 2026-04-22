namespace Umbraco.Community.SchemeWeaver.Models.Api;

/// <summary>
/// Response containing a JSON-LD preview with validation results.
/// </summary>
public class JsonLdPreviewResponse
{
    public string JsonLd { get; set; } = string.Empty;

    /// <summary>
    /// True when no <see cref="Issues"/> of severity <c>critical</c> are present
    /// — i.e. the JSON-LD is eligible for Google Rich Results according to
    /// the rule-sets that ship with SchemeWeaver.
    /// </summary>
    public bool IsValid { get; set; }

    /// <summary>
    /// Legacy string-only errors for consumers that haven't upgraded to read
    /// the richer <see cref="Issues"/> field. Populated with the
    /// <c>Message</c> of every Critical entry in <see cref="Issues"/> (plus
    /// any pre-validation generation error) so older clients still see the
    /// blocking problems.
    /// </summary>
    public List<string> Errors { get; set; } = [];

    /// <summary>
    /// Structured Rich Results compliance findings from the
    /// <see cref="Services.Validation.ISchemaValidator"/>. Editor UI groups
    /// by severity (Critical / Warning / Info) and surfaces the field path
    /// so editors can fix the exact node that tripped the rule.
    /// </summary>
    public List<ValidationIssueDto> Issues { get; set; } = [];
}
