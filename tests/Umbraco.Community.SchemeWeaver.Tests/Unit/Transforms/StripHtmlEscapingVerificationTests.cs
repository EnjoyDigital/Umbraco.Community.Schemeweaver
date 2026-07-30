using FluentAssertions;
using Schema.NET;
using Umbraco.Community.SchemeWeaver.Services.Transforms;
using Xunit;

namespace Umbraco.Community.SchemeWeaver.Tests.Unit.Transforms;

/// <summary>
/// <see cref="SchemaValueTransformer.StripHtmlTags"/> HTML-DECODES entities, so text an editor
/// typed as "&lt;/script&gt;" — which Umbraco stores encoded as "&amp;lt;/script&amp;gt;" — comes
/// back out as a literal "&lt;/script&gt;" inside the mapped value. That is only safe because every
/// JSON-LD serialisation path re-escapes angle brackets before the JSON is written into a
/// &lt;script type="application/ld+json"&gt; block; the HTML parser ends a script element on a
/// literal "&lt;/script&gt;" whatever its type attribute says.
///
/// These tests pin the two halves of that guarantee: the decode really does produce raw angle
/// brackets, and Schema.NET's own ToString() — used on the non-graph path — does NOT escape them,
/// which is why JsonLdGenerator.SafeSerialize re-encodes. Without that re-encode this is a stored
/// XSS vector whenever UseGraphModel is false.
/// </summary>
public class StripHtmlEscapingVerificationTests
{
    private const string EditorTypedClosingTag = "&lt;/script&gt;&lt;script&gt;alert(1)&lt;/script&gt;";

    [Fact]
    public void StripHtmlTags_DecodesEntitiesIntoLiteralAngleBrackets()
    {
        var stripped = SchemaValueTransformer.StripHtmlTags(EditorTypedClosingTag);

        stripped.Should().Contain("</script>",
            "this is the premise the escaping guarantee has to cover");
    }

    [Fact]
    public void SchemaNetToString_DoesNotEscapeAngleBrackets_SoCallersMustReEncode()
    {
        var thing = new Article { Description = SchemaValueTransformer.StripHtmlTags(EditorTypedClosingTag) };

        thing.ToString().Should().Contain("</script>",
            "if Schema.NET ever starts escaping this, SafeSerialize's re-encode can be revisited");
    }

    [Fact]
    public void StripHtmlTags_DropsScriptAndStyleElementsWithTheirContents()
    {
        var stripped = SchemaValueTransformer.StripHtmlTags(
            "<p>Safe.</p><script>alert(1)</script><style>body{color:red}</style>");

        stripped.Should().Be("Safe.");
    }

    [Fact]
    public void StripHtmlTags_CollapsesBlockTagsToSpaces_ButNotInlineTags()
    {
        SchemaValueTransformer.StripHtmlTags("<p>One.</p><p>Two.</p>").Should().Be("One. Two.");
        SchemaValueTransformer.StripHtmlTags("Because <strong>schema</strong>.").Should().Be("Because schema.");
    }
}
