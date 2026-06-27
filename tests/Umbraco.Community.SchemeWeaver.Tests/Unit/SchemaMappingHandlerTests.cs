using System.Reflection;
using System.Xml.Linq;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Umbraco.Cms.Core.Cache;
using Umbraco.Cms.Core.Strings;
using Umbraco.Community.SchemeWeaver.Models.Entities;
using Umbraco.Community.SchemeWeaver.Persistence;
using Umbraco.Community.SchemeWeaver.uSync;
using uSync.BackOffice;
using uSync.BackOffice.Configuration;
using uSync.BackOffice.Services;
using uSync.BackOffice.SyncHandlers;
using uSync.Core;
using uSync.Core.Dependency;
using uSync.Core.Serialization;
using uSync.Core.Tracking;
using Xunit;

namespace Umbraco.Community.SchemeWeaver.Tests.Unit;

/// <summary>
/// Tests for <see cref="SchemaMappingHandler"/> — the uSync dashboard handler that
/// makes SchemeWeaver mappings participate in Import All / Export All. The handler
/// derives from uSync's <see cref="SyncHandlerRoot{TObject,TContainer}"/>, so the
/// tests construct it with mocked uSync services and the real serializer, then
/// exercise the handler-specific overrides (item source, naming) directly. A
/// separate round-trip test proves the on-disk file contract the handler relies on.
/// </summary>
public class SchemaMappingHandlerTests
{
    private readonly ISchemaMappingRepository _repository = Substitute.For<ISchemaMappingRepository>();
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly SchemaMappingHandler _sut;

    public SchemaMappingHandlerTests()
    {
        _scopeFactory = BuildScopeFactory(_repository);
        _sut = BuildHandler(_scopeFactory, _repository);
    }

    // === Handler metadata (dashboard wiring) ===

    [Fact]
    public void Handler_ExposesExpectedMetadata()
    {
        _sut.Alias.Should().Be("schemeWeaverMappingHandler");
        _sut.Name.Should().Be("SchemeWeaver Mappings");
        // Must match SchemeWeaverMappingPaths.MappingsFolderName so the dashboard
        // export lands where the export-on-save handler / first-boot importer read.
        _sut.DefaultFolder.Should().Be("SchemeWeaverMappings");
        // The serialiser's item type drives which serialiser uSync picks for the
        // handler — it must equal the shared item-type constant.
        _sut.TypeName.Should().Be(SchemeWeaverMappingConstants.ItemType);
        _sut.EntityType.Should().Be(SchemeWeaverMappingConstants.ItemType);
        _sut.Group.Should().Be("Settings");
    }

    // === Item source (export) ===

    [Fact]
    public async Task GetChildItems_ForRoot_ReturnsAllMappingsFromRepository()
    {
        var mappings = new List<SchemaMapping>
        {
            new() { Id = 1, ContentTypeAlias = "blogPost", SchemaTypeName = "BlogPosting" },
            new() { Id = 2, ContentTypeAlias = "newsArticle", SchemaTypeName = "NewsArticle" },
        };
        _repository.GetAll().Returns(mappings);

        var result = await InvokeGetChildItemsAsync(parent: null);

        result.Should().BeEquivalentTo(mappings);
        _repository.Received(1).GetAll();
    }

    [Fact]
    public async Task GetChildItems_ForNonRoot_ReturnsEmpty()
    {
        // Mappings are a flat list — a non-null parent has no children.
        var result = await InvokeGetChildItemsAsync(parent: new SchemaMapping { Id = 1, ContentTypeAlias = "blogPost" });

        result.Should().BeEmpty();
        _repository.DidNotReceive().GetAll();
    }

    [Fact]
    public void GetItemName_ReturnsContentTypeAlias()
    {
        var name = InvokeGetItemName(new SchemaMapping { ContentTypeAlias = "blogPost" });

        name.Should().Be("blogPost");
    }

    // === Round-trip file contract (export writes a file, import re-creates the mapping) ===

