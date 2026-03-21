using FluentAssertions;
using Schema.NET;
using Umbraco.Community.SchemeWeaver.Services;
using Xunit;

namespace Umbraco.Community.SchemeWeaver.Tests.Unit;

public class SchemaPropertySetterTests
{
    [Fact]
    public void SetPropertyValue_OneOrManyUri_SetsFromString()
    {
        // Thing.Url is OneOrMany<Uri> — previously this was silently dropped
        var thing = new Event();
        SchemaPropertySetter.SetPropertyValue(thing, "Url", "https://example.com/event");

        var jsonLd = thing.ToString();
        jsonLd.Should().Contain("https://example.com/event");
    }

    [Fact]
    public void SetPropertyValue_OneOrManyValuesWithUri_SetsImageFromString()
    {
        // Article.Image is OneOrMany<Values<IImageObject, Uri>> — the Values<> path
        var article = new Article();
        SchemaPropertySetter.SetPropertyValue(article, "Image", "https://example.com/image.jpg");

        var jsonLd = article.ToString();
        jsonLd.Should().Contain("https://example.com/image.jpg");
    }

    [Fact]
    public void SetPropertyValue_StringProperty_SetsViaImplicit()
    {
        // Article.Headline is OneOrMany<Values<string>> effectively — handled via implicit
        var article = new Article();
        SchemaPropertySetter.SetPropertyValue(article, "Headline", "Test Headline");

        var jsonLd = article.ToString();
        jsonLd.Should().Contain("Test Headline");
    }

    [Fact]
    public void SetPropertyValue_ThingValue_SetsViaImplicit()
    {
        // Article.Author accepts Person via implicit conversion
        var article = new Article();
        var person = new Person { Name = "Jane Smith" };
        SchemaPropertySetter.SetPropertyValue(article, "Author", person);

        var jsonLd = article.ToString();
        jsonLd.Should().Contain("Jane Smith");
    }

    [Fact]
    public void SetPropertyValue_SameAs_SetsOneOrManyUri()
    {
        // Thing.SameAs is OneOrMany<Uri> — same pattern as Url
        var thing = new Organization();
        SchemaPropertySetter.SetPropertyValue(thing, "SameAs", "https://twitter.com/example");

        var jsonLd = thing.ToString();
        jsonLd.Should().Contain("https://twitter.com/example");
    }

    #region Collection (List<Thing>) tests

    [Fact]
    public void SetPropertyValue_ListOfQuestions_SetsOnFAQPageMainEntity()
    {
        // FAQPage.MainEntity is OneOrMany<Values<IQuestion, ICreativeWork>>
        var faq = new FAQPage();
        var q1 = new Question { Name = "What is X?" };
        q1.AcceptedAnswer = new Answer { Text = "X is Y" };
        var q2 = new Question { Name = "What is Z?" };
        q2.AcceptedAnswer = new Answer { Text = "Z is W" };

        var questions = new List<Thing> { q1, q2 };
        SchemaPropertySetter.SetPropertyValue(faq, "MainEntity", questions);

        var jsonLd = faq.ToString();
        jsonLd.Should().Contain("Question");
        jsonLd.Should().Contain("What is X?");
        jsonLd.Should().Contain("What is Z?");
        jsonLd.Should().Contain("Answer");
        jsonLd.Should().Contain("X is Y");
    }

    [Fact]
    public void SetPropertyValue_ListOfReviews_SetsOnProductReview()
    {
        // Product.Review is OneOrMany<Values<IReview>>
        var product = new Product();
        var r1 = new Review { Author = new Person { Name = "Alice" }, ReviewBody = "Great!" };
        var r2 = new Review { Author = new Person { Name = "Bob" }, ReviewBody = "Good" };

        var reviews = new List<Thing> { r1, r2 };
        SchemaPropertySetter.SetPropertyValue(product, "Review", reviews);

        var jsonLd = product.ToString();
        jsonLd.Should().Contain("Review");
        jsonLd.Should().Contain("Alice");
        jsonLd.Should().Contain("Bob");
    }

    [Fact]
    public void SetPropertyValue_ListOfHowToSteps_SetsOnRecipeInstructions()
    {
        // Recipe.RecipeInstructions is Values<ICreativeWork, IItemList, string> — accepts List<ICreativeWork>
        var recipe = new Recipe();
        var s1 = new HowToStep { Name = "Step 1", Text = "Mix ingredients" };
        var s2 = new HowToStep { Name = "Step 2", Text = "Bake" };

        var steps = new List<Thing> { s1, s2 };
        SchemaPropertySetter.SetPropertyValue(recipe, "RecipeInstructions", steps);

        var jsonLd = recipe.ToString();
        jsonLd.Should().Contain("Mix ingredients");
        jsonLd.Should().Contain("Bake");
    }

    [Fact]
    public void TryConvertViaImplicit_HowToStep_ConvertsToValuesType()
    {
        // Recipe.RecipeInstructions is Values<ICreativeWork, IItemList, string> (not wrapped in OneOrMany)
        var prop = typeof(Recipe).GetProperty("RecipeInstructions",
            System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance)!;
        var valuesType = prop.PropertyType;

        // HowToStep implements ICreativeWork — it should convert via op_Implicit(ICreativeWork)
        var step = new HowToStep { Name = "Test", Text = "Test text" };
        var converted = SchemaPropertySetter.TryConvertViaImplicit(valuesType, step);
        converted.Should().NotBeNull($"HowToStep should convert to {valuesType} via ICreativeWork implicit operator");
    }

    [Fact]
    public void SetPropertyValue_ListOfStrings_SetsOnRecipeIngredient()
    {
        // Recipe.RecipeIngredient is OneOrMany<Values<string>>
        var recipe = new Recipe();
        var ingredients = new List<string> { "200g flour", "100g sugar", "2 eggs" };

        SchemaPropertySetter.SetPropertyValue(recipe, "RecipeIngredient", ingredients);

        var jsonLd = recipe.ToString();
        jsonLd.Should().Contain("200g flour");
        jsonLd.Should().Contain("100g sugar");
        jsonLd.Should().Contain("2 eggs");
    }

    [Fact]
    public void SetPropertyValue_SingleQuestion_SetsOnFAQPageMainEntity()
    {
        // Single Thing should also work via implicit conversion
        var faq = new FAQPage();
        var q = new Question { Name = "Single Q?" };
        q.AcceptedAnswer = new Answer { Text = "Single A" };

        SchemaPropertySetter.SetPropertyValue(faq, "MainEntity", q);

        var jsonLd = faq.ToString();
        jsonLd.Should().Contain("Question");
        jsonLd.Should().Contain("Single Q?");
    }

    #endregion
}
