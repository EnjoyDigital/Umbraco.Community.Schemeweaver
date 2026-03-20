using Umbraco.Community.SchemeWeaver.Models.Api;

namespace Umbraco.Community.SchemeWeaver.Services;

/// <summary>
/// Generates Umbraco content types from Schema.org type definitions.
/// </summary>
public interface IContentTypeGenerator
{
    Task<Guid> GenerateContentTypeAsync(ContentTypeGenerationRequest request, CancellationToken cancellationToken = default);
}
