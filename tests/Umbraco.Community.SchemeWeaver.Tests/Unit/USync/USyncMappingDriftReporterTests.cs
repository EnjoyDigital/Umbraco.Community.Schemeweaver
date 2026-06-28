using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Umbraco.Community.SchemeWeaver.Models.Entities;
using Umbraco.Community.SchemeWeaver.Persistence;
using Umbraco.Community.SchemeWeaver.Services;
using Umbraco.Community.SchemeWeaver.uSync;
using uSync.Core.Serialization;
using Xunit;

namespace Umbraco.Community.SchemeWeaver.Tests.Unit.USync;

public class USyncMappingDriftReporterTests : IDisposable
{
    private readonly string _contentRoot = Path.Combine(Path.GetTempPath(), "sw-drift-" + Guid.NewGuid().ToString("N"));
    private readonly ISchemaMappingRepository _repository = Substitute.For<ISchemaMappingRepository>();
    private readonly IHostEnvironment _hostEnvironment = Substitute.For<IHostEnvironment>();
    private readonly SyncSerializerCollection _serializers;
    private readonly IServiceScopeFactory _scopeFactory;

    public USyncMappingDriftReporterTests()
    {
        Directory.CreateDirectory(_contentRoot);
        _hostEnvironment.ContentRootPath.Returns(_contentRoot);

        var serviceProvider = Substitute.For<IServiceProvider>();
        serviceProvider.GetService(typeof(ISchemaMappingRepository)).Returns(_repository);
        var scope = Substitute.For<IServiceScope>();
        scope.ServiceProvider.Returns(serviceProvider);
        _scopeFactory = Substitute.For<IServiceScopeFactory>();
        _scopeFactory.CreateScope().Returns(scope);
        _serializers = new SyncSerializerCollection(() =>
            new[] { new SchemaMappingSerializer(_scopeFactory, Substitute.For<ILogger<SchemaMappingSerializer>>()) });
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_contentRoot)) Directory.Delete(_contentRoot, recursive: true); } catch { /* best effort */ }
    }

    private static SchemaMapping Mapping(string alias, string schemaType = "Article") => new()
    {
        Id = 1,
        ContentTypeAlias = alias,
        ContentTypeKey = Guid.Parse("11111111-1111-1111-1111-111111111111"),
        SchemaTypeName = schemaType,
        IsEnabled = true
    };

    private USyncMappingDriftReporter CreateReporter() =>
        new(_serializers, _scopeFactory, _hostEnvironment, Substitute.For<ILogger<USyncMappingDriftReporter>>());

    private USyncMappingExporter CreateExporter() =>
        new(_serializers, _scopeFactory, _hostEnvironment, new MappingFileWriter(), Substitute.For<ILogger<USyncMappingExporter>>());

    [Fact]
    public void GetStatus_NoFile_ReturnsDbOnly()
    {
        _repository.GetByContentTypeAlias("article").Returns(Mapping("article"));
        _repository.GetPropertyMappings(Arg.Any<int>()).Returns([]);

        CreateReporter().GetStatus("article").Should().Be(MappingDriftStatus.DbOnly);
    }

    [Fact]
    public void GetStatus_ExportedThenUnchanged_ReturnsInSync()
    {
        _repository.GetByContentTypeAlias("article").Returns(Mapping("article"));
        _repository.GetAll().Returns(new[] { Mapping("article") });
        _repository.GetPropertyMappings(Arg.Any<int>()).Returns([]);

        // Export writes the config; an immediate re-serialise must round-trip to the same XML.
        CreateExporter().Export("article");

        CreateReporter().GetStatus("article").Should().Be(MappingDriftStatus.InSync);
    }

    [Fact]
    public void GetStatus_OnlyGuidCaseDiffers_ReturnsInSync()
    {
        // Older fixtures stored ContentTypeKey upper-case; the current serializer emits lower-case.
        // GUIDs are case-insensitive identifiers, so this must NOT be reported as drift.
        var key = Guid.Parse("abcdef12-3456-7890-abcd-ef1234567890");
        var mapping = new SchemaMapping
        {
            Id = 1,
            ContentTypeAlias = "article",
            ContentTypeKey = key,
            SchemaTypeName = "Article",
            IsEnabled = true
        };
        _repository.GetByContentTypeAlias("article").Returns(mapping);
        _repository.GetPropertyMappings(Arg.Any<int>()).Returns([]);
        CreateExporter().Export("article");

        var path = Path.Combine(_contentRoot, "uSync", "v18", "SchemeWeaverMappings", "article.config");
        var xml = File.ReadAllText(path).Replace(key.ToString(), key.ToString().ToUpperInvariant());
        File.WriteAllText(path, xml);

        CreateReporter().GetStatus("article").Should().Be(MappingDriftStatus.InSync);
    }

    [Fact]
    public void GetStatus_DiskDiffersFromDb_ReturnsContentDiffers()
    {
        // Export the "article -> Article" mapping...
        _repository.GetByContentTypeAlias("article").Returns(Mapping("article"));
        _repository.GetPropertyMappings(Arg.Any<int>()).Returns([]);
        CreateExporter().Export("article");

        // ...then the DB value changes (schema type) without re-export → drift.
        _repository.GetByContentTypeAlias("article").Returns(Mapping("article", schemaType: "NewsArticle"));

        CreateReporter().GetStatus("article").Should().Be(MappingDriftStatus.ContentDiffers);
    }

    [Fact]
    public void GetReport_OrphanConfigOnDisk_ReportedAsDiskOnly()
    {
        // Export a mapping, then remove it from the DB so only the file remains.
        _repository.GetByContentTypeAlias("ghost").Returns(Mapping("ghost"));
        _repository.GetPropertyMappings(Arg.Any<int>()).Returns([]);
        CreateExporter().Export("ghost");

        _repository.GetAll().Returns(Array.Empty<SchemaMapping>());

        var report = CreateReporter().GetReport();

        report.UsyncAvailable.Should().BeTrue();
        report.Items.Should().ContainSingle(i => i.ContentTypeAlias == "ghost" && i.Status == MappingDriftStatus.DiskOnly);
    }

    [Fact]
    public void GetReport_MixedStates_ClassifiesEach()
    {
        var inSync = Mapping("article");
        var dbOnly = Mapping("newsItem", "NewsArticle");
        dbOnly.Id = 2;

        // Export only "article" so it round-trips in-sync; "newsItem" is DB-only.
        _repository.GetByContentTypeAlias("article").Returns(inSync);
        _repository.GetPropertyMappings(Arg.Any<int>()).Returns([]);
        CreateExporter().Export("article");

        _repository.GetAll().Returns(new[] { inSync, dbOnly });

        var report = CreateReporter().GetReport();

        report.Items.Single(i => i.ContentTypeAlias == "article").Status.Should().Be(MappingDriftStatus.InSync);
        report.Items.Single(i => i.ContentTypeAlias == "newsItem").Status.Should().Be(MappingDriftStatus.DbOnly);
    }
}
