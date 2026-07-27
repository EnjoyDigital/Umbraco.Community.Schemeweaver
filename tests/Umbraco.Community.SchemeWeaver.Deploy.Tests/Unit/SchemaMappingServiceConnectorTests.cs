using FluentAssertions;
using NSubstitute;
using Umbraco.Cms.Core;
using Umbraco.Cms.Core.Deploy;
using Umbraco.Cms.Core.Models;
using Umbraco.Community.SchemeWeaver.Deploy.Artifacts;
using Umbraco.Community.SchemeWeaver.Models.Entities;
using Umbraco.Deploy.Core;
using Xunit;

namespace Umbraco.Community.SchemeWeaver.Deploy.Tests.Unit;

public class SchemaMappingServiceConnectorTests
{
    private static readonly Guid Key = new("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");

    private static GuidUdi MappingUdi(Guid? key = null)
        => new(SchemeWeaverDeployConstants.MappingUdiEntityType, key ?? Key);

    // ----- GetArtifactAsync(udi) -----

    [Fact]
    public async Task GetArtifact_MapsAllScalarFields_AndSetsNameAlias()
    {
        var (connector, repository, _) = ConnectorTestHarness.Build();
        var mapping = ConnectorTestHarness.Mapping(key: Key);
        mapping.IdOverride = "{url}#custom";
        repository.GetAll().Returns(new[] { mapping });
        repository.GetPropertyMappings(mapping.Id).Returns(Array.Empty<PropertyMapping>());

        var artifact = await connector.GetArtifactAsync(MappingUdi(), new DictionaryCache());

        artifact.Should().NotBeNull();
        artifact!.Udi.Should().Be(MappingUdi());
        artifact.ContentTypeAlias.Should().Be("blogPost");
        artifact.ContentTypeKey.Should().Be(Key);
        artifact.SchemaTypeName.Should().Be("BlogPosting");
        artifact.IsEnabled.Should().BeTrue();
        artifact.IsInherited.Should().BeFalse();
        artifact.IdOverride.Should().Be("{url}#custom");
        artifact.Name.Should().Be("SchemeWeaver mapping: blogPost");
        artifact.Alias.Should().Be("blogPost");
    }

    [Fact]
    public async Task GetArtifact_MapsPropertyRows_OrderedById()
    {
        var (connector, repository, _) = ConnectorTestHarness.Build();
        var mapping = ConnectorTestHarness.Mapping(key: Key);
        repository.GetAll().Returns(new[] { mapping });
        // Repository fetch has no ORDER BY — return rows deliberately out of order.
        repository.GetPropertyMappings(mapping.Id).Returns(new[]
        {
            ConnectorTestHarness.Row(3, schemaProperty: "DatePublished"),
            ConnectorTestHarness.Row(1, schemaProperty: "Headline"),
            ConnectorTestHarness.Row(2, schemaProperty: "Description"),
        });

        var artifact = await connector.GetArtifactAsync(MappingUdi(), new DictionaryCache());

        artifact!.PropertyMappings.Select(r => r.SchemaPropertyName)
            .Should().ContainInOrder("Headline", "Description", "DatePublished");
    }

    [Fact]
    public async Task GetArtifact_NormalisesEmptyOptionalsToNull()
    {
        var (connector, repository, _) = ConnectorTestHarness.Build();
        var mapping = ConnectorTestHarness.Mapping(key: Key);
        mapping.IdOverride = "";
        repository.GetAll().Returns(new[] { mapping });
        var row = ConnectorTestHarness.Row(1);
        row.StaticValue = "";
        row.TransformType = "";
        repository.GetPropertyMappings(mapping.Id).Returns(new[] { row });

        var artifact = await connector.GetArtifactAsync(MappingUdi(), new DictionaryCache());

        artifact!.IdOverride.Should().BeNull();
        artifact.PropertyMappings[0].StaticValue.Should().BeNull();
        artifact.PropertyMappings[0].TransformType.Should().BeNull();
    }

    [Fact]
    public async Task GetArtifact_EmptyAndNullOptionals_ProduceIdenticalChecksums()
    {
        var (connector, repository, _) = ConnectorTestHarness.Build();
        var mapping = ConnectorTestHarness.Mapping(key: Key);
        repository.GetAll().Returns(new[] { mapping });

        var emptyRow = ConnectorTestHarness.Row(1);
        emptyRow.StaticValue = "";
        emptyRow.ResolverConfig = "";
        repository.GetPropertyMappings(mapping.Id).Returns(new[] { emptyRow });
        var fromEmpty = await connector.GetArtifactAsync(MappingUdi(), new DictionaryCache());

        var nullRow = ConnectorTestHarness.Row(1);
        nullRow.StaticValue = null;
        nullRow.ResolverConfig = null;
        repository.GetPropertyMappings(mapping.Id).Returns(new[] { nullRow });
        var fromNull = await connector.GetArtifactAsync(MappingUdi(), new DictionaryCache());

        fromEmpty!.Checksum.Should().Be(fromNull!.Checksum);
    }

