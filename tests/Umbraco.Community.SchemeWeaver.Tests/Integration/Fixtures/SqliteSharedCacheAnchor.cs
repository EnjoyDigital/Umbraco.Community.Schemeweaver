using Microsoft.Data.Sqlite;

namespace Umbraco.Community.SchemeWeaver.Tests.Integration.Fixtures;

/// <summary>
/// Holds a SQLite shared cache open for as long as it is not disposed.
/// </summary>
/// <remarks>
/// <para>
/// A SQLite shared cache exists only while at least one connection to that file is
/// open. Umbraco opens and closes connections constantly — one per NPoco scope, plus
/// background jobs — so on a fixture using <c>Cache=Shared;Pooling=False</c> the open
/// count repeatedly hits zero and the native shared-cache structure is torn down and
/// rebuilt underneath threads that are still working against it.
/// </para>
/// <para>
/// That produced three intermittent failures, all of which presented as an unrelated
/// test failing on an ordinary operation:
/// </para>
/// <list type="bullet">
///   <item><description><c>ArgumentOutOfRangeException</c> out of
///   <c>sqlite3_prepare_v2</c> — use-after-free on the destroyed handle.</description></item>
///   <item><description><c>SQLite Error 8: 'attempt to write a readonly database'</c>
///   — on a file that is perfectly writable.</description></item>
///   <item><description><c>SQLite Error 6: 'database table is locked'</c> — the
///   shared-cache table-lock error, from lock state left over across a rebuild.</description></item>
/// </list>
/// <para>
/// Keeping one connection open means the count never reaches zero, so the cache is
/// created once and lives until the fixture is disposed.
/// </para>
/// <para>
/// Note that <see cref="RichResultsAuditFactory"/> needs no anchor: it uses
/// <c>Pooling=True</c>, and the pool keeps connections alive, which pins the cache for
/// the same reason. The two <c>Pooling=False</c> factories are precisely the ones that
/// flaked.
/// </para>
/// <para>
/// Dropping <c>Cache=Shared</c> instead was measured and is far worse — the Deploy
/// suite went from ~1 failure in 7 runs to 6 in 8.
/// </para>
/// </remarks>
internal sealed class SqliteSharedCacheAnchor : IDisposable
{
    private SqliteConnection? _connection;

    private SqliteSharedCacheAnchor(SqliteConnection connection) => _connection = connection;

    /// <summary>
    /// Opens the anchor. Call this only once the database file definitely exists —
    /// Umbraco's unattended install must create it first, and we must not hold a handle
    /// across that.
    /// </summary>
    public static SqliteSharedCacheAnchor Open(string databasePath)
    {
        var connection = new SqliteConnection(
            $"Data Source={databasePath};Cache=Shared;Pooling=False");
        connection.Open();
        return new SqliteSharedCacheAnchor(connection);
    }

    public void Dispose()
    {
        // Release before the fixture deletes its temp directory, or the open handle
        // keeps the database file locked on Windows.
        _connection?.Dispose();
        _connection = null;
        SqliteConnection.ClearAllPools();
    }
}
