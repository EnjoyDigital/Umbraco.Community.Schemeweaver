using Microsoft.Extensions.DependencyInjection;
using Umbraco.Cms.Infrastructure.Scoping;
using Umbraco.Community.SchemeWeaver.Models.Entities;
using Umbraco.Community.SchemeWeaver.Persistence;
using Xunit;

namespace Umbraco.Community.SchemeWeaver.Tests.Integration.Fixtures;

/// <summary>
/// Base class for SchemeWeaver integration tests. Concrete derivations must be
/// attributed with
/// <c>[Collection(SchemeWeaverIntegrationCollection.Name)]</c> so every test
/// class shares a single <see cref="SchemeWeaverWebApplicationFactory"/>, which
/// avoids the Umbraco static-state collision that happens when multiple
/// <see cref="Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory{TEntryPoint}"/>
/// instances boot in parallel.
/// </summary>
public abstract class UmbracoIntegrationTestBase : IAsyncLifetime
{
    protected SchemeWeaverWebApplicationFactory Factory { get; }

    protected HttpClient Client { get; }

    protected UmbracoIntegrationTestBase(SchemeWeaverWebApplicationFactory factory)
    {
        Factory = factory;
        Client = factory.CreateClient();
    }

    /// <summary>
    /// Creates a new DI scope from the running host's service provider. Callers
    /// should <see cref="IDisposable.Dispose"/> the returned scope (use a
    /// <c>using</c> block) so scoped services are released promptly.
    /// </summary>
    protected IServiceScope CreateServiceScope() => Factory.Services.CreateScope();

    /// <summary>
    /// Resolves the real <see cref="ISchemaMappingRepository"/> from a fresh DI
    /// scope, along with the scope itself so the caller can dispose it when done.
    /// </summary>
    protected (ISchemaMappingRepository Repository, IServiceScope Scope) CreateRepository()
    {
        var scope = CreateServiceScope();
        var repository = scope.ServiceProvider.GetRequiredService<ISchemaMappingRepository>();
        return (repository, scope);
    }

    /// <summary>
    /// Deletes every row from the SchemeWeaver tables. Umbraco's own bootstrap
    /// content is left intact so the backoffice stays usable between tests.
    /// Retries briefly on transient SQLite startup races ("no such table" while
    /// package migrations are still completing, or temporary nested-transaction
    /// collisions during host bootstrap).
    /// </summary>
    protected void ResetSchemeWeaverTables()
    {
        const int maxAttempts = 50;
        const int delayMs = 100;

        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            try
            {
                using var scope = CreateServiceScope();
                var scopeProvider = scope.ServiceProvider.GetRequiredService<IScopeProvider>();

                using (var dbScope = scopeProvider.CreateScope())
                {
                    dbScope.Database.Execute($"DELETE FROM {SchemeWeaverConstants.Tables.PropertyMapping}");
                    dbScope.Database.Execute($"DELETE FROM {SchemeWeaverConstants.Tables.SchemaMapping}");
                    dbScope.Complete();
                }

                // The repository caches mapping reads; this reset writes the tables directly, so the
                // cache must be cleared too or the next read would serve the pre-delete snapshot.
                scope.ServiceProvider.GetRequiredService<ISchemaMappingRepository>().ClearCache();

                // Verify from a fresh scope that the delete actually stuck. A row
                // can reappear when a write started by the previous test commits
                // after our DELETE; loop (deleting again) until the tables stay
                // empty so no test starts against residue.
                using (var verifyScope = scopeProvider.CreateScope(autoComplete: true))
                {
                    var remaining = verifyScope.Database.ExecuteScalar<int>(
                        $"SELECT COUNT(1) FROM {SchemeWeaverConstants.Tables.SchemaMapping}");
                    if (remaining == 0)
                    {
                        return;
                    }
                }

                Thread.Sleep(delayMs);
            }
            catch (Microsoft.Data.Sqlite.SqliteException ex)
                when (attempt < maxAttempts && IsTransientSqliteStartupError(ex))
            {
                Thread.Sleep(delayMs);
            }
            catch (ArgumentOutOfRangeException)
                when (attempt < maxAttempts)
            {
                // Microsoft.Data.Sqlite occasionally surfaces a broken native handle
                // as ArgumentOutOfRangeException from sqlite3_prepare_v2 while
                // opening a connection. Transient — retry.
                Thread.Sleep(delayMs);
            }
        }

        throw new InvalidOperationException(
            "SchemeWeaver tables could not be reset to empty — a background writer keeps re-inserting rows.");
    }

    private static bool IsTransientSqliteStartupError(Microsoft.Data.Sqlite.SqliteException exception)
    {
        var message = exception.Message;
        return
            message.Contains("no such table", StringComparison.OrdinalIgnoreCase) ||
            // Umbraco/NPoco occasionally overlaps startup scopes on CI runners.
            message.Contains("cannot start a transaction within a transaction", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Inserts a single <see cref="SchemaMapping"/> with optional property
    /// mappings and returns the persisted entity (with <c>Id</c> assigned).
    /// </summary>
    protected SchemaMapping SeedMapping(
        string contentTypeAlias,
        string schemaTypeName,
        bool isEnabled = true,
        bool isInherited = false,
        IEnumerable<PropertyMapping>? propertyMappings = null)
    {
        var (repository, scope) = CreateRepository();
        using (scope)
        {
            var mapping = new SchemaMapping
            {
                ContentTypeAlias = contentTypeAlias,
                ContentTypeKey = Guid.NewGuid(),
                SchemaTypeName = schemaTypeName,
                IsEnabled = isEnabled,
                IsInherited = isInherited,
            };

            var saved = repository.Save(mapping);

            if (propertyMappings is not null)
            {
                repository.SavePropertyMappings(saved.Id, propertyMappings);
            }

            return saved;
        }
    }

    public Task InitializeAsync()
    {
        // Ensure a clean slate before every test. Running synchronously is fine —
        // NPoco's scope provider is blocking and ResetSchemeWeaverTables completes
        // in microseconds.
        ResetSchemeWeaverTables();
        return Task.CompletedTask;
    }

    public Task DisposeAsync() => Task.CompletedTask;
}
