using Microsoft.AspNetCore.Authorization.Policy;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Umbraco.Community.SchemeWeaver.Tests.Integration.Fixtures;

/// <summary>
/// WebApplicationFactory that boots the SchemeWeaver TestHost Umbraco app against a
/// per-instance temp SQLite database. Each factory instance owns its own database
/// file so xUnit test classes (each with their own <see cref="Xunit.IClassFixture{T}"/>)
/// don't collide when run in parallel.
///
/// <para>
/// SQLite <c>:memory:</c> is deliberately avoided — NPoco's scope provider opens
/// multiple connections per operation and an in-memory database is only visible to
/// the connection that created it, so the second connection sees an empty DB.
/// </para>
///
/// <para>
/// Backoffice authentication is bypassed via <see cref="TestPolicyEvaluator"/>
/// registered through <c>ConfigureTestServices</c>. Integration tests can therefore
/// call protected management API endpoints directly without going through a real
/// cookie login flow.
/// </para>
/// </summary>
public class SchemeWeaverWebApplicationFactory : WebApplicationFactory<Program>
{
    private readonly string _databasePath;
    private readonly string _dataDirectory;

    public SchemeWeaverWebApplicationFactory()
    {
        // Unique per-factory temp directory so parallel test classes don't share state.
        _dataDirectory = Path.Join(
            Path.GetTempPath(),
            $"schemeweaver-it-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_dataDirectory);

        _databasePath = Path.Join(_dataDirectory, "Umbraco.sqlite.db");
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("IntegrationTest");

        builder.ConfigureAppConfiguration((context, config) =>
        {
            // Override the connection string to an absolute path and force unattended
            // install so BootUmbracoAsync creates tables and runs the SchemeWeaver
            // migration plan on first boot.
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                // Pooling is deliberately OFF (Umbraco's default has it on): the test
                // host's background jobs open pooled connections concurrently with the
                // rapid per-test scope churn, and Microsoft.Data.Sqlite's pool then
                // intermittently hands out a broken handle (native
                // ArgumentOutOfRangeException in sqlite3_prepare_v2 during Open).
                // Unpooled file-based opens are cheap and race-free.
                ["ConnectionStrings:umbracoDbDSN"] =
                    $"Data Source={_databasePath};Cache=Shared;Foreign Keys=True;Pooling=False",
                ["ConnectionStrings:umbracoDbDSN_ProviderName"] = "Microsoft.Data.Sqlite",
                ["Umbraco:CMS:Unattended:InstallUnattended"] = "true",
                ["Umbraco:CMS:Unattended:UnattendedUserName"] = "Integration Test",
                ["Umbraco:CMS:Unattended:UnattendedUserEmail"] = "integration@test.local",
                ["Umbraco:CMS:Unattended:UnattendedUserPassword"] = "IntegrationTest1234!",
                ["Umbraco:CMS:Unattended:UnattendedTelemetryLevel"] = "Minimal",
                ["Umbraco:CMS:Global:Id"] = Guid.NewGuid().ToString(),
                // Suppress uSync first-boot import — integration tests seed their own
                // data via UmbracoIntegrationTestBase and don't need the full TestHost
                // content tree.
                ["uSync:Settings:ImportOnFirstBoot"] = "false",
                ["uSync:Settings:ImportAtStartup"] = "None",
            });
        });

        // ConfigureTestServices runs after the app's own ConfigureServices, so
        // registering IPolicyEvaluator here guarantees our override wins over the
        // default one the authorization middleware would otherwise resolve.
        builder.ConfigureTestServices(services =>
        {
            services.AddTransient<IPolicyEvaluator, TestPolicyEvaluator>();

            // The uSync addon's first-boot mapping import fires on
            // UmbracoApplicationStarted and runs in the background AFTER tests have
            // begun (the integration database is always empty at boot, so its
            // "mappings already exist" guard never trips). Its inserts race the
            // per-test table resets and intermittently leak fixture mappings like
            // "medicalClinicPage" into count assertions. Integration tests seed
            // their own data, so remove the handler outright — the uSync settings
            // above don't gate it.
            foreach (var descriptor in services
                .Where(d => d.ImplementationType ==
                    typeof(Umbraco.Community.SchemeWeaver.uSync.SchemaMappingImportNotificationHandler))
                .ToList())
            {
                services.Remove(descriptor);
            }
        });
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);

        if (!disposing)
        {
            return;
        }

        // Best-effort cleanup of the temp data directory. Umbraco may still hold
        // open handles for a moment after Dispose, so we swallow IO exceptions.
        try
        {
            if (Directory.Exists(_dataDirectory))
            {
                Directory.Delete(_dataDirectory, recursive: true);
            }
        }
        catch (IOException)
        {
            // Ignore — the OS will clean up the temp folder eventually.
        }
        catch (UnauthorizedAccessException)
        {
            // Same — leave it to the OS.
        }
    }
}
