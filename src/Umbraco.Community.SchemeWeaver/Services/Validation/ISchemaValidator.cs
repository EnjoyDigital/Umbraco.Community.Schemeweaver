namespace Umbraco.Community.SchemeWeaver.Services.Validation;

/// <summary>
/// Validates emitted JSON-LD against Google's Rich Results rules.
/// Rules are codified per Schema.org type in the <c>Rules/</c> folder; the
/// validator walks the document and dispatches each node to every applicable
/// <see cref="Rules.ITypeRule"/>.
/// </summary>
public interface ISchemaValidator
{
    /// <summary>
    /// Validate a JSON-LD document. Accepts both a single Thing
    /// (<c>{"@context":..., "@type":"Article", ...}</c>) and a graph envelope
    /// (<c>{"@context":..., "@graph":[...]}</c>).
    /// </summary>
    ValidationResult Validate(string jsonLd);
}
