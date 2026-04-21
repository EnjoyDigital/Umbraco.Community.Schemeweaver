using System.Text.Json;
using FluentAssertions;
using Umbraco.Community.SchemeWeaver.Services.Validation;
using Umbraco.Community.SchemeWeaver.Services.Validation.Rules;
using Xunit;

namespace Umbraco.Community.SchemeWeaver.Tests.Unit.Validation.Rules;

public class RecipeRuleTests
{
    private readonly RecipeRule _sut = new();

    private const string FullyPopulatedJson = """
        {
          "@type": "Recipe",
          "name": "Classic Victoria Sponge",
          "image": "https://example.com/images/sponge.jpg",
          "author": { "@type": "Person", "name": "Mary Berry" },
          "datePublished": "2026-03-01T09:00:00Z",
          "description": "A light, buttery sponge filled with jam and cream.",
          "recipeIngredient": ["225g butter", "225g caster sugar", "4 eggs"],
          "recipeInstructions": [
            { "@type": "HowToStep", "text": "Preheat oven to 180C." },
            { "@type": "HowToStep", "text": "Cream butter and sugar." }
          ],
          "recipeYield": "8 servings",
          "totalTime": "PT45M",
          "aggregateRating": { "@type": "AggregateRating", "ratingValue": "4.8", "reviewCount": "120" }
        }
        """;

    private static JsonElement Parse(string json) => JsonDocument.Parse(json).RootElement;

    [Fact]
    public void Check_FullyPopulated_YieldsNoIssues()
    {
        var issues = _sut.Check(Parse(FullyPopulatedJson), "$").ToList();
        issues.Should().BeEmpty();
    }

    [Fact]
    public void Check_MissingName_YieldsCritical()
    {
        const string json = """
            {
              "@type": "Recipe",
              "image": "https://example.com/img.jpg"
            }
            """;

        var issues = _sut.Check(Parse(json), "$").ToList();

        issues.Should().ContainSingle(i => i.Severity == ValidationSeverity.Critical && i.Path == "$.name");
    }

    [Fact]
    public void Check_MissingImage_YieldsCritical()
    {
        const string json = """
            {
              "@type": "Recipe",
              "name": "Loaf"
            }
            """;

        var issues = _sut.Check(Parse(json), "$").ToList();

        issues.Should().Contain(i => i.Severity == ValidationSeverity.Critical && i.Path == "$.image");
    }

    [Fact]
    public void Check_MissingRecipeIngredient_YieldsWarning()
    {
        var json = FullyPopulatedJson.Replace("\"recipeIngredient\": [\"225g butter\", \"225g caster sugar\", \"4 eggs\"],", string.Empty);

        var issues = _sut.Check(Parse(json), "$").ToList();

        issues.Should().ContainSingle(i => i.Path == "$.recipeIngredient")
            .Which.Severity.Should().Be(ValidationSeverity.Warning);
    }

    [Fact]
    public void Check_MissingTotalTimeButHasPrepAndCook_NoTimingWarning()
    {
        const string json = """
            {
              "@type": "Recipe",
              "name": "Bread",
              "image": "https://example.com/img.jpg",
              "prepTime": "PT15M",
              "cookTime": "PT30M"
            }
            """;

        var issues = _sut.Check(Parse(json), "$").ToList();

        issues.Should().NotContain(i => i.Path == "$.totalTime");
    }

    [Fact]
    public void Check_MissingAggregateRating_YieldsWarning()
    {
        var json = FullyPopulatedJson.Replace(
            ",\n  \"aggregateRating\": { \"@type\": \"AggregateRating\", \"ratingValue\": \"4.8\", \"reviewCount\": \"120\" }",
            string.Empty);

        var issues = _sut.Check(Parse(json), "$").ToList();

        issues.Should().ContainSingle(i => i.Path == "$.aggregateRating")
            .Which.Severity.Should().Be(ValidationSeverity.Warning);
    }

    [Fact]
    public void AppliesTo_Article_IsFalse()
    {
        _sut.AppliesTo("Article").Should().BeFalse();
    }

    [Fact]
    public void AppliesTo_Recipe_IsTrue()
    {
        _sut.AppliesTo("Recipe").Should().BeTrue();
    }
}
