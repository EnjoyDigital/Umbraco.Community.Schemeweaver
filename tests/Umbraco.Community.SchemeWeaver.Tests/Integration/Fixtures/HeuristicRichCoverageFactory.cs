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
public class HeuristicRichCoverageFactory : WebApplicationFactory<Program>, Xunit.IAsyncLifetime
{
    private readonly string _dataDirectory;
    private readonly string _databasePath;

    /// <summary>
    /// Pins the SQLite shared cache for the fixture's lifetime — see
    /// <see cref="SqliteSharedCacheAnchor"/>. This factory uses
    /// <c>Cache=Shared;Pooling=False</c>, the same combination that made the Deploy
    /// suite flake, and without the anchor this gate intermittently failed with
    /// <c>SQLite Error 6: 'database table is locked'</c> and took 11-21 minutes.
    /// </summary>
    private SqliteSharedCacheAnchor? _sharedCacheAnchor;

    public HeuristicRichCoverageFactory()
    {
        _dataDirectory = Path.Join(
            Path.GetTempPath(),
            $"schemeweaver-heuristic-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_dataDirectory);

        _databasePath = Path.Join(_dataDirectory, "Umbraco.sqlite.db");
    }

    /// <summary>
    /// Boots the host, then anchors the shared cache. The anchor must come after boot:
    /// Umbraco's unattended install has to create the database first.
    /// </summary>
    public Task InitializeAsync()
    {
        // Forces the host to build and the unattended install to run.
        CreateClient().Dispose();

        _sharedCacheAnchor = SqliteSharedCacheAnchor.Open(_databasePath);
        return Task.CompletedTask;
    }

    Task Xunit.IAsyncLifetime.DisposeAsync() => Task.CompletedTask;

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("IntegrationTest");

        builder.ConfigureAppConfiguration((context, config) =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                // Cache=Shared needs something holding it open — see the anchor field.
                ["ConnectionStrings:umbracoDbDSN"] =
                    $"Data Source={_databasePath};Cache=Shared;Foreign Keys=True;Pooling=False",
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

            // Umbraco.AI's background EF migration must not run in integration hosts —
            // its DDL races test traffic on the shared cache, and it migrates the wrong
            // database anyway. Full story: UmbracoAiTestHostOverrides.
            UmbracoAiTestHostOverrides.RemoveUmbracoAiMigrationHandler(services);
        });
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (!disposing) return;

        // Release before deleting the data directory, or the open handle keeps the
        // database file locked on Windows.
        _sharedCacheAnchor?.Dispose();
        _sharedCacheAnchor = null;

        try
        {
            if (Directory.Exists(_dataDirectory))
                Directory.Delete(_dataDirectory, recursive: true);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
