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

    /// <summary>Strips HTML tags and trims surrounding whitespace.</summary>
    public static string StripHtmlTags(string html)
    {
        return StripHtmlRegex().Replace(html, string.Empty).Trim();
    }

    [GeneratedRegex("<[^>]+>")]
    private static partial Regex StripHtmlRegex();
}
