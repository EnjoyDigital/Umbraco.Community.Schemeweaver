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
}
