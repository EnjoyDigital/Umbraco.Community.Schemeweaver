using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Umbraco.Community.SchemeWeaver.Services;

/// <summary>
/// Default <see cref="ISiteOriginResolver"/>. Normalises
/// <see cref="SchemeWeaverOptions.PublicSiteUrl"/> ONCE at construction — an
/// invalid value, a non-http(s) scheme, or a stray path/query/fragment is a
/// configuration mistake surfaced as a warning, never an exception, so a typo
/// in appsettings degrades to the historical request-derived behaviour instead
/// of taking structured data down with it.
/// </summary>
public sealed class SiteOriginResolver : ISiteOriginResolver
{
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly Uri? _publicOrigin;

    public SiteOriginResolver(
        IHttpContextAccessor httpContextAccessor,
        IOptions<SchemeWeaverOptions> options,
        ILogger<SiteOriginResolver> logger)
    {
        _httpContextAccessor = httpContextAccessor;
        _publicOrigin = NormalisePublicOrigin(options.Value.PublicSiteUrl, logger);
    }

    public Uri? ResolveOrigin()
    {
        if (_publicOrigin is not null)
            return _publicOrigin;

        return RequestOrigin() is { } origin
               && Uri.TryCreate(origin, UriKind.Absolute, out var uri)
            ? uri
            : null;
    }

    public string RebaseToPublicOrigin(string json)
    {
        if (_publicOrigin is null || string.IsNullOrEmpty(json))
            return json;

        var requestOrigin = RequestOrigin();
        if (requestOrigin is null)
            return json;

        var publicOrigin = _publicOrigin.GetLeftPart(UriPartial.Authority);
        if (string.Equals(requestOrigin, publicOrigin, StringComparison.OrdinalIgnoreCase))
            return json;

        // Boundary-guarded: the origin must be followed by a path, query,
        // fragment, an escape, or the closing quote of the JSON string — so
        // "https://cms.example.com" never matches inside
        // "https://cms.example.com.evil.net". URLs serialise as plain ASCII in
        // both writer paths (neither encoder escapes ':' or '/'), so a literal
        // string match against the serialised JSON is exact.
        return Regex.Replace(
            json,
            Regex.Escape(requestOrigin) + @"(?=[/""?#\\]|$)",
            publicOrigin,
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }

    private string? RequestOrigin()
    {
        var request = _httpContextAccessor.HttpContext?.Request;
        return request is null ? null : $"{request.Scheme}://{request.Host}";
    }

    private static Uri? NormalisePublicOrigin(string? configured, ILogger logger)
    {
        if (string.IsNullOrWhiteSpace(configured))
            return null;

        var trimmed = configured.Trim();
        if (!Uri.TryCreate(trimmed, UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            logger.LogWarning(
                "SchemeWeaver:PublicSiteUrl {PublicSiteUrl} is not an absolute http(s) URL — "
                + "ignoring it; JSON-LD URLs will derive from the request host",
                configured);
            return null;
        }

        var origin = new Uri(uri.GetLeftPart(UriPartial.Authority));
        if (uri.PathAndQuery != "/" || !string.IsNullOrEmpty(uri.Fragment))
        {
            logger.LogWarning(
                "SchemeWeaver:PublicSiteUrl {PublicSiteUrl} carries a path/query/fragment — "
                + "it is an origin, not a base path; using {Origin}",
                configured, origin);
        }

        return origin;
    }
}
