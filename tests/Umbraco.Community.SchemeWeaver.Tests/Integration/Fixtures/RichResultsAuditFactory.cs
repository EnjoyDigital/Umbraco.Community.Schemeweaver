using Microsoft.AspNetCore.Authorization.Policy;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Umbraco.Community.SchemeWeaver.Tests.Integration.Fixtures;

/// <summary>
/// Variant WebApplicationFactory used by the Rich Results audit harness. Unlike
/// <see cref="SchemeWeaverWebApplicationFactory"/> this DOES enable uSync first-
/// boot import so the full TestHost content tree (147 nodes, 142 mappings) is
/// available for the audit to walk — otherwise the audit has nothing to scan.
///
/// The tradeoff is a much slower boot (uSync import of the whole v17 folder),
/// but this factory is only instantiated when the audit trait is run.
/// </summary>
public class RichResultsAuditFactory : WebApplicationFactory<Program>
{
    private readonly string _dataDirectory;

    public RichResultsAuditFactory()
    {
        _dataDirectory = Path.Join(
            Path.GetTempPath(),
            $"schemeweaver-audit-{Guid.NewGuid():N}");
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
                    $"Data Source={dbPath};Cache=Shared;Foreign Keys=True;Pooling=True",
                ["ConnectionStrings:umbracoDbDSN_ProviderName"] = "Microsoft.Data.Sqlite",
                ["Umbraco:CMS:Unattended:InstallUnattended"] = "true",
                ["Umbraco:CMS:Unattended:UnattendedUserName"] = "Audit Runner",
                ["Umbraco:CMS:Unattended:UnattendedUserEmail"] = "audit@test.local",
                ["Umbraco:CMS:Unattended:UnattendedUserPassword"] = "AuditRunner1234!",
                ["Umbraco:CMS:Unattended:UnattendedTelemetryLevel"] = "Minimal",
                ["Umbraco:CMS:Global:Id"] = Guid.NewGuid().ToString(),

                // KEY DIFFERENCE from SchemeWeaverWebApplicationFactory: uSync
                // first-boot import runs so the TestHost content tree is seeded.
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
