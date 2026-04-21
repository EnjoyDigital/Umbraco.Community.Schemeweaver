using System.Text.Json;

namespace Umbraco.Community.SchemeWeaver.Services.Validation.Rules;

/// <summary>
/// Google Rich Results rule for Book.
///
/// Rules from <see href="https://developers.google.com/search/docs/appearance/structured-data/book"/>.
/// Google's Book action requires <c>name</c> and <c>author</c>; work editions
/// (<c>workExample</c>) carry the format / ISBN / url that drive the book
/// action links. <c>sameAs</c> is recommended for authority disambiguation.
/// </summary>
public sealed class BookRule : ITypeRule
{
    private static readonly HashSet<string> Matches = new(StringComparer.OrdinalIgnoreCase)
    {
        "Book",
    };

    public bool AppliesTo(string schemaType) => Matches.Contains(schemaType);

    public IEnumerable<ValidationIssue> Check(JsonElement node, string path)
    {
        var type = node.GetProperty("@type").GetString() ?? "Book";

        if (!RuleHelpers.HasNonEmptyString(node, "Name"))
            yield return new ValidationIssue(ValidationSeverity.Critical, type,
                $"{path}.name",
                "Missing `name` — Google requires it to display the book title in rich results.");

        if (!RuleHelpers.HasNonEmptyArrayOrObject(node, "Author")
            && !RuleHelpers.HasNonEmptyString(node, "Author"))
            yield return new ValidationIssue(ValidationSeverity.Critical, type,
                $"{path}.author",
                "Missing `author` — Google requires at least one Person or Organization author for Book rich results.");

        if (!RuleHelpers.HasNonEmptyArrayOrObject(node, "WorkExample"))
            yield return new ValidationIssue(ValidationSeverity.Warning, type,
                $"{path}.workExample",
                "Missing `workExample` — recommended (array of Book editions carrying `bookFormat`, `isbn` and `potentialAction`). Required for the Book action link experience.");

        if (!RuleHelpers.HasNonEmptyString(node, "BookFormat"))
            yield return new ValidationIssue(ValidationSeverity.Warning, type,
                $"{path}.bookFormat",
                "Missing `bookFormat` — recommended (e.g. `https://schema.org/EBook`, `Hardcover`, `Paperback`).");

        if (!RuleHelpers.HasNonEmptyString(node, "Isbn"))
            yield return new ValidationIssue(ValidationSeverity.Warning, type,
                $"{path}.isbn",
                "Missing `isbn` — recommended (ISBN-13 preferred) for edition-level identification.");

        if (!RuleHelpers.HasNonEmptyArrayOrObject(node, "SameAs")
            && !RuleHelpers.HasUri(node, "SameAs"))
            yield return new ValidationIssue(ValidationSeverity.Warning, type,
                $"{path}.sameAs",
                "Missing `sameAs` — recommended (authoritative references like Wikipedia / Wikidata / publisher catalogue URLs).");

        if (!RuleHelpers.HasUri(node, "Url"))
            yield return new ValidationIssue(ValidationSeverity.Warning, type,
                $"{path}.url",
                "Missing `url` — recommended (absolute canonical URL to the book page).");
    }
}
