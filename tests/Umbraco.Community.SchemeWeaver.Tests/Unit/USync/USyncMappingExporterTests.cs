using System.Xml.Linq;
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

public class USyncMappingExporterTests : IDisposable
{
    private readonly string _contentRoot = Path.Join(Path.GetTempPath(), "sw-export-" + Guid.NewGuid().ToString("N"));
    private readonly ISchemaMappingRepository _repository = Substitute.For<ISchemaMappingRepository>();
    private readonly IHostEnvironment _hostEnvironment = Substitute.For<IHostEnvironment>();

    public USyncMappingExporterTests()
    {
        Directory.CreateDirectory(_contentRoot);
        _hostEnvironment.ContentRootPath.Returns(_contentRoot);
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_contentRoot)) Directory.Delete(_contentRoot, recursive: true); } catch { /* best effort */ }
    }

    private (SyncSerializerCollection Serializers, IServiceScopeFactory ScopeFactory) BuildSerializer()
    {
        var serviceProvider = Substitute.For<IServiceProvider>();
        serviceProvider.GetService(typeof(ISchemaMappingRepository)).Returns(_repository);
        var scope = Substitute.For<IServiceScope>();
        scope.ServiceProvider.Returns(serviceProvider);
        var scopeFactory = Substitute.For<IServiceScopeFactory>();
        scopeFactory.CreateScope().Returns(scope);

        var serializer = new SchemaMappingSerializer(scopeFactory, Substitute.For<ILogger<SchemaMappingSerializer>>());
        return (new SyncSerializerCollection(() => new[] { serializer }), scopeFactory);
    }

    private static SchemaMapping Mapping(int id, string alias) => new()
    {
        Id = id,
        ContentTypeAlias = alias,
        ContentTypeKey = Guid.NewGuid(),
        SchemaTypeName = "Article",
        IsEnabled = true
    };

    [Fact]
    public void Export_All_WritesEveryMapping()
    {
        _repository.GetAll().Returns(new[] { Mapping(1, "article"), Mapping(2, "newsItem") });
        _repository.GetPropertyMappings(Arg.Any<int>()).Returns([]);
        var (serializers, scopeFactory) = BuildSerializer();
        var exporter = new USyncMappingExporter(serializers, scopeFactory, _hostEnvironment,
            new MappingFileWriter(), Substitute.For<ILogger<USyncMappingExporter>>());

        var result = exporter.Export();

        result.UsyncAvailable.Should().BeTrue();
        result.Items.Should().HaveCount(2);
        result.Items.Should().OnlyContain(i => i.Written);
        File.Exists(Path.Join(result.Folder!, "article.config")).Should().BeTrue();
        File.Exists(Path.Join(result.Folder!, "newsItem.config")).Should().BeTrue();
    }

    [Fact]
    public void Export_SingleAlias_WritesOnlyThatMapping()
    {
        _repository.GetByContentTypeAlias("article").Returns(Mapping(1, "article"));
        _repository.GetPropertyMappings(Arg.Any<int>()).Returns([]);
        var (serializers, scopeFactory) = BuildSerializer();
        var exporter = new USyncMappingExporter(serializers, scopeFactory, _hostEnvironment,
            new MappingFileWriter(), Substitute.For<ILogger<USyncMappingExporter>>());

        var result = exporter.Export("article");

        result.Items.Should().ContainSingle(i => i.Alias == "article" && i.Written);
        File.Exists(Path.Join(result.Folder!, "article.config")).Should().BeTrue();
    }

    [Fact]
    public void Export_ReadOnlyRoot_ReportsFailure_DoesNotThrow()
    {
        _repository.GetAll().Returns(new[] { Mapping(1, "article") });
        _repository.GetPropertyMappings(Arg.Any<int>()).Returns([]);
        var (serializers, scopeFactory) = BuildSerializer();

        var throwingWriter = Substitute.For<IMappingFileWriter>();
        throwingWriter.When(w => w.Write(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<XElement>()))
            .Do(_ => throw new UnauthorizedAccessException("read-only content root"));

        var exporter = new USyncMappingExporter(serializers, scopeFactory, _hostEnvironment,
            throwingWriter, Substitute.For<ILogger<USyncMappingExporter>>());

        var act = () => exporter.Export();

        var result = act.Should().NotThrow().Subject;
        result.Items.Should().ContainSingle(i => !i.Written && i.Error != null);
    }
}

public class NullMappingSeamTests
{
    [Fact]
    public void NullDriftReporter_ReportsUnavailable()
    {
        var reporter = new NullMappingDriftReporter();

        reporter.IsAvailable.Should().BeFalse();
        reporter.GetStatus("anything").Should().Be(MappingDriftStatus.USyncUnavailable);
        reporter.GetReport().UsyncAvailable.Should().BeFalse();
        reporter.GetReport().Items.Should().BeEmpty();
    }

    [Fact]
    public void NullExporter_ReportsUnavailable()
    {
        var exporter = new NullMappingExporter();

        exporter.IsAvailable.Should().BeFalse();
        var result = exporter.Export();
        result.UsyncAvailable.Should().BeFalse();
        result.Items.Should().BeEmpty();
    }
}
