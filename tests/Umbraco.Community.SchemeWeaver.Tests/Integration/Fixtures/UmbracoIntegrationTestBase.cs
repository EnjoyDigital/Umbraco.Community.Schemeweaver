using Microsoft.Extensions.DependencyInjection;
using Umbraco.Cms.Infrastructure.Scoping;
using Umbraco.Community.SchemeWeaver.Models.Entities;
using Umbraco.Community.SchemeWeaver.Persistence;
using Xunit;

namespace Umbraco.Community.SchemeWeaver.Tests.Integration.Fixtures;

/// <summary>
/// Base class for SchemeWeaver integration tests. Provides a shared
/// <see cref="SchemeWeaverWebApplicationFactory"/> via xUnit's
/// <see cref="IClassFixture{TFixture}"/>, plus helpers to resolve services from
/// fresh DI scopes and wipe SchemeWeaver tables between tests.
/// </summary>
public abstract class UmbracoIntegrationTestBase
    : IClassFixture<SchemeWeaverWebApplicationFactory>, IAsyncLifetime
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
    /// </summary>
    protected void ResetSchemeWeaverTables()
    {
        using var scope = CreateServiceScope();
        var scopeProvider = scope.ServiceProvider.GetRequiredService<IScopeProvider>();

        using var dbScope = scopeProvider.CreateScope();
        dbScope.Database.Execute($"DELETE FROM {SchemeWeaverConstants.Tables.PropertyMapping}");
        dbScope.Database.Execute($"DELETE FROM {SchemeWeaverConstants.Tables.SchemaMapping}");
        dbScope.Complete();
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
