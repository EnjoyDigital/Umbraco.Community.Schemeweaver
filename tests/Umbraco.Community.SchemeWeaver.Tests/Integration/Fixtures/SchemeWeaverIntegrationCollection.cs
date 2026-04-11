using Xunit;

namespace Umbraco.Community.SchemeWeaver.Tests.Integration.Fixtures;

/// <summary>
/// xUnit collection that shares a single <see cref="SchemeWeaverWebApplicationFactory"/>
/// across every integration test class. Umbraco holds internal static state
/// (e.g. cached <c>DataValueEditorFactory</c> closures over <c>IServiceProvider</c>)
/// that breaks when two <see cref="Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory{TEntryPoint}"/>
/// instances boot in parallel — a second factory picks up a disposed provider and
/// blows up inside <c>TestDataSeeder</c>. Pinning every integration test class to
/// one collection forces sequential execution and a single host lifetime.
/// </summary>
[CollectionDefinition(Name)]
public class SchemeWeaverIntegrationCollection
    : ICollectionFixture<SchemeWeaverWebApplicationFactory>
{
    public const string Name = "SchemeWeaver Integration";
}
