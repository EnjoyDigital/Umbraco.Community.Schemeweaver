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
        var urlProperty = typeof(Event).GetProperty("Url");
        urlProperty.Should().NotBeNull();
        urlProperty!.PropertyType.Should().Be(typeof(OneOrMany<System.Uri>));

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

    #region Date properties (Values<int?, DateTime?, DateTimeOffset?>)

    [Fact]
    public void SetPropertyValue_DatePublished_SetsFromIsoStringWithOffset()
    {
        // Article.DatePublished is Values<int?, DateTime?, DateTimeOffset?>. The DateTimeResolver
        // emits an ISO 8601 ("o") string — previously dropped because no string→date operator exists.
        var article = new Article();
        SchemaPropertySetter.SetPropertyValue(article, "DatePublished", "2026-06-29T10:30:00.0000000+01:00");

        var jsonLd = article.ToString();
        jsonLd.Should().Contain("datePublished");
        jsonLd.Should().Contain("2026-06-29");
        jsonLd.Should().Contain("+01:00", "an explicit offset must be preserved as a DateTimeOffset");
    }

    [Fact]
    public void SetPropertyValue_DatePublished_SetsFromUtcZuluString()
    {
        var article = new Article();
        SchemaPropertySetter.SetPropertyValue(article, "DatePublished", "2026-06-29T10:30:00Z");

        var jsonLd = article.ToString();
        jsonLd.Should().Contain("2026-06-29");
    }

    [Fact]
    public void SetPropertyValue_DatePublished_SetsFromDateOnlyStringWithoutSpuriousOffset()
    {
        // A zone-less date (e.g. a formatDate transform result) must not gain a server-local offset.
        var article = new Article();
        SchemaPropertySetter.SetPropertyValue(article, "DatePublished", "2026-06-29");

        var jsonLd = article.ToString();
        jsonLd.Should().Contain("2026-06-29");
        jsonLd.Should().NotContain("2026-06-29T00:00:00+", "a date-only value must not introduce a timezone offset");
        jsonLd.Should().NotContain("2026-06-29T00:00:00-");
    }

    [Fact]
    public void SetPropertyValue_DateModified_SetsFromIsoString()
    {
        var article = new Article();
        SchemaPropertySetter.SetPropertyValue(article, "DateModified", "2026-06-29T10:30:00+00:00");

        var jsonLd = article.ToString();
        jsonLd.Should().Contain("dateModified");
        jsonLd.Should().Contain("2026-06-29");
    }

    [Fact]
    public void SetPropertyValue_EventStartDate_SetsFromIsoString()
    {
        var ev = new Event();
        SchemaPropertySetter.SetPropertyValue(ev, "StartDate", "2026-12-01T19:00:00+00:00");

        var jsonLd = ev.ToString();
        jsonLd.Should().Contain("startDate");
        jsonLd.Should().Contain("2026-12-01");
    }

    [Fact]
    public void SetPropertyValue_GarbageDateString_IsDroppedNotThrown()
    {
        // An unparseable string for a date-only property is simply not set (and must not throw).
        var article = new Article();
        var act = () => SchemaPropertySetter.SetPropertyValue(article, "DatePublished", "not a date");

        act.Should().NotThrow();
        article.ToString().Should().NotContain("datePublished");
    }

    #endregion

    #region ImageObject media values (range-aware set + Uri downgrade)

    [Fact]
    public void SetPropertyValue_ImageObject_SetsArticleImage_AsImageObject()
    {
        // Article.Image is OneOrMany<Values<IImageObject, Uri>> — an ImageObject value must be
        // stored as a nested ImageObject (not downgraded), because the target accepts IImageObject.
        var article = new Article();
        var image = new ImageObject { Url = new Uri("https://example.com/photo.jpg") };

        SchemaPropertySetter.SetPropertyValue(article, "Image", image);

        var jsonLd = article.ToString();
        jsonLd.Should().Contain("ImageObject",
            "an ImageObject set into an IImageObject-accepting target must remain an ImageObject");
        jsonLd.Should().Contain("https://example.com/photo.jpg");
    }

    [Fact]
    public void SetPropertyValue_ListOfImageObjects_SetsArticleImage_AsArray()
    {
        // A collection of ImageObjects into Article.Image must produce an array of ImageObjects.
        var article = new Article();
        var images = new List<ImageObject>
        {
            new() { Url = new Uri("https://example.com/one.jpg") },
            new() { Url = new Uri("https://example.com/two.jpg") },
        };

        SchemaPropertySetter.SetPropertyValue(article, "Image", images);

        var jsonLd = article.ToString();
        jsonLd.Should().Contain("ImageObject");
        jsonLd.Should().Contain("https://example.com/one.jpg");
        jsonLd.Should().Contain("https://example.com/two.jpg");
    }

    [Fact]
    public void SetPropertyValue_ImageObject_ToUriOnlyTarget_DowngradesToUrl()
    {
        // Organization.Url is OneOrMany<Uri> — accepts a Uri leaf but NOT IImageObject.
        // An ImageObject value must be downgraded to its bare URL, not dropped or nested.
        var org = new Organization();
        var image = new ImageObject { Url = new Uri("https://example.com/logo.png") };

        SchemaPropertySetter.SetPropertyValue(org, "Url", image);

        var jsonLd = org.ToString();
        jsonLd.Should().Contain("https://example.com/logo.png", "the bare URL must be stored");
        jsonLd.Should().NotContain("ImageObject",
            "a Uri-only target must receive the bare URL, not a nested ImageObject");
    }

    [Fact]
    public void SetPropertyValue_ListOfUrlStrings_SetsOnOrganizationSameAs()
    {
        // Organization.SameAs is OneOrMany<Uri>. A MultiUrlPicker resolving several
        // profile URLs arrives as List<string>; there is no string→Uri implicit operator,
        // so the string-collection path must parse each URL into a Uri — every URL must
        // land in the output, not be silently dropped.
        var org = new Organization();
        var urls = new List<string> { "https://twitter.com/acme", "https://facebook.com/acme" };

        SchemaPropertySetter.SetPropertyValue(org, "SameAs", urls);

        var jsonLd = org.ToString();
        jsonLd.Should().Contain("sameAs");
        jsonLd.Should().Contain("https://twitter.com/acme");
        jsonLd.Should().Contain("https://facebook.com/acme");
    }

    [Fact]
    public void SetPropertyValue_StringList_OnKeywords_SetsAllValues()
    {
        // The MNTP resolver emits List<string> for multiple picked-node names —
        // the string-collection path must carry every value.
        var article = new Article();
        var names = new List<string> { "Alpha", "Beta", "Gamma" };

        SchemaPropertySetter.SetPropertyValue(article, "Keywords", names);

        var jsonLd = article.ToString();
        jsonLd.Should().Contain("Alpha").And.Contain("Beta").And.Contain("Gamma");
    }

    [Fact]
    public void SetPropertyValue_HeterogeneousObjectList_Behaviour_Pin()
    {
        // Behaviour pin for WHY MultiNodeTreePickerResolver homogenises its output:
        // a List<object> bypasses the typed IEnumerable<Thing>/IEnumerable<string>
        // fast paths (no generic variance from object) and only lands — partially,
        // and shape-dependently — via the late Values<> reflection path. The typed
        // homogenised lists are the only DETERMINISTIC contract; if this pin's
        // observed behaviour changes, revisit the resolver's homogenisation.
        var article = new Article();
        var mixed = new List<object> { new Person { Name = "Jane" }, "loose string" };

        var act = () => SchemaPropertySetter.SetPropertyValue(article, "Author", mixed);

        act.Should().NotThrow();
        // The typed path is the guaranteed one:
        var typed = new Article();
        SchemaPropertySetter.SetPropertyValue(typed, "Author", new List<Thing> { new Person { Name = "Jane" } });
        typed.ToString().Should().Contain("Jane");
    }

    [Fact]
    public void SetPropertyValue_MultipleUrlStrings_OnLogo_FallsBackToFirstUrl()
    {
        // Organization.Logo is OneOrMany<Values<IImageObject, Uri>> — plain strings cannot
        // be converted into Values<IImageObject, Uri> items, so the whole-collection path
        // (TrySetStringCollectionValue) fails. A MultiUrlPicker resolving SEVERAL links must
        // then fall back to the FIRST url (the pre-multi-link single-string behaviour) rather
        // than silently dropping the value entirely. This pins the first-string fallback in
        // SetPropertyValue: without it this logo would vanish from the JSON-LD.
        var org = new Organization();
        var urls = new List<string> { "https://example.com/logo.png", "https://example.com/logo-alt.png" };

        SchemaPropertySetter.SetPropertyValue(org, "Logo", urls);

        var jsonLd = org.ToString();
        jsonLd.Should().Contain("logo", "the first URL must survive via the first-string fallback");
        jsonLd.Should().Contain("https://example.com/logo.png");
        jsonLd.Should().NotContain("logo-alt.png",
            "only the first link can be represented when the collection cannot be set as a whole");
    }

    [Fact]
    public void SetPropertyValue_ImageObject_ToLogo_SetsImageObject()
    {
        // Organization.Logo is OneOrMany<Values<IImageObject, Uri>> — accepts IImageObject, so
        // an ImageObject must be kept as an ImageObject rather than downgraded.
        var org = new Organization();
        var image = new ImageObject { Url = new Uri("https://example.com/brand-logo.svg") };

        SchemaPropertySetter.SetPropertyValue(org, "Logo", image);

        var jsonLd = org.ToString();
        jsonLd.Should().Contain("ImageObject");
        jsonLd.Should().Contain("https://example.com/brand-logo.svg");
    }

    #endregion

    #region Reference shells (cross-piece @id links)

    [Fact]
    public void CreateReferenceShell_Publisher_ReturnsTypedOrganizationCarryingId()
    {
        // Article.publisher accepts an Organization, not a bare Thing. A generic
        // Thing shell would fail to bind and the publisher would silently vanish —
        // the reference must be typed as Organization.
        var article = new Article();
        var id = new Uri("https://example.com/#organization");

        var shell = SchemaPropertySetter.CreateReferenceShell(article, "publisher", id);

        shell.Should().BeOfType<Organization>();
        shell.Id.Should().Be(id);

        // …and it actually binds and serialises as a publisher @id reference.
        SchemaPropertySetter.SetPropertyValue(article, "publisher", shell);
        var jsonLd = article.ToString();
        jsonLd.Should().Contain("publisher");
        jsonLd.Should().Contain("https://example.com/#organization");
    }

    [Fact]
    public void CreateReferenceShell_ThingRangeProperty_ReturnsBareThing()
    {
        // mainEntity accepts IThing directly, so a bare Thing is correct and must
        // NOT be narrowed to a concrete subtype.
        var page = new WebPage();
        var id = new Uri("https://example.com/#organization");

        var shell = SchemaPropertySetter.CreateReferenceShell(page, "mainEntity", id);

        shell.GetType().Should().Be(typeof(Thing));
        shell.Id.Should().Be(id);
    }

    #endregion
}
