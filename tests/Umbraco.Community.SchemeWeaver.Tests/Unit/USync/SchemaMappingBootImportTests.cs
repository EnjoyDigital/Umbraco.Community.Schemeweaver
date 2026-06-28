using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;
using Umbraco.Cms.Core.Notifications;
using Umbraco.Community.SchemeWeaver;
using Umbraco.Community.SchemeWeaver.Models.Entities;
using Umbraco.Community.SchemeWeaver.Persistence;
using Umbraco.Community.SchemeWeaver.uSync;
using uSync.Core.Serialization;
using Xunit;

namespace Umbraco.Community.SchemeWeaver.Tests.Unit.USync;

/// <summary>
/// Exercises the <see cref="BootImportMode"/> branching of the boot importer using a real
/// serializer over a temp uSync folder and a stubbed repository. The serializer calls
/// <c>repository.Save</c> for each imported file, so Save-call counts reveal which files the
/// mode actually imported.
/// </summary>
public class SchemaMappingBootImportTests : IDisposable
{
    private readonly string _contentRoot = Path.Combine(Path.GetTempPath(), "sw-boot-" + Guid.NewGuid().ToString("N"));
    private readonly ISchemaMappingRepository _repository = Substitute.For<ISchemaMappingRepository>();
    private readonly IHostEnvironment _hostEnvironment = Substitute.For<IHostEnvironment>();
    private readonly SyncSerializerCollection _serializers;
    private readonly IServiceScopeFactory _scopeFactory;

    public SchemaMappingBootImportTests()
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

        _repository.GetPropertyMappings(Arg.Any<int>()).Returns([]);
        _repository.Save(Arg.Any<SchemaMapping>()).Returns(ci =>
        {
            var m = ci.Arg<SchemaMapping>();
            if (m.Id == 0) m.Id = 1;
            return m;
        });

        // Seed a committed config on disk (article -> Article).
        _repository.GetByContentTypeAlias("article").Returns(new SchemaMapping
        {
            Id = 1,
            ContentTypeAlias = "article",
            ContentTypeKey = Guid.Parse("11111111-1111-1111-1111-111111111111"),
            SchemaTypeName = "Article",
            IsEnabled = true
        });
        new USyncMappingExporter(_serializers, _scopeFactory, _hostEnvironment,
            new MappingFileWriter(), Substitute.For<ILogger<USyncMappingExporter>>()).Export("article");

        // After seeding the file, make the serializer treat an import as a fresh create so that
        // when a mode DOES import, it actually persists (uSync short-circuits "no change"
        // imports). Whether an import is attempted at all is what the mode controls and what
        // these tests assert.
        _repository.GetByContentTypeAlias("article").Returns((SchemaMapping?)null);
        _repository.ClearReceivedCalls();
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_contentRoot)) Directory.Delete(_contentRoot, recursive: true); } catch { /* best effort */ }
    }

    private SchemaMappingImportNotificationHandler CreateHandler(BootImportMode mode)
        => new(_serializers, _scopeFactory, _hostEnvironment,
            Options.Create(new SchemeWeaverOptions { USyncBootImport = mode }),
            Substitute.For<ILogger<SchemaMappingImportNotificationHandler>>());

    private async Task RunAsync(BootImportMode mode)
        => await CreateHandler(mode).HandleAsync(new UmbracoApplicationStartedNotification(false), CancellationToken.None);

    private SchemaMapping Existing(string alias) => new()
    {
        Id = 1,
        ContentTypeAlias = alias,
        ContentTypeKey = Guid.Parse("11111111-1111-1111-1111-111111111111"),
        SchemaTypeName = "Article",
        IsEnabled = true
    };

    [Fact]
    public async Task Off_EmptyDb_ImportsConfig()
    {
        _repository.GetAll().Returns(Array.Empty<SchemaMapping>());

        await RunAsync(BootImportMode.Off);

        _repository.Received().Save(Arg.Is<SchemaMapping>(m => m.ContentTypeAlias == "article"));
    }

    [Fact]
    public async Task Off_PopulatedDb_SkipsImport()
    {
        _repository.GetAll().Returns(new[] { Existing("article") });

        await RunAsync(BootImportMode.Off);

        // First-boot-only: a populated DB is never re-imported, so backoffice edits survive.
        _repository.DidNotReceive().Save(Arg.Any<SchemaMapping>());
    }

    [Fact]
    public async Task Seed_PopulatedDb_SkipsExistingAlias()
    {
        _repository.GetAll().Returns(new[] { Existing("article") });

        await RunAsync(BootImportMode.Seed);

        // Create-missing only: the committed config for an existing alias is left untouched.
        _repository.DidNotReceive().Save(Arg.Any<SchemaMapping>());
    }

    [Fact]
    public async Task Seed_EmptyDb_ImportsConfig()
    {
        _repository.GetAll().Returns(Array.Empty<SchemaMapping>());

        await RunAsync(BootImportMode.Seed);

        _repository.Received().Save(Arg.Is<SchemaMapping>(m => m.ContentTypeAlias == "article"));
    }

    [Fact]
    public async Task Upsert_PopulatedDb_ReimportsConfig()
    {
        // DB holds a stale value (NewsArticle) while disk says Article — disk must win.
        var stale = Existing("article");
        stale.SchemaTypeName = "NewsArticle";
        _repository.GetAll().Returns(new[] { stale });

        await RunAsync(BootImportMode.Upsert);

        // Disk wins: the differing config is re-imported even though the DB already has the mapping.
        _repository.Received().Save(Arg.Is<SchemaMapping>(m => m.ContentTypeAlias == "article"));
    }
}
