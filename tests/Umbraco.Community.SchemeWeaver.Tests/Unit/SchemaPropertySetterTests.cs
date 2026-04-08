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

    [Fact]
    public void SetPropertyValue_PersonName_SetsFromString()
    {
        // Person.Name is OneOrMany<Values<string>> — verify SetPropertyValue can set it
        var person = new Person();
        SchemaPropertySetter.SetPropertyValue(person, "Name", "Alice Smith");

        var jsonLd = person.ToString();
        jsonLd.Should().Contain("Alice Smith");
    }

    [Fact]
    public void SetPropertyValue_ReviewAuthorWithPerson_SetsPersonName()
    {
        // Full wrapping scenario: create Person, set Name, set on Review.Author
        var person = new Person();
        SchemaPropertySetter.SetPropertyValue(person, "Name", "Alice Smith");

        var review = new Review();
        SchemaPropertySetter.SetPropertyValue(review, "Author", person);

        var jsonLd = review.ToString();
        jsonLd.Should().Contain("Person");
        jsonLd.Should().Contain("Alice Smith");
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

    #region Scalar auto-wrapping (Brand, Author, Publisher, etc.)

    [Fact]
    public void SetPropertyValue_ProductBrand_WrapsStringIntoBrandObject()
    {
        // Product.Brand is OneOrMany<IBrand>/OneOrMany<Values<IBrand, IOrganization>> — a Thing
        // property. Users frequently map it from a plain Textbox in Umbraco, so we must wrap
        // the scalar string into `{ "@type": "Brand", "name": "AudioTech" }`.
        var product = new Product();
        SchemaPropertySetter.SetPropertyValue(product, "Brand", "AudioTech");

        var jsonLd = product.ToString();
        jsonLd.Should().Contain("AudioTech", "the brand name must appear in the JSON-LD");
        jsonLd.Should().Contain("Brand", "the wrapped Brand @type must appear");
    }

    [Fact]
    public void SetPropertyValue_ArticleAuthor_WrapsStringIntoPersonObject()
    {
        // Article.Author expects a Person or Organization. Mapping it from a Textbox
        // (e.g., author name) should wrap as { "@type": "Person", "name": "..." }.
        var article = new Article();
        SchemaPropertySetter.SetPropertyValue(article, "Author", "Jane Doe");

        var jsonLd = article.ToString();
        jsonLd.Should().Contain("Jane Doe");
        jsonLd.Should().Contain("Person");
    }

    [Fact]
    public void SetPropertyValue_ArticlePublisher_WrapsStringIntoOrganizationObject()
    {
        // Article.Publisher expects Person or Organization. Organization is more appropriate
        // for a publisher field mapped from a string.
        var article = new Article();
        SchemaPropertySetter.SetPropertyValue(article, "Publisher", "Acme Publishing");

        var jsonLd = article.ToString();
        jsonLd.Should().Contain("Acme Publishing");
        jsonLd.Should().Contain("Organization");
    }

    [Fact]
    public void SetPropertyValue_RecipeAuthor_WrapsStringIntoPersonObject()
    {
        var recipe = new Recipe();
        SchemaPropertySetter.SetPropertyValue(recipe, "Author", "Jamie Oliver");

        var jsonLd = recipe.ToString();
        jsonLd.Should().Contain("Jamie Oliver");
        jsonLd.Should().Contain("Person");
    }

    [Fact]
    public void SetPropertyValue_ProductBrand_StillAcceptsExplicitBrandObject()
    {
        // The auto-wrap must not break the existing path where the value is already a Thing.
        var product = new Product();
        var brand = new Brand { Name = "AudioTech", Url = new Uri("https://audiotech.example") };
        SchemaPropertySetter.SetPropertyValue(product, "Brand", brand);

        var jsonLd = product.ToString();
        jsonLd.Should().Contain("AudioTech");
        jsonLd.Should().Contain("https://audiotech.example");
    }

    #endregion
}
