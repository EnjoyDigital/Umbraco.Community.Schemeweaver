using Umbraco.Cms.Core.Services;

namespace Umbraco.Community.SchemeWeaver.Services;

/// <inheritdoc />
public class MappingReachabilityClassifier : IMappingReachabilityClassifier
{
    /// <summary>Reachability code for a routable document type.</summary>
    public const string RoutedPage = "routed-page";

    /// <summary>Reachability code for an element/block type with no route of its own.</summary>
    public const string ComposedFromBlock = "composed-from-block";

    /// <summary>Reachability code when no content type exists for the alias.</summary>
    public const string Unknown = "unknown";

    /// <summary>
    /// Hedged warning text for <see cref="ComposedFromBlock"/>. We cannot know
    /// whether any page actually routes the element, so this never asserts
    /// emission and must never be rendered as a success state.
    /// </summary>
    public const string ComposedFromBlockWarning =
        "Element/block types emit JSON-LD only when a containing page's block mapping routes them; " +
        "if no page routes this type, this mapping never emits on its own route.";

    private readonly IContentTypeService _contentTypeService;

    public MappingReachabilityClassifier(IContentTypeService contentTypeService)
        => _contentTypeService = contentTypeService;

    public string Classify(string contentTypeAlias)
    {
        if (string.IsNullOrWhiteSpace(contentTypeAlias))
            return Unknown;

        var contentType = _contentTypeService.Get(contentTypeAlias);
        if (contentType is null)
            return Unknown;

        return contentType.IsElement ? ComposedFromBlock : RoutedPage;
    }
}
