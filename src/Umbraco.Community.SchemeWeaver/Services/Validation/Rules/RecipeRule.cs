using System.Text.Json;

namespace Umbraco.Community.SchemeWeaver.Services.Validation.Rules;

/// <summary>
/// Google Rich Results rule for Recipe.
///
/// Rules from <see href="https://developers.google.com/search/docs/appearance/structured-data/recipe"/>.
/// Google requires <c>name</c> and <c>image</c> for any recipe rich result; the
/// remaining fields (<c>author</c>, <c>datePublished</c>, <c>description</c>,
/// <c>recipeIngredient</c>, <c>recipeInstructions</c>, <c>recipeYield</c>,
/// times, <c>aggregateRating</c>) are strongly recommended and unlock the
/// richer guided-recipes / host-carousel experiences.
/// </summary>
public sealed class RecipeRule : ITypeRule
{
    private static readonly HashSet<string> Matches = new(StringComparer.OrdinalIgnoreCase)
    {
        "Recipe",
    };

    public bool AppliesTo(string schemaType) => Matches.Contains(schemaType);

    public IEnumerable<ValidationIssue> Check(JsonElement node, string path)
    {
        var type = node.GetProperty("@type").GetString() ?? "Recipe";

        if (!RuleHelpers.HasNonEmptyString(node, "Name"))
            yield return new ValidationIssue(ValidationSeverity.Critical, type,
                $"{path}.name",
                "Missing `name` — Google requires it to display the recipe title in rich results.");

        if (!RuleHelpers.HasImage(node))
            yield return new ValidationIssue(ValidationSeverity.Critical, type,
                $"{path}.image",
                "Missing `image` — Google requires at least one image for recipe rich results (recommended 16:9, 4:3 and 1:1 variants at ≥1200px wide).");

        if (!RuleHelpers.HasNonEmptyArrayOrObject(node, "Author")
            && !RuleHelpers.HasNonEmptyString(node, "Author"))
            yield return new ValidationIssue(ValidationSeverity.Warning, type,
                $"{path}.author",
                "Missing `author` — recommended (Person or Organization) for byline display and host-carousel eligibility.");

        if (!RuleHelpers.HasIsoDate(node, "DatePublished"))
            yield return new ValidationIssue(ValidationSeverity.Warning, type,
                $"{path}.datePublished",
                "Missing or non-ISO `datePublished` — recommended so Google can show freshness signals.");

        if (!RuleHelpers.HasNonEmptyString(node, "Description"))
            yield return new ValidationIssue(ValidationSeverity.Warning, type,
                $"{path}.description",
                "Missing `description` — recommended; Google uses it for snippet generation.");

        if (!RuleHelpers.HasNonEmptyArrayOrObject(node, "RecipeIngredient"))
            yield return new ValidationIssue(ValidationSeverity.Warning, type,
                $"{path}.recipeIngredient",
                "Missing `recipeIngredient` — recommended (array of ingredient strings with quantities).");

        if (!RuleHelpers.HasNonEmptyArrayOrObject(node, "RecipeInstructions"))
            yield return new ValidationIssue(ValidationSeverity.Warning, type,
                $"{path}.recipeInstructions",
                "Missing `recipeInstructions` — recommended (array of HowToStep or strings). Required for guided-recipe carousels.");

        if (!RuleHelpers.HasNonEmptyString(node, "RecipeYield")
            && !RuleHelpers.HasNonEmptyArrayOrObject(node, "RecipeYield"))
            yield return new ValidationIssue(ValidationSeverity.Warning, type,
                $"{path}.recipeYield",
                "Missing `recipeYield` — recommended (e.g. \"4 servings\") so Google can display portion information.");

        // totalTime OR (prepTime + cookTime) satisfies the timing recommendation.
        var hasTotal = RuleHelpers.HasNonEmptyString(node, "TotalTime");
        var hasPrep = RuleHelpers.HasNonEmptyString(node, "PrepTime");
        var hasCook = RuleHelpers.HasNonEmptyString(node, "CookTime");
        if (!hasTotal && !(hasPrep && hasCook))
            yield return new ValidationIssue(ValidationSeverity.Warning, type,
                $"{path}.totalTime",
                "Missing `totalTime` (or `prepTime` + `cookTime` pair) — recommended as an ISO 8601 duration (e.g. `PT30M`) so Google can show cook-time badges.");

        if (!RuleHelpers.HasNonEmptyArrayOrObject(node, "AggregateRating"))
            yield return new ValidationIssue(ValidationSeverity.Warning, type,
                $"{path}.aggregateRating",
                "Missing `aggregateRating` — recommended; required to show star ratings in recipe rich results.");
    }
}