    [Fact]
    public async Task GetArtifact_ReturnsNull_WhenMappingUnknown()
    {
        var (connector, repository, _) = ConnectorTestHarness.Build();
        repository.GetAll().Returns(Array.Empty<SchemaMapping>());

        (await connector.GetArtifactAsync(MappingUdi(), new DictionaryCache())).Should().BeNull();
    }

    [Fact]
    public async Task GetArtifact_ReturnsNull_WhenContentTypeNoLongerExists()
    {
        var (connector, repository, contentTypeService) = ConnectorTestHarness.Build();
        repository.GetAll().Returns(new[] { ConnectorTestHarness.Mapping(key: Key) });
        contentTypeService.Get(Key).Returns((IContentType?)null);

        (await connector.GetArtifactAsync(MappingUdi(), new DictionaryCache())).Should().BeNull();
    }

    [Fact]
    public async Task GetArtifact_ForEntityWithEmptyKey_Throws()
    {
        var (connector, _, _) = ConnectorTestHarness.Build();
        var mapping = ConnectorTestHarness.Mapping(key: Guid.Empty);

        var act = () => connector.GetArtifactAsync(mapping, new DictionaryCache());

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*empty ContentTypeKey*");
    }

    // ----- ProcessInitAsync -----

    [Fact]
    public async Task ProcessInit_FindsEntityByContentTypeKey()
    {
        var (connector, repository, _) = ConnectorTestHarness.Build();
        var mapping = ConnectorTestHarness.Mapping(key: Key);
        repository.GetAll().Returns(new[] { mapping });

        var state = await connector.ProcessInitAsync(
            new SchemaMappingArtifact(MappingUdi()), Substitute.For<IDeployContext>());

        state.Entity.Should().BeSameAs(mapping);
        state.NextPass.Should().Be(3);
    }

    [Fact]
    public async Task ProcessInit_FallsBackToAliasLookup_WhenKeyMissing()
    {
        var (connector, repository, _) = ConnectorTestHarness.Build();
        var recreated = ConnectorTestHarness.Mapping(alias: "blogPost", key: Guid.NewGuid());
        repository.GetAll().Returns(new[] { recreated });
        repository.GetByContentTypeAlias("blogPost").Returns(recreated);

        var state = await connector.ProcessInitAsync(
            new SchemaMappingArtifact(MappingUdi()) { ContentTypeAlias = "blogPost" },
            Substitute.For<IDeployContext>());

        state.Entity.Should().BeSameAs(recreated);
    }

    [Fact]
    public async Task ProcessInit_NullEntity_WhenNeitherMatches()
    {
        var (connector, repository, _) = ConnectorTestHarness.Build();
        repository.GetAll().Returns(Array.Empty<SchemaMapping>());
        repository.GetByContentTypeAlias(Arg.Any<string>()).Returns((SchemaMapping?)null);

        var state = await connector.ProcessInitAsync(
            new SchemaMappingArtifact(MappingUdi()) { ContentTypeAlias = "blogPost" },
            Substitute.For<IDeployContext>());

        state.Entity.Should().BeNull();
    }

    // ----- ProcessAsync -----

    private static SchemaMappingArtifact Artifact(string alias = "blogPost", Guid? key = null)
    {
        var udi = MappingUdi(key);
        return new SchemaMappingArtifact(udi)
        {
            ContentTypeAlias = alias,
            ContentTypeKey = udi.Guid,
            SchemaTypeName = "BlogPosting",
            IsEnabled = true,
            PropertyMappings = new List<PropertyMappingArtifact>
            {
                new() { SchemaPropertyName = "Headline", SourceType = "property", ContentTypePropertyAlias = "title" },
                new() { SchemaPropertyName = "Description", SourceType = "property", ContentTypePropertyAlias = "standfirst" },
            },
        };
    }

