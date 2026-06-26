namespace Umbraco.Community.SchemeWeaver.Services;

/// <summary>
/// Classifies how a mapped content type can emit JSON-LD: as a routed page on
/// its own URL, or only when composed into a containing page's graph because it
/// is a block/element type. Surfaced on the mapping DTO so the backoffice can
/// warn editors that element-type mappings never emit on their own route.
/// </summary>
public interface IMappingReachabilityClassifier
{
    /// <summary>
    /// Returns one of:
    /// <list type="bullet">
    ///   <item><description><c>routed-page</c> — a routable document type.</description></item>
    ///   <item><description><c>composed-from-block</c> — an element/block type that only
    ///   emits when a containing page's block mapping routes it.</description></item>
    ///   <item><description><c>unknown</c> — no content type found for the alias.</description></item>
    /// </list>
    /// </summary>
    string Classify(string contentTypeAlias);
}
