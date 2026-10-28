using Microsoft.Extensions.Logging;
using Schema.NET;
using Umbraco.Cms.Core.Routing;
using Umbraco.Cms.Core.Web;
using Umbraco.Community.SchemeWeaver.Graph;
using Umbraco.Community.SchemeWeaver.Services.Media;

namespace Umbraco.Community.SchemeWeaver.Seo.SiteIdentity;

/// <summary>
/// Feeds the stored backoffice identity into the core's Organization / WebSite pieces through
/// the <see cref="ISiteIdentityProvider"/> seam. Scoped: resolved once per graph request.
/// </summary>
public sealed class SeoSiteIdentityProvider : ISiteIdentityProvider
{
    private readonly ISiteIdentityStore _store;
    private readonly IUmbracoContextAccessor _umbracoContextAccessor;
    private readonly IPublishedUrlProvider _urlProvider;
    private readonly ILogger<SeoSiteIdentityProvider> _logger;

    public SeoSiteIdentityProvider(
        ISiteIdentityStore store,
        IUmbracoContextAccessor umbracoContextAccessor,
        IPublishedUrlProvider urlProvider,
        ILogger<SeoSiteIdentityProvider> logger)
    {
        _store = store;
        _umbracoContextAccessor = umbracoContextAccessor;
        _urlProvider = urlProvider;
        _logger = logger;
    }

    public Graph.SiteIdentity? Resolve()
    {
        var dto = _store.Get();
        if (dto is null || string.IsNullOrWhiteSpace(dto.Name))
            return null;

        return Build(dto);
    }

    internal Graph.SiteIdentity Build(SiteIdentityDto dto) => new()
    {
        Kind = string.Equals(dto.Kind, "person", StringComparison.OrdinalIgnoreCase) ? SiteIdentityKind.Person : SiteIdentityKind.Organisation,
        Name = dto.Name.Trim(),
        AlternateName = NullIfBlank(dto.AlternateName),
        Url = Uri.TryCreate(dto.Url, UriKind.Absolute, out var url) ? url : null,
        Logo = ResolveLogo(dto.LogoMediaKey),
        SameAs = dto.SameAs
            .Select(s => Uri.TryCreate(s?.Trim(), UriKind.Absolute, out var u) ? u : null)
            .Where(u => u is not null)
            .Select(u => u!)
            .ToList(),
        Telephone = NullIfBlank(dto.Telephone),
        Email = NullIfBlank(dto.Email),
        Description = NullIfBlank(dto.Description),
        BusinessType = NullIfBlank(dto.BusinessType),
        Address = dto.Address is { IsEmpty: false } a
            ? new PostalAddress
            {
                StreetAddress = NullIfBlank(a.StreetAddress),
                AddressLocality = NullIfBlank(a.AddressLocality),
                AddressRegion = NullIfBlank(a.AddressRegion),
                PostalCode = NullIfBlank(a.PostalCode),
                AddressCountry = NullIfBlank(a.AddressCountry),
            }
            : null,
        Geo = dto.Geo is { } g ? new GeoCoordinates { Latitude = g.Latitude, Longitude = g.Longitude } : null,
        OpeningHours = dto.OpeningHours.Select(ToSpecification).Where(s => s is not null).Select(s => s!).ToList(),
        PriceRange = NullIfBlank(dto.PriceRange),
        JobTitle = NullIfBlank(dto.JobTitle),
        SearchUrlTemplate = NullIfBlank(dto.SearchUrlTemplate),
        SearchQueryInputName = NullIfBlank(dto.SearchQueryInputName),
    };

    private ImageObject? ResolveLogo(Guid? mediaKey)
    {
        if (mediaKey is null || mediaKey == Guid.Empty)
            return null;

        try
        {
            if (!_umbracoContextAccessor.TryGetUmbracoContext(out var umbracoContext) || umbracoContext.Media is null)
                return null;

            var media = umbracoContext.Media.GetById(mediaKey.Value);
            return media is null ? null : MediaImageObjectFactory.Create(media, _urlProvider);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "SchemeWeaver SEO could not resolve the identity logo {MediaKey}", mediaKey);
            return null;
        }
    }

    private static OpeningHoursSpecification? ToSpecification(SiteIdentityOpeningHoursDto hours)
    {
        var days = hours.DayOfWeek
            .Select(d => Enum.TryParse<Schema.NET.DayOfWeek>(d, ignoreCase: true, out var day) ? day : (Schema.NET.DayOfWeek?)null)
            .Where(d => d is not null)
            .Select(d => d!.Value)
            .ToList();
        if (days.Count == 0)
            return null;

        var spec = new OpeningHoursSpecification { DayOfWeek = new OneOrMany<Schema.NET.DayOfWeek?>(days.Select(d => (Schema.NET.DayOfWeek?)d).ToList()) };
        if (TimeSpan.TryParse(hours.Opens, out var opens))
            spec.Opens = opens;
        if (TimeSpan.TryParse(hours.Closes, out var closes))
            spec.Closes = closes;
        return spec;
    }

    private static string? NullIfBlank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
