using Umbraco.Community.SchemeWeaver.Seo.Models.Api;

namespace Umbraco.Community.SchemeWeaver.Seo.Services;

public interface ISeoHealthCheckService
{
    /// <summary>Previews the most recent published page of every mapped type and aggregates the issues.</summary>
    SeoHealthReportDto Run(bool force);

    void Evict();
}