    [Fact]
    public async Task ExportThenImport_RoundTripsThroughTheMappingsFolder()
    {
        // Arrange: a content root with a uSync/v18 data folder so the path resolver
        // picks the current-convention version sub-folder.
        var contentRoot = Path.Combine(Path.GetTempPath(), "sw-usync-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(contentRoot, "uSync", "v18"));

        try
        {
            var serializer = new SchemaMappingSerializer(_scopeFactory, Substitute.For<ILogger<SchemaMappingSerializer>>());
            var fileWriter = new MappingFileWriter();

            var mapping = new SchemaMapping
            {
                Id = 1,
                ContentTypeAlias = "blogPost",
                ContentTypeKey = Guid.Parse("a1b2c3d4-e5f6-7890-abcd-ef1234567890"),
                SchemaTypeName = "BlogPosting",
                IsEnabled = true,
                IdOverride = "{url}#{type}",
            };
            var propertyMappings = new List<PropertyMapping>
            {
                new()
                {
                    Id = 10, SchemaMappingId = 1, SchemaPropertyName = "headline",
                    SourceType = "property", ContentTypePropertyAlias = "title", IsAutoMapped = true,
                },
            };
            _repository.GetPropertyMappings(mapping.Id).Returns(propertyMappings);

            // Act 1 — export: serialise and write to the SchemeWeaverMappings folder.
            var folder = SchemeWeaverMappingPaths.ResolveWriteFolder(contentRoot);
            var serialised = await serializer.SerializeAsync(mapping, new SyncSerializerOptions());
            serialised.Success.Should().BeTrue();
            fileWriter.Write(folder, mapping.ContentTypeAlias, serialised.Item!);

            // A flat {alias}.config file lands exactly where uSync's handler writes it.
            var expectedFile = Path.Combine(contentRoot, "uSync", "v18", "SchemeWeaverMappings", "blogPost.config");
            File.Exists(expectedFile).Should().BeTrue();

            // Act 2 — import: read the file back and deserialise into the repository.
            _repository.GetByContentTypeAlias("blogPost").Returns((SchemaMapping?)null);
            _repository.Save(Arg.Any<SchemaMapping>()).Returns(c => { var m = c.Arg<SchemaMapping>(); m.Id = 1; return m; });

            var savedProps = new List<PropertyMapping>();
            _repository.When(r => r.SavePropertyMappings(Arg.Any<int>(), Arg.Any<IEnumerable<PropertyMapping>>()))
                .Do(c => savedProps.AddRange(c.Arg<IEnumerable<PropertyMapping>>()));

            var loaded = XElement.Load(expectedFile);
            var imported = await serializer.DeserializeAsync(loaded, new SyncSerializerOptions());

            // Assert — the mapping is re-created with full fidelity.
            imported.Success.Should().BeTrue();
            imported.Item!.ContentTypeAlias.Should().Be("blogPost");
            imported.Item.ContentTypeKey.Should().Be(Guid.Parse("a1b2c3d4-e5f6-7890-abcd-ef1234567890"));
            imported.Item.SchemaTypeName.Should().Be("BlogPosting");
            imported.Item.IsEnabled.Should().BeTrue();
            imported.Item.IdOverride.Should().Be("{url}#{type}");

            savedProps.Should().ContainSingle();
            savedProps[0].SchemaPropertyName.Should().Be("headline");
            savedProps[0].ContentTypePropertyAlias.Should().Be("title");
            _repository.Received().Save(Arg.Any<SchemaMapping>());
        }
        finally
        {
            if (Directory.Exists(contentRoot))
                Directory.Delete(contentRoot, recursive: true);
        }
    }

    // === Construction helpers ===

    private static IServiceScopeFactory BuildScopeFactory(ISchemaMappingRepository repository)
    {
        var serviceProvider = Substitute.For<IServiceProvider>();
        serviceProvider.GetService(typeof(ISchemaMappingRepository)).Returns(repository);

        var scope = Substitute.For<IServiceScope>();
        scope.ServiceProvider.Returns(serviceProvider);

        var scopeFactory = Substitute.For<IServiceScopeFactory>();
        scopeFactory.CreateScope().Returns(scope);
        return scopeFactory;
    }

    private static SchemaMappingHandler BuildHandler(IServiceScopeFactory scopeFactory, ISchemaMappingRepository repository)
    {
        var serializer = new SchemaMappingSerializer(scopeFactory, Substitute.For<ILogger<SchemaMappingSerializer>>());

        var itemFactory = Substitute.For<ISyncItemFactory>();
        itemFactory.GetSerializers<SchemaMapping>().Returns(new List<ISyncSerializer<SchemaMapping>> { serializer });
        itemFactory.GetTrackers<SchemaMapping>().Returns(new List<ISyncTracker<SchemaMapping>>());
        itemFactory.GetCheckers<SchemaMapping>().Returns(new List<ISyncDependencyChecker<SchemaMapping>>());

        var config = Substitute.For<ISyncConfigService>();
        config.Settings.Returns(new uSyncSettings());
        config.GetFolders().Returns(Array.Empty<string>());
        config.GetDefaultSetSettings().Returns(new uSyncHandlerSetSettings());

        return new SchemaMappingHandler(
            Substitute.For<ILogger<SyncHandlerRoot<SchemaMapping, SchemaMapping>>>(),
            AppCaches.NoCache,
            Substitute.For<IShortStringHelper>(),
            Substitute.For<ISyncFileService>(),
            Substitute.For<ISyncEventService>(),
            config,
            itemFactory,
            scopeFactory);
    }

    private async Task<IEnumerable<SchemaMapping>> InvokeGetChildItemsAsync(SchemaMapping? parent)
    {
        var method = typeof(SchemaMappingHandler)
            .GetMethod("GetChildItemsAsync", BindingFlags.NonPublic | BindingFlags.Instance)!;
        var task = (Task<IEnumerable<SchemaMapping>>)method.Invoke(_sut, [parent])!;
        return await task;
    }

    private string InvokeGetItemName(SchemaMapping item)
    {
        var method = typeof(SchemaMappingHandler)
            .GetMethod("GetItemName", BindingFlags.NonPublic | BindingFlags.Instance)!;
        return (string)method.Invoke(_sut, [item])!;
    }
}
