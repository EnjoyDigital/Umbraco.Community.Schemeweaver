namespace Umbraco.Community.SchemeWeaver.Services.Advisory;

/// <summary>
/// Derives non-blocking <c>suggestion</c>-severity advisories from a mapping plus the source
/// property's editor alias and persistence state — the proactive half of the "warn-on-drop"
/// pattern. Used both reactively (the service folds advisories into a mapping's <c>warnings[]</c>)
/// and proactively (the suggester pre-fills the matching fix). Never mutates a saved mapping.
/// </summary>
public interface IMappingAdvisor
{
    /// <summary>Per-property checks (1-3). Returns 0..n advisories for a single mapping entry.</summary>
    IReadOnlyList<MappingAdvice> AdviseEntry(MappingEntryInput entry);

    /// <summary>Persistence check (4). Returns one advisory at most, or null when nothing to suggest.</summary>
    MappingAdvice? AdvisePersistence(string schemaTypeName, PersistenceFacts facts);
}
