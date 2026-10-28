namespace Umbraco.Community.SchemeWeaver.Seo.Overrides;

/// <summary>Per-page override persistence. Reads are served from an in-memory snapshot (hot path).</summary>
public interface ISeoPageOverrideRepository
{
    SeoPageOverride? Get(Guid contentKey);

    IReadOnlyCollection<SeoPageOverride> GetAll();

    SeoPageOverride Upsert(SeoPageOverride @override);

    void Delete(Guid contentKey);

    void DeleteMany(IEnumerable<Guid> contentKeys);

    void ClearCache();
}
