using System.Text.Json;

namespace Umbraco.Community.SchemeWeaver.Services.Validation.Rules;

/// <summary>
/// Google Rich Results rule for Movie.
///
/// Rules from <see href="https://developers.google.com/search/docs/appearance/structured-data/movie"/>.
/// Google requires <c>name</c> and <c>image</c>; director, release date,
/// ratings, reviews and genre round out the movie carousel experience.
/// </summary>
public sealed class MovieRule : ITypeRule
{
    private static readonly HashSet<string> Matches = new(StringComparer.OrdinalIgnoreCase)
    {
        "Movie",
    };

    public bool AppliesTo(string schemaType) => Matches.Contains(schemaType);

    public IEnumerable<ValidationIssue> Check(JsonElement node, string path)
    {
        var type = node.GetProperty("@type").GetString() ?? "Movie";

        if (!RuleHelpers.HasNonEmptyString(node, "Name"))
            yield return new ValidationIssue(ValidationSeverity.Critical, type,
                $"{path}.name",
                "Missing `name` — Google requires it to display the movie title in rich results.");

        if (!RuleHelpers.HasImage(node))
            yield return new ValidationIssue(ValidationSeverity.Critical, type,
                $"{path}.image",
                "Missing `image` — Google requires at least one image (poster) for movie rich results.");

        if (!RuleHelpers.HasUri(node, "Url"))
            yield return new ValidationIssue(ValidationSeverity.Warning, type,
                $"{path}.url",
                "Missing `url` — recommended (absolute canonical URL to the movie page).");

        if (!RuleHelpers.HasNonEmptyArrayOrObject(node, "Director")
            && !RuleHelpers.HasNonEmptyString(node, "Director"))
            yield return new ValidationIssue(ValidationSeverity.Warning, type,
                $"{path}.director",
                "Missing `director` — recommended (Person or array of Persons).");

        if (!RuleHelpers.HasIsoDate(node, "DateCreated"))
            yield return new ValidationIssue(ValidationSeverity.Warning, type,
                $"{path}.dateCreated",
                "Missing or non-ISO `dateCreated` — recommended (movie release date) so Google can show year / sort carousels.");

        if (!RuleHelpers.HasNonEmptyArrayOrObject(node, "AggregateRating"))
            yield return new ValidationIssue(ValidationSeverity.Warning, type,
                $"{path}.aggregateRating",
                "Missing `aggregateRating` — recommended; required to show star ratings in movie rich results.");

        if (!RuleHelpers.HasNonEmptyArrayOrObject(node, "Review"))
            yield return new ValidationIssue(ValidationSeverity.Warning, type,
                $"{path}.review",
                "Missing `review` — recommended (array of Review) for critic-review surfacing.");

        if (!RuleHelpers.HasNonEmptyArrayOrObject(node, "Genre")
            && !RuleHelpers.HasNonEmptyString(node, "Genre"))
            yield return new ValidationIssue(ValidationSeverity.Warning, type,
                $"{path}.genre",
                "Missing `genre` — recommended (string or array) so Google can group the movie by category.");
    }
}
