using System.Net;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace Umbraco.Community.SchemeWeaver.Services.Transforms;

/// <summary>
/// Applies a named value transform (<c>stripHtml</c>, <c>toAbsoluteUrl</c>, <c>formatDate</c>)
/// to a resolved string value. Shared by the top-level <see cref="JsonLdGenerator"/> and the
/// nested-block <see cref="Resolvers.BlockContentResolver"/> so transforms behave identically
/// whether a property is mapped at the page level or inside a block.
/// </summary>
public static partial class SchemaValueTransformer
{
    /// <summary>
    /// Applies <paramref name="transformType"/> to <paramref name="value"/>. Returns the value
    /// unchanged when either is null/empty or the transform is unknown. <c>toAbsoluteUrl</c>
    /// needs the current request's base URL, supplied via <paramref name="httpContextAccessor"/>.
    /// </summary>
    public static string? Apply(
        string? value,
        string? transformType,
        IHttpContextAccessor? httpContextAccessor,
        ILogger? logger = null)
    {
        if (string.IsNullOrEmpty(value) || string.IsNullOrEmpty(transformType))
            return value;

        return transformType switch
        {
            "stripHtml" => StripHtmlTags(value),
            "toAbsoluteUrl" => ToAbsoluteUrl(value, httpContextAccessor, logger),
            "formatDate" => DateTime.TryParse(value, out var dt) ? dt.ToString("yyyy-MM-dd") : value,
            _ => value
        };
    }

    /// <summary>
    /// Resolves a relative URL to an absolute URL using the current request's base URL.
    /// Returns the original value when it is already absolute or no HttpContext is available.
    /// </summary>
    private static string ToAbsoluteUrl(string value, IHttpContextAccessor? httpContextAccessor, ILogger? logger)
    {
        if (!value.StartsWith('/'))
            return value;

        var request = httpContextAccessor?.HttpContext?.Request;
        if (request is null)
        {
            logger?.LogWarning("Cannot resolve absolute URL: no HttpContext available");
            return value;
        }

        return $"{request.Scheme}://{request.Host}{value}";
    }

    /// <summary>
    /// Reduces HTML to the plain text Schema.org string properties expect.
    ///
    /// Block-level tags collapse to a space and inline tags vanish, so
    /// <c>&lt;p&gt;One.&lt;/p&gt;&lt;p&gt;Two.&lt;/p&gt;</c> becomes "One. Two." rather than
    /// "One.Two.", while <c>Because &lt;strong&gt;schema&lt;/strong&gt;.</c> keeps its full stop
    /// tight against the word. <c>script</c>/<c>style</c> elements and comments are dropped
    /// with their contents (stripping only their tags would leak CSS/JS into the output).
    /// Entities are decoded — a mapped value must carry "Tom &amp; Jerry", not
    /// "Tom &amp;amp; Jerry" — which is safe because <see cref="Graph.GraphGenerator"/>
    /// serialises with an encoder that re-escapes &lt;, &gt;, &amp; and ' before the JSON
    /// reaches the <c>&lt;script type="application/ld+json"&gt;</c> block.
    /// Runs of whitespace (including the non-breaking spaces <c>&amp;nbsp;</c> decodes to)
    /// collapse to a single space, and the result is trimmed.
    /// </summary>
    public static string StripHtmlTags(string html)
    {
        if (string.IsNullOrWhiteSpace(html))
            return string.Empty;

        var text = ScriptOrStyleElementRegex().Replace(html, " ");
        text = CommentRegex().Replace(text, " ");
        text = BlockLevelTagRegex().Replace(text, " ");
        text = AnyTagRegex().Replace(text, string.Empty);
        text = WebUtility.HtmlDecode(text);

        return WhitespaceRegex().Replace(text, " ").Trim();
    }

    [GeneratedRegex(@"<(script|style)\b[^>]*>.*?</\1\s*>",
        RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex ScriptOrStyleElementRegex();

    [GeneratedRegex("<!--.*?-->", RegexOptions.Singleline)]
    private static partial Regex CommentRegex();

    /// <summary>
    /// Tags that imply a text boundary. Matched before <see cref="AnyTagRegex"/> so they can be
    /// replaced with a space while inline tags are replaced with nothing.
    /// </summary>
    [GeneratedRegex(
        @"</?(?:p|div|br|hr|li|ul|ol|dl|dt|dd|table|thead|tbody|tfoot|tr|td|th|caption"
        + @"|h[1-6]|section|article|aside|header|footer|nav|main|blockquote|pre"
        + @"|figure|figcaption|address|form|fieldset|legend|option|iframe)\b[^>]*>",
        RegexOptions.IgnoreCase)]
    private static partial Regex BlockLevelTagRegex();

    [GeneratedRegex("<[^>]+>")]
    private static partial Regex AnyTagRegex();

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespaceRegex();
}
