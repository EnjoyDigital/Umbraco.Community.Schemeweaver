using Umbraco.Community.SchemeWeaver.Seo.Models.Api;

namespace Umbraco.Community.SchemeWeaver.Seo.Services;

public interface ISeoContentTypeStatusService
{
    IReadOnlyList<SeoContentTypeStatusDto> GetAll();

    SeoContentTypeStatusDto? Get(string contentTypeAlias);
}
