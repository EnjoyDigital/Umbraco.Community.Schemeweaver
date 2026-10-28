namespace Umbraco.Community.SchemeWeaver.Seo.SiteIdentity;

/// <summary>Editable site identity (Yoast's "Site representation"). Stored as JSON in the key-value store.</summary>
public class SiteIdentityDto
{
    /// <summary><c>organisation</c> or <c>person</c>.</summary>
    public string Kind { get; set; } = "organisation";
    public string Name { get; set; } = string.Empty;
    public string? AlternateName { get; set; }
    public string? Url { get; set; }
    public Guid? LogoMediaKey { get; set; }
    public List<string> SameAs { get; set; } = [];
    public string? Telephone { get; set; }
    public string? Email { get; set; }
    public string? Description { get; set; }

    /// <summary>LocalBusiness subtype name, or null for a plain Organization.</summary>
    public string? BusinessType { get; set; }
    public SiteIdentityAddressDto? Address { get; set; }
    public List<SiteIdentityOpeningHoursDto> OpeningHours { get; set; } = [];
    public SiteIdentityGeoDto? Geo { get; set; }
    public string? PriceRange { get; set; }

    /// <summary>Person only.</summary>
    public string? JobTitle { get; set; }

    public string? SearchUrlTemplate { get; set; }
    public string? SearchQueryInputName { get; set; }

    /// <summary>Output-only.</summary>
    public DateTime? UpdatedDate { get; set; }

    /// <summary>Output-only.</summary>
    public Guid? UpdatedByUserKey { get; set; }

    /// <summary>Output-only: true when a saved identity with a name exists.</summary>
    public bool IsConfigured { get; set; }
}

public class SiteIdentityAddressDto
{
    public string? StreetAddress { get; set; }
    public string? AddressLocality { get; set; }
    public string? AddressRegion { get; set; }
    public string? PostalCode { get; set; }
    public string? AddressCountry { get; set; }

    public bool IsEmpty =>
        string.IsNullOrWhiteSpace(StreetAddress) && string.IsNullOrWhiteSpace(AddressLocality)
        && string.IsNullOrWhiteSpace(AddressRegion) && string.IsNullOrWhiteSpace(PostalCode)
        && string.IsNullOrWhiteSpace(AddressCountry);
}

public class SiteIdentityOpeningHoursDto
{
    /// <summary>Schema.org day names: Monday … Sunday, or PublicHolidays.</summary>
    public List<string> DayOfWeek { get; set; } = [];

    /// <summary>24-hour time, e.g. <c>09:00</c>.</summary>
    public string? Opens { get; set; }
    public string? Closes { get; set; }
}

public class SiteIdentityGeoDto
{
    public double Latitude { get; set; }
    public double Longitude { get; set; }
}
