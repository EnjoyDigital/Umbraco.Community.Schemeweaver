using Microsoft.AspNetCore.Authorization.Policy;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

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
public class SchemeWeaverWebApplicationFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    /// <summary>
    /// A cheap, side-effect-free backoffice management endpoint used only to warm the
    /// host. It must sit under <c>/umbraco/management/api</c> so the request travels
    /// through <c>BackOfficeAuthorizationInitializationMiddleware</c> — that is the
    /// code path being warmed.
    /// </summary>
    private const string WarmUpRoute = "/umbraco/management/api/v1/schemeweaver/server-context";

    private readonly string _databasePath;
    private readonly string _dataDirectory;

    /// <summary>
    /// Holds the SQLite shared cache alive for the lifetime of the fixture. See
    /// <see cref="InitializeAsync"/> for why this exists.
    /// </summary>
    private Microsoft.Data.Sqlite.SqliteConnection? _sharedCacheAnchor;

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
                //
                // Cache=Shared is LOAD-BEARING — do not remove it. It was tried: without
                // it, Pooling=False gives every connection its own independent handle and
                // lock state, and the Deploy suite went from ~1 failure in 7 runs to 6 in
                // 8. It matches Umbraco's own default SQLite connection string. What the
                // shared cache DOES need is something holding it open — see
                // AnchorSharedCache.
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

    /// <summary>
    /// Boots the host and settles it before any test runs, then pins the SQLite shared
    /// cache for the fixture's lifetime.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Two jobs, in order. First, issue one real request so the host's lazy first-request
    /// work (route table, OpenIddict backoffice application, Umbraco's unattended install
    /// finishing) happens here under a bounded retry rather than inside whichever test
    /// happened to go first. Second — and this is the part that actually fixed the flake —
    /// call <see cref="AnchorSharedCache"/> once the database definitely exists.
    /// </para>
    /// <para>
    /// The retry loop is deliberately tolerant: during boot a request can legitimately
    /// fail or return non-success for a short while.
    /// </para>
    /// </remarks>
    public async Task InitializeAsync()
    {
        const int maxAttempts = 50;
        var delay = TimeSpan.FromMilliseconds(100);

        using var client = CreateClient();
        string? lastOutcome = null;

        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            try
            {
                using var response = await client.GetAsync(WarmUpRoute);
                if (response.IsSuccessStatusCode)
                {
                    AnchorSharedCache();
                    return;
                }

                lastOutcome = $"HTTP {(int)response.StatusCode}";
            }
            catch (Exception ex) when (ex is HttpRequestException or InvalidOperationException or IOException)
            {
                // The host can still be mid-boot; treat as transient and retry.
                lastOutcome = ex.GetType().Name + ": " + ex.Message;
            }

            await Task.Delay(delay);
        }

        throw new InvalidOperationException(
            $"Integration host never became ready: {maxAttempts} warm-up requests to " +
            $"'{WarmUpRoute}' all failed. Last outcome: {lastOutcome}.");
    }

    /// <summary>
    /// Opens one connection and holds it for the fixture's lifetime, purely to pin the
    /// SQLite shared cache.
    /// </summary>
    /// <remarks>
    /// <para>
    /// With <c>Cache=Shared</c> and <c>Pooling=False</c>, the shared cache exists only
    /// while at least one connection to that path is open. Umbraco opens and closes
    /// connections constantly (per NPoco scope, plus background jobs), so the count
    /// repeatedly hits zero and the native shared-cache structure is torn down and
    /// rebuilt underneath threads that are still working against it. That is what
    /// produced the two intermittent failures — both surfacing as an unrelated test
    /// getting a bare 500 out of a perfectly ordinary save:
    /// </para>
    /// <list type="bullet">
    ///   <item><description><c>ArgumentOutOfRangeException</c> from
    ///   <c>sqlite3_prepare_v2</c> — use-after-free on the destroyed shared handle.</description></item>
    ///   <item><description><c>SQLite Error 8: 'attempt to write a readonly database'</c>
    ///   — on a file that is perfectly writable; the rebuilt cache had lost its
    ///   write lock state.</description></item>
    /// </list>
    /// <para>
    /// Keeping one connection open means the count never reaches zero, so the shared
    /// cache is created once and lives until the fixture is disposed. Anchoring AFTER
    /// warm-up is deliberate: Umbraco's unattended install must create the database
    /// first, and we must not hold a handle across that.
    /// </para>
    /// <para>
    /// Removing <c>Cache=Shared</c> instead was measured and is much worse — see the
    /// connection-string comment.
    /// </para>
    /// </remarks>
    private void AnchorSharedCache()
    {
        _sharedCacheAnchor = new Microsoft.Data.Sqlite.SqliteConnection(
            $"Data Source={_databasePath};Cache=Shared;Pooling=False");
        _sharedCacheAnchor.Open();
    }

    Task IAsyncLifetime.DisposeAsync() => Task.CompletedTask;

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);

        if (!disposing)
        {
            return;
        }

        // Release the shared-cache anchor before deleting the data directory, or the
        // open handle keeps the database file locked on Windows.
        _sharedCacheAnchor?.Dispose();
        _sharedCacheAnchor = null;
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();

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
