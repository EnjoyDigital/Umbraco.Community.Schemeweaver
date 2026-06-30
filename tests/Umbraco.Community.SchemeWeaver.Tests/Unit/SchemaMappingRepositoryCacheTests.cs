using FluentAssertions;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Umbraco.Cms.Infrastructure.Persistence;
using Umbraco.Cms.Infrastructure.Scoping;
using Umbraco.Community.SchemeWeaver.Models.Entities;
using Umbraco.Community.SchemeWeaver.Persistence;
using Xunit;

namespace Umbraco.Community.SchemeWeaver.Tests.Unit;

/// <summary>
/// The repository caches mapping reads so JSON-LD generation (hot on every publish/render) stops
/// hammering the DB — the cause of publishing stalling under the SQLite write lock. Reads serve
/// defensive clones; writes evict.
/// </summary>
public class SchemaMappingRepositoryCacheTests
{
    private readonly IScopeProvider _scopeProvider = Substitute.For<IScopeProvider>();
    private readonly IScope _scope = Substitute.For<IScope>();
    private readonly IUmbracoDatabase _db = Substitute.For<IUmbracoDatabase>();
    private readonly IMemoryCache _cache = new MemoryCache(new MemoryCacheOptions());

    public SchemaMappingRepositoryCacheTests()
    {
        _scope.Database.Returns(_db);
        _scopeProvider.CreateScope().ReturnsForAnyArgs(_scope);
    }

    private SchemaMappingRepository Create() =>
        new(_scopeProvider, _cache, NullLogger<SchemaMappingRepository>.Instance);

    [Fact]
    public void GetAll_SecondCall_IsServedFromCache_WithNoSecondDbFetch()
    {
        _db.Fetch<SchemaMapping>().Returns(new List<SchemaMapping> { new() { Id = 1, ContentTypeAlias = "a" } });
        var repo = Create();

        repo.GetAll().ToList();
        repo.GetAll().ToList();

        _db.Received(1).Fetch<SchemaMapping>();
    }

    [Fact]
    public void GetByContentTypeAlias_ReturnsDefensiveClone_MutationDoesNotCorruptCache()
    {
        _db.Fetch<SchemaMapping>()
            .Returns(new List<SchemaMapping> { new() { Id = 1, ContentTypeAlias = "a", SchemaTypeName = "Article" } });
        var repo = Create();

        var first = repo.GetByContentTypeAlias("a")!;
        first.SchemaTypeName = "MUTATED";

        var second = repo.GetByContentTypeAlias("a")!;
        second.SchemaTypeName.Should().Be("Article");
    }

    [Fact]
    public void Save_EvictsCache_SoNextReadReFetches()
    {
        _db.Fetch<SchemaMapping>().Returns(new List<SchemaMapping> { new() { Id = 1, ContentTypeAlias = "a" } });
        var repo = Create();

        repo.GetAll().ToList();                                       // db fetch #1 (populates cache)
        repo.Save(new SchemaMapping { Id = 1, ContentTypeAlias = "a" }); // evicts
        repo.GetAll().ToList();                                       // db fetch #2

        _db.Received(2).Fetch<SchemaMapping>();
    }

    [Fact]
    public void Delete_EvictsCache_SoNextReadReFetches()
    {
        _db.Fetch<SchemaMapping>().Returns(new List<SchemaMapping> { new() { Id = 1, ContentTypeAlias = "a" } });
        var repo = Create();

        repo.GetAll().ToList();   // fetch #1
        repo.Delete(1);           // evicts
        repo.GetAll().ToList();   // fetch #2

        _db.Received(2).Fetch<SchemaMapping>();
    }

    [Fact]
    public void GetPropertyMappings_SecondCall_IsServedFromCache()
    {
        _db.Fetch<PropertyMapping>().Returns(new List<PropertyMapping>
        {
            new() { Id = 1, SchemaMappingId = 7, SchemaPropertyName = "Headline" },
        });
        var repo = Create();

        repo.GetPropertyMappings(7).ToList();
        var second = repo.GetPropertyMappings(7).ToList();

        _db.Received(1).Fetch<PropertyMapping>();
        second.Should().ContainSingle(p => p.SchemaPropertyName == "Headline");
    }
}
