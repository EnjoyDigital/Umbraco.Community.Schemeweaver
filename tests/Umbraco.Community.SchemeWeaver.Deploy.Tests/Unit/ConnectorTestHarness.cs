using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Umbraco.Cms.Core.Models;
using Umbraco.Cms.Core.Services;
using Umbraco.Community.SchemeWeaver.Deploy.Connectors;
using Umbraco.Community.SchemeWeaver.Models.Entities;
using Umbraco.Community.SchemeWeaver.Persistence;

namespace Umbraco.Community.SchemeWeaver.Deploy.Tests.Unit;

/// <summary>
/// Builds a <see cref="SchemaMappingServiceConnector"/> over a substituted scoped
/// repository, mirroring the fake scope-factory chain the uSync serializer tests use
/// (the connector is a singleton and resolves the scoped repository per call).
/// </summary>
internal static class ConnectorTestHarness
{
    static ConnectorTestHarness()
    {
        // Root-UDI creation (Udi.Create(entityType)) requires the type to be known
        // to the static UdiParser. Production registers it in the composer; tests
        // must not depend on the composer test having run first. TryAdd — idempotent.
        Umbraco.Cms.Core.UdiParser.RegisterUdiType(
            SchemeWeaverDeployConstants.MappingUdiEntityType, Umbraco.Cms.Core.UdiType.GuidUdi);
    }

    public static (SchemaMappingServiceConnector Connector, ISchemaMappingRepository Repository, IContentTypeService ContentTypeService) Build()
    {
        var repository = Substitute.For<ISchemaMappingRepository>();
        var contentTypeService = Substitute.For<IContentTypeService>();

        // By default every content type "exists" so artifact building isn't skipped.
        contentTypeService.Get(Arg.Any<Guid>()).Returns(Substitute.For<IContentType>());

        var connector = Build(repository, contentTypeService);
        return (connector, repository, contentTypeService);
    }

    public static SchemaMappingServiceConnector Build(
        ISchemaMappingRepository repository, IContentTypeService contentTypeService)
    {
        var serviceProvider = Substitute.For<IServiceProvider>();
        serviceProvider.GetService(typeof(ISchemaMappingRepository)).Returns(repository);
        serviceProvider.GetService(typeof(IContentTypeService)).Returns(contentTypeService);
        var scope = Substitute.For<IServiceScope>();
        scope.ServiceProvider.Returns(serviceProvider);
        var scopeFactory = Substitute.For<IServiceScopeFactory>();
        scopeFactory.CreateScope().Returns(scope);

        return new SchemaMappingServiceConnector(
            scopeFactory, Substitute.For<ILogger<SchemaMappingServiceConnector>>());
    }

    public static SchemaMapping Mapping(int id = 1, string alias = "blogPost", Guid? key = null) => new()
    {
        Id = id,
        ContentTypeAlias = alias,
        ContentTypeKey = key ?? new Guid("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee"),
        SchemaTypeName = "BlogPosting",
        IsEnabled = true,
        IsInherited = false,
        IdOverride = null,
    };

    public static PropertyMapping Row(int id, int mappingId = 1, string schemaProperty = "Headline") => new()
    {
        Id = id,
        SchemaMappingId = mappingId,
        SchemaPropertyName = schemaProperty,
        SourceType = "property",
        ContentTypePropertyAlias = "title",
        IsAutoMapped = false,
    };
}