    [Fact]
    public async Task Process_CreatesNewMapping_WhenNoEntity()
    {
        var (connector, repository, _) = ConnectorTestHarness.Build();
        repository.GetByContentTypeAlias(Arg.Any<string>()).Returns((SchemaMapping?)null);
        SchemaMapping? savedMapping = null;
        repository.Save(Arg.Do<SchemaMapping>(m => savedMapping = m))
            .Returns(ci => { var m = ci.Arg<SchemaMapping>(); if (m.Id == 0) { m.Id = 42; } return m; });
        List<PropertyMapping>? savedRows = null;
        repository.When(r => r.SavePropertyMappings(42, Arg.Any<IEnumerable<PropertyMapping>>()))
            .Do(ci => savedRows = ci.Arg<IEnumerable<PropertyMapping>>().ToList());

        var artifact = Artifact();
        var state = ArtifactDeployState.Create<SchemaMappingArtifact, SchemaMapping>(artifact, null, connector, 3);
        await connector.ProcessAsync(state, Substitute.For<IDeployContext>(), 3);

        savedMapping.Should().NotBeNull();
        savedMapping!.ContentTypeAlias.Should().Be("blogPost");
        savedMapping.ContentTypeKey.Should().Be(Key);
        savedMapping.SchemaTypeName.Should().Be("BlogPosting");
        savedRows.Should().NotBeNull();
        savedRows!.Select(r => r.SchemaPropertyName).Should().ContainInOrder("Headline", "Description");
        savedRows.Should().OnlyContain(r => r.SchemaMappingId == 42);
        state.NextPass.Should().Be(-1);
    }

    [Fact]
    public async Task Process_UpdatesExistingMapping_KeepingItsId()
    {
        var (connector, repository, _) = ConnectorTestHarness.Build();
        var existing = ConnectorTestHarness.Mapping(id: 7, key: Key);
        existing.SchemaTypeName = "Article";
        repository.GetByContentTypeAlias("blogPost").Returns(existing);
        repository.Save(Arg.Any<SchemaMapping>()).Returns(ci => ci.Arg<SchemaMapping>());

        var state = ArtifactDeployState.Create<SchemaMappingArtifact, SchemaMapping>(Artifact(), existing, connector, 3);
        await connector.ProcessAsync(state, Substitute.For<IDeployContext>(), 3);

        existing.Id.Should().Be(7);
        existing.SchemaTypeName.Should().Be("BlogPosting");
        repository.DidNotReceive().Delete(Arg.Any<int>());
        repository.Received(1).SavePropertyMappings(7, Arg.Any<IEnumerable<PropertyMapping>>());
    }

    [Fact]
    public async Task Process_UpdatesAlias_WhenDocTypeRenamed()
    {
        // Same content type key, new alias at source: the key-matched row adopts the
        // new alias; no other row holds it, so nothing is deleted.
        var (connector, repository, _) = ConnectorTestHarness.Build();
        var existing = ConnectorTestHarness.Mapping(id: 7, alias: "oldAlias", key: Key);
        repository.GetByContentTypeAlias("blogPost").Returns((SchemaMapping?)null);
        repository.Save(Arg.Any<SchemaMapping>()).Returns(ci => ci.Arg<SchemaMapping>());

        var state = ArtifactDeployState.Create<SchemaMappingArtifact, SchemaMapping>(Artifact(), existing, connector, 3);
        await connector.ProcessAsync(state, Substitute.For<IDeployContext>(), 3);

        existing.ContentTypeAlias.Should().Be("blogPost");
        repository.DidNotReceive().Delete(Arg.Any<int>());
    }

    [Fact]
    public async Task Process_DeletesStaleAliasRow_WhenAliasHeldByDifferentKey()
    {
        // The incoming alias is owned by a row keyed to a doc type identity that no
        // longer holds it (recreated at source). Without the sweep, Save would hit
        // the unique alias index and fail the whole deployment.
        var (connector, repository, _) = ConnectorTestHarness.Build();
        var stale = ConnectorTestHarness.Mapping(id: 9, alias: "blogPost", key: Guid.NewGuid());
        repository.GetAll().Returns(new[] { stale });
        repository.Save(Arg.Any<SchemaMapping>()).Returns(ci => { var m = ci.Arg<SchemaMapping>(); if (m.Id == 0) { m.Id = 42; } return m; });

        var state = ArtifactDeployState.Create<SchemaMappingArtifact, SchemaMapping>(Artifact(), null, connector, 3);
        await connector.ProcessAsync(state, Substitute.For<IDeployContext>(), 3);

        repository.Received(1).Delete(9);
        repository.Received(1).Save(Arg.Is<SchemaMapping>(m => m.ContentTypeKey == Key));
    }

