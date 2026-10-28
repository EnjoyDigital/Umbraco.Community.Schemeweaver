namespace Umbraco.Community.SchemeWeaver.Seo.SiteIdentity;

/// <summary>Persists the single site identity record.</summary>
public interface ISiteIdentityStore
{
    /// <summary>The stored identity, or null when none has been saved.</summary>
    SiteIdentityDto? Get();

    /// <summary>Validates and stores <paramref name="identity"/>. Returns validation messages; empty means saved.</summary>
    IReadOnlyList<string> Save(SiteIdentityDto identity, Guid? userKey);

    void Delete();
}
