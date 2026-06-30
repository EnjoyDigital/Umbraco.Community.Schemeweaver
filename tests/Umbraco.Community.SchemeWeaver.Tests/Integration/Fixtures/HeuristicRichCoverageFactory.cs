using Microsoft.AspNetCore.Authorization.Policy;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Umbraco.Community.SchemeWeaver.Tests.Integration.Fixtures;

/// <summary>
/// WebApplicationFactory for the heuristic rich-coverage gate. Unlike
/// <see cref="SchemeWeaverWebApplicationFactory"/> this enables uSync first-boot import so the
/// TestHost's content types (and their block data types) are seeded — the heuristic auto-mapper
/// needs the real content-type schema to map against. Only the schema is needed (not the published
/// content tree), so the gate polls <c>IContentTypeService</c> rather than the content cache.
/// </summary>
public class HeuristicRichCoverageFactory : WebApplicationFactory<Program>
{
    private readonly string _dataDirectory;

    public HeuristicRichCoverageFactory()
    {
        _dataDirectory = Path.Join(
            Path.GetTempPath(),
            $"schemeweaver-heuristic-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_dataDirectory);
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("IntegrationTest");

        builder.ConfigureAppConfiguration((context, config) =>
        {
            var dbPath = Path.Join(_dataDirectory, "Umbraco.sqlite.db");

            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:umbracoDbDSN"] =
                    $"Data Source={dbPath};Cache=Shared;Foreign Keys=True;Pooling=False",
                ["ConnectionStrings:umbracoDbDSN_ProviderName"] = "Microsoft.Data.Sqlite",
                ["Umbraco:CMS:Unattended:InstallUnattended"] = "true",
                ["Umbraco:CMS:Unattended:UnattendedUserName"] = "Heuristic Runner",
                ["Umbraco:CMS:Unattended:UnattendedUserEmail"] = "heuristic@test.local",
                ["Umbraco:CMS:Unattended:UnattendedUserPassword"] = "HeuristicRunner1234!",
                ["Umbraco:CMS:Unattended:UnattendedTelemetryLevel"] = "Minimal",
                ["Umbraco:CMS:Global:Id"] = Guid.NewGuid().ToString(),

                // Seed the TestHost schema (content types + data types) via uSync first-boot import.
                ["uSync:Settings:ImportOnFirstBoot"] = "true",
                ["uSync:Settings:FirstBootGroup"] = "All",
                ["uSync:Settings:ImportAtStartup"] = "None",
            });
        });

        builder.ConfigureTestServices(services =>
        {
            services.AddTransient<IPolicyEvaluator, TestPolicyEvaluator>();
        });
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (!disposing) return;

        try
        {
            if (Directory.Exists(_dataDirectory))
                Directory.Delete(_dataDirectory, recursive: true);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
