using Schema.NET;

namespace Umbraco.Community.SchemeWeaver.Graph;

/// <summary>
/// Whether the site is represented by an organisation or by a person (Yoast's
/// "Site representation" choice).
/// </summary>
public enum SiteIdentityKind
{
    Organisation = 0,
    Person = 1,
}

/// <summary>
/// A render-ready description of who the site represents. Supplied per request by an
/// <see cref="ISiteIdentityProvider"/> (the SchemeWeaver SEO satellite stores one in the
/// backoffice; developers can register their own) and consumed by the built-in Organization
/// and WebSite graph pieces in preference to the <c>schemaSiteSettings</c> content-node
/// convention. Media is already resolved to an <see cref="ImageObject"/> so pieces stay free
/// of media dependencies. Invariant: one identity per site.
/// </summary>
public sealed class SiteIdentity
{
    public SiteIdentityKind Kind { get; init; } = SiteIdentityKind.Organisation;

    /// <summary>Organisation or person name. Required; an identity without a name is ignored.</summary>
    public required string Name { get; init; }

    public string? AlternateName { get; init; }

    /// <summary>Public site URL. Falls back to the resolved site origin when null.</summary>
    public Uri? Url { get; init; }

    /// <summary>Organisation logo, or the person's image.</summary>
    public ImageObject? Logo { get; init; }

    /// <summary>Social / profile URLs emitted as <c>sameAs</c>.</summary>
    public IReadOnlyList<Uri> SameAs { get; init; } = [];

    public string? Telephone { get; init; }

    public string? Email { get; init; }

    public string? Description { get; init; }

    /// <summary>
    /// Optional LocalBusiness upgrade (organisations only): a Schema.org type name that must be
    /// <c>LocalBusiness</c> or one of its subtypes. Null emits a plain <c>Organization</c>.
    /// </summary>
    public string? BusinessType { get; init; }

    public PostalAddress? Address { get; init; }

    public GeoCoordinates? Geo { get; init; }

    public IReadOnlyList<OpeningHoursSpecification> OpeningHours { get; init; } = [];

    public string? PriceRange { get; init; }

    /// <summary>Person only.</summary>
    public string? JobTitle { get; init; }

    /// <summary>
    /// Site search URL template containing the query placeholder (e.g.
    /// <c>https://example.com/search?q={search_term_string}</c>). When set it takes precedence
    /// over <c>SchemeWeaver:SiteSearch:UrlTemplate</c>.
    /// </summary>
    public string? SearchUrlTemplate { get; init; }

    /// <summary>Placeholder name used inside <see cref="SearchUrlTemplate"/>; defaults to <c>search_term_string</c>.</summary>
    public string? SearchQueryInputName { get; init; }
}
