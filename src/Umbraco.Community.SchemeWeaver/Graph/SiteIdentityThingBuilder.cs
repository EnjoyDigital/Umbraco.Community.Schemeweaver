using Schema.NET;
using Umbraco.Community.SchemeWeaver.Services;

namespace Umbraco.Community.SchemeWeaver.Graph;

/// <summary>
/// Turns a <see cref="SiteIdentity"/> into the publisher node of the graph: an
/// <c>Organization</c>, a <c>LocalBusiness</c> subtype, or a <c>Person</c>. The node keeps the
/// conventional <c>{siteUrl}#organization</c> @id whatever its type, so <c>reference</c>
/// property mappings targeting the <c>organization</c> piece and the WebSite's <c>publisher</c>
/// cross-reference keep working unchanged.
/// </summary>
internal static class SiteIdentityThingBuilder
{
    public static Thing? BuildPublisher(SiteIdentity identity, Uri? siteUrl, ISchemaTypeRegistry registry)
    {
        if (string.IsNullOrWhiteSpace(identity.Name))
            return null;

        Thing thing = identity.Kind == SiteIdentityKind.Person
            ? BuildPerson(identity)
            : BuildOrganisation(identity, registry);

        ApplyCommon(thing, identity, siteUrl);
        return thing;
    }

    private static Person BuildPerson(SiteIdentity identity)
    {
        var person = new Person { Name = identity.Name };
        if (identity.Logo is not null)
            person.Image = identity.Logo;
        if (!string.IsNullOrWhiteSpace(identity.JobTitle))
            person.JobTitle = identity.JobTitle;
        if (!string.IsNullOrWhiteSpace(identity.Telephone))
            person.Telephone = identity.Telephone;
        if (!string.IsNullOrWhiteSpace(identity.Email))
            person.Email = identity.Email;
        return person;
    }

    private static Thing BuildOrganisation(SiteIdentity identity, ISchemaTypeRegistry registry)
    {
        var organisation = CreateOrganisationInstance(identity.BusinessType, registry);
        organisation.Name = identity.Name;
        if (identity.Logo is not null)
            organisation.Logo = identity.Logo;
        if (!string.IsNullOrWhiteSpace(identity.Telephone))
            organisation.Telephone = identity.Telephone;
        if (!string.IsNullOrWhiteSpace(identity.Email))
            organisation.Email = identity.Email;
        if (identity.Address is not null)
            organisation.Address = identity.Address;

        if (organisation is IPlace place)
        {
            // LocalBusiness is both an Organization and a Place; Google's local-business
            // feature reads the Place half (address, geo, opening hours, image).
            if (identity.Geo is not null)
                place.Geo = identity.Geo;
            if (identity.OpeningHours.Count > 0)
                place.OpeningHoursSpecification = new OneOrMany<IOpeningHoursSpecification>(identity.OpeningHours.Cast<IOpeningHoursSpecification>().ToList());
            if (identity.Logo is not null)
                place.Image = identity.Logo;
        }

        if (organisation is ILocalBusiness localBusiness && !string.IsNullOrWhiteSpace(identity.PriceRange))
            localBusiness.PriceRange = identity.PriceRange;

        return (Thing)organisation;
    }

    /// <summary>
    /// Instantiates the requested business type when it resolves to an Organization subtype;
    /// anything else (unknown name, a non-Organization type) degrades to a plain Organization
    /// rather than failing the whole graph.
    /// </summary>
    private static IOrganization CreateOrganisationInstance(string? businessType, ISchemaTypeRegistry registry)
    {
        if (!string.IsNullOrWhiteSpace(businessType))
        {
            var clrType = registry.GetClrType(businessType);
            if (clrType is not null
                && typeof(IOrganization).IsAssignableFrom(clrType)
                && Activator.CreateInstance(clrType) is IOrganization typed)
            {
                return typed;
            }
        }

        return new Organization();
    }

    private static void ApplyCommon(Thing thing, SiteIdentity identity, Uri? siteUrl)
    {
        if (!string.IsNullOrWhiteSpace(identity.AlternateName))
            thing.AlternateName = identity.AlternateName;
        if (!string.IsNullOrWhiteSpace(identity.Description))
            thing.Description = identity.Description;

        var url = identity.Url ?? siteUrl;
        if (url is not null)
            thing.Url = url;

        if (identity.SameAs.Count > 0)
            thing.SameAs = identity.SameAs.ToList();

        if (siteUrl is not null)
            thing.Id = new Uri($"{siteUrl}#organization");
    }
}
