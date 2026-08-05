using Microsoft.Extensions.DependencyInjection;

namespace Umbraco.Community.SchemeWeaver.Tests.Integration.Fixtures;

/// <summary>
/// Keeps Umbraco.AI's boot-time EF Core migration out of integration hosts.
/// </summary>
/// <remarks>
/// <para>
/// The TestHost includes the SchemeWeaver.AI satellite on both majors, and Umbraco.AI's
/// <c>RunAIMigrationNotificationHandler</c> runs EF migrations in the background AFTER
/// the host starts serving requests. In these fixtures that is actively harmful, twice
/// over:
/// </para>
/// <para>
/// 1. A test request that lands while the migration's DDL is running shares the SQLite
/// shared cache with it and dies at the native interop layer
/// (<c>ArgumentOutOfRangeException</c> in <c>sqlite3_prepare_v2</c> → HTTP 500).
/// Observed 2-for-2 on the Linux CI runners, where boot is slow enough for the first
/// test to win the race; never on faster local Windows runs.
/// </para>
/// <para>
/// 2. Waiting for the migration instead of removing it was tried and CANNOT work: the
/// handler observably runs against a connection string captured outside the fixture's
/// in-memory <c>umbracoDbDSN</c> override — it logs "completed successfully" (in ~30ms)
/// while the AI tables never appear in the fixture's temp database, so any
/// wait-for-tables/wait-for-EF-pending gate spins to its ceiling. (An out-of-process
/// SQLite poll was also tried and additionally starved the writer's locks.)
/// </para>
/// <para>
/// Integration tests exercise no Umbraco.AI behaviour (AI coverage is unit-level), so
/// the migration is pure boot noise here. Without its tables, Umbraco.AI's own
/// background jobs (usage-stats rollup, settings reads) error-log "no such table" —
/// that is logged-and-swallowed by their runners and fails nothing; the hosts already
/// behaved that way whenever a query beat the migration.
/// </para>
/// <para>
/// Same pattern as the <c>SchemaMappingImportNotificationHandler</c> removal in
/// <see cref="SchemeWeaverWebApplicationFactory"/>: strip the handler's registrations
/// in <c>ConfigureTestServices</c>, which runs after the app's own ConfigureServices.
/// </para>
/// </remarks>
internal static class UmbracoAiTestHostOverrides
{
    public static void RemoveUmbracoAiMigrationHandler(IServiceCollection services)
    {
        foreach (var descriptor in services
            .Where(d => d.ImplementationType ==
                typeof(Umbraco.AI.Persistence.Notifications.RunAIMigrationNotificationHandler))
            .ToList())
        {
            services.Remove(descriptor);
        }
    }
}
