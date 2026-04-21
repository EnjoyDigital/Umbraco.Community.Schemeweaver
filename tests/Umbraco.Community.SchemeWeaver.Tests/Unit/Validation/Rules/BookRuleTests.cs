using System.Text.Json;
using FluentAssertions;
using Umbraco.Community.SchemeWeaver.Services.Validation;
using Umbraco.Community.SchemeWeaver.Services.Validation.Rules;
using Xunit;

namespace Umbraco.Community.SchemeWeaver.Tests.Unit.Validation.Rules;

public class BookRuleTests
{
    private readonly BookRule _sut = new();

    private const string FullyPopulatedJson = """
        {
          "@type": "Book",
          "name": "Umbraco in Action",
          "author": { "@type": "Person", "name": "O. Picton" },
          "workExample": [
            {
              "@type": "Book",
              "isbn": "9781234567890",
              "bookFormat": "https://schema.org/Paperback",
              "potentialAction": { "@type": "ReadAction", "target": "https://example.com/read" }
            }
          ],
          "bookFormat": "https://schema.org/Paperback",
          "isbn": "9781234567890",
          "sameAs": ["https://www.wikidata.org/wiki/Q000"],
          "url": "https://example.com/books/umbraco-in-action"
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
        var json = FullyPopulatedJson.Replace("\"name\": \"Umbraco in Action\",", string.Empty);

        var issues = _sut.Check(Parse(json), "$").ToList();

        issues.Should().ContainSingle(i => i.Severity == ValidationSeverity.Critical && i.Path == "$.name");
    }

    [Fact]
    public void Check_MissingAuthor_YieldsCritical()
    {
        var json = FullyPopulatedJson.Replace(
            "\"author\": { \"@type\": \"Person\", \"name\": \"O. Picton\" },",
            string.Empty);

        var issues = _sut.Check(Parse(json), "$").ToList();

        issues.Should().ContainSingle(i => i.Severity == ValidationSeverity.Critical && i.Path == "$.author");
    }

    [Fact]
    public void Check_MissingIsbn_YieldsWarning()
    {
        const string json = """
            {
              "@type": "Book",
              "name": "A Book",
              "author": { "@type": "Person", "name": "Someone" },
              "workExample": [ { "@type": "Book", "bookFormat": "https://schema.org/EBook" } ],
              "bookFormat": "https://schema.org/EBook",
              "sameAs": ["https://www.wikidata.org/wiki/Q000"],
              "url": "https://example.com/books/a-book"
            }
            """;

        var issues = _sut.Check(Parse(json), "$").ToList();

        issues.Should().ContainSingle(i => i.Path == "$.isbn")
            .Which.Severity.Should().Be(ValidationSeverity.Warning);
    }

    [Fact]
    public void Check_MissingWorkExample_YieldsWarning()
    {
        var json = FullyPopulatedJson.Replace(
            "\"workExample\": [\n    {\n      \"@type\": \"Book\",\n      \"isbn\": \"9781234567890\",\n      \"bookFormat\": \"https://schema.org/Paperback\",\n      \"potentialAction\": { \"@type\": \"ReadAction\", \"target\": \"https://example.com/read\" }\n    }\n  ],",
            string.Empty);

        var issues = _sut.Check(Parse(json), "$").ToList();

        issues.Should().ContainSingle(i => i.Path == "$.workExample")
            .Which.Severity.Should().Be(ValidationSeverity.Warning);
    }

    [Fact]
    public void AppliesTo_Article_IsFalse()
    {
        _sut.AppliesTo("Article").Should().BeFalse();
    }

    [Fact]
    public void AppliesTo_Book_IsTrue()
    {
        _sut.AppliesTo("Book").Should().BeTrue();
    }
}