    [Fact]
    public async Task Process_CollisionSweep_IsCaseInsensitive()
    {
        // The unique alias index is case-insensitive on default-collation SQL Server,
        // so a stale row differing only in case must be swept too.
        var (connector, repository, _) = ConnectorTestHarness.Build();
        var stale = ConnectorTestHarness.Mapping(id: 9, alias: "BLOGPOST", key: Guid.NewGuid());
        repository.GetAll().Returns(new[] { stale });
        repository.Save(Arg.Any<SchemaMapping>()).Returns(ci => { var m = ci.Arg<SchemaMapping>(); if (m.Id == 0) { m.Id = 42; } return m; });

        var state = ArtifactDeployState.Create<SchemaMappingArtifact, SchemaMapping>(Artifact(), null, connector, 3);
        await connector.ProcessAsync(state, Substitute.For<IDeployContext>(), 3);

        repository.Received(1).Delete(9);
    }

    [Fact]
    public async Task KeyLookups_PreferLowestId_WhenRowsShareContentTypeKey()
    {
        // The unique index is on alias, not key: an orphaned old-alias row can share
        // a key with its recreated mapping. Lookups must be deterministic (lowest Id)
        // and ExpandRange must never yield the same UDI twice.
        var (connector, repository, _) = ConnectorTestHarness.Build();
        var older = ConnectorTestHarness.Mapping(id: 3, alias: "oldAlias", key: Key);
        var newer = ConnectorTestHarness.Mapping(id: 8, alias: "newAlias", key: Key);
        repository.GetAll().Returns(new[] { newer, older });
        repository.GetPropertyMappings(Arg.Any<int>()).Returns(Array.Empty<PropertyMapping>());

        var artifact = await connector.GetArtifactAsync(MappingUdi(), new DictionaryCache());
        artifact!.ContentTypeAlias.Should().Be("oldAlias");

        var range = new UdiRange(Udi.Create(SchemeWeaverDeployConstants.MappingUdiEntityType),
            Constants.DeploySelector.ThisAndDescendants);
        var udis = new List<GuidUdi>();
        await foreach (var udi in connector.ExpandRangeAsync(range))
        {
            udis.Add(udi);
        }

        udis.Should().ContainSingle().Which.Guid.Should().Be(Key);
    }

    // ----- Ranges -----

    [Fact]
    public async Task ExpandRange_Root_YieldsAllMappings_SkippingEmptyKeys()
    {
        var (connector, repository, _) = ConnectorTestHarness.Build();
        var good = ConnectorTestHarness.Mapping(id: 1, alias: "a", key: Key);
        var emptyKey = ConnectorTestHarness.Mapping(id: 2, alias: "b", key: Guid.Empty);
        repository.GetAll().Returns(new[] { good, emptyKey });

        var range = new UdiRange(Udi.Create(SchemeWeaverDeployConstants.MappingUdiEntityType),
            Constants.DeploySelector.ThisAndDescendants);
        var udis = new List<GuidUdi>();
        await foreach (var udi in connector.ExpandRangeAsync(range))
        {
            udis.Add(udi);
        }

        udis.Should().ContainSingle().Which.Guid.Should().Be(Key);
    }

    [Fact]
    public async Task GetRange_SidMinusOne_ReturnsOpenRange()
    {
        var (connector, _, _) = ConnectorTestHarness.Build();

        var range = await connector.GetRangeAsync(
            SchemeWeaverDeployConstants.MappingUdiEntityType, "-1", Constants.DeploySelector.ThisAndDescendants);

        range.Udi.IsRoot.Should().BeTrue();
        range.Name.Should().Be("All SchemeWeaver mappings");
    }

    [Fact]
    public async Task GetRange_SidMinusOne_InvalidSelector_Throws()
    {
        var (connector, _, _) = ConnectorTestHarness.Build();

        var act = () => connector.GetRangeAsync(
            SchemeWeaverDeployConstants.MappingUdiEntityType, "-1", "children");

        await act.Should().ThrowAsync<NotSupportedException>();
    }

    [Fact]
    public async Task GetRange_ForKnownMapping_ReturnsNamedRange()
    {
        var (connector, repository, _) = ConnectorTestHarness.Build();
        repository.GetAll().Returns(new[] { ConnectorTestHarness.Mapping(key: Key) });

        var range = await connector.GetRangeAsync(MappingUdi(), Constants.DeploySelector.This);

        range.Udi.Should().Be(MappingUdi());
        range.Name.Should().Be("blogPost");
    }

    // ----- IUniqueIdentifyingServiceConnector -----

    [Fact]
    public void GetUniqueIdentifier_ReturnsContentTypeAlias()
    {
        var (connector, _, _) = ConnectorTestHarness.Build();

        connector.GetUniqueIdentifier(Artifact("faqPage")).Should().Be("faqPage");
    }
}
