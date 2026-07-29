using FluentAssertions;
using NSubstitute;
using Umbraco.Cms.Core;
using Umbraco.Cms.Core.Deploy;
using Umbraco.Community.SchemeWeaver.Deploy.Artifacts;
using Umbraco.Community.SchemeWeaver.Models.Entities;
using Umbraco.Community.SchemeWeaver.Persistence;
using Umbraco.Deploy.Core;
using Xunit;

namespace Umbraco.Community.SchemeWeaver.Deploy.Tests.Unit;

/// <summary>
/// The cross-environment no-op gate: an artifact processed into a (fake) target
/// database and re-exported must come back field-identical with an identical
/// checksum — otherwise every deployment would report phantom differences forever.
/// </summary>
public class ArtifactRoundTripTests
{
    private static readonly Guid Key = new("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");

    /// <summary>
    /// Minimal in-memory repository with the real one's semantics: insert assigns
    /// ascending Ids; SavePropertyMappings is delete-all-then-reinsert (Ids assigned
    /// in list order); lookups match on alias/key.
    /// </summary>
    private sealed class InMemoryRepository : ISchemaMappingRepository
    {
        private readonly List<SchemaMapping> _mappings = new();
        private readonly List<PropertyMapping> _rows = new();
        private int _nextMappingId = 1;
        private int _nextRowId = 1;

        public IEnumerable<SchemaMapping> GetAll() => _mappings.Select(m => m.Clone()).ToList();

        public SchemaMapping? GetByContentTypeAlias(string contentTypeAlias)
            => _mappings.FirstOrDefault(m => m.ContentTypeAlias == contentTypeAlias)?.Clone();

        public SchemaMapping Save(SchemaMapping mapping)
        {
            if (mapping.Id == 0)
            {
                mapping.Id = _nextMappingId++;
                _mappings.Add(mapping.Clone());
            }
            else
            {
                _mappings.RemoveAll(m => m.Id == mapping.Id);
                _mappings.Add(mapping.Clone());
            }

            return mapping.Clone();
        }

        public void Delete(int id)
        {
            _rows.RemoveAll(r => r.SchemaMappingId == id);
            _mappings.RemoveAll(m => m.Id == id);
        }

        public IEnumerable<PropertyMapping> GetPropertyMappings(int schemaMappingId)
            => _rows.Where(r => r.SchemaMappingId == schemaMappingId).Select(r => r.Clone()).ToList();

        public IReadOnlyDictionary<int, List<PropertyMapping>> GetAllPropertyMappingsByMappingId()
            => _rows.GroupBy(r => r.SchemaMappingId)
                .ToDictionary(g => g.Key, g => g.Select(r => r.Clone()).ToList());

        public void SavePropertyMappings(int schemaMappingId, IEnumerable<PropertyMapping> mappings)
        {
            _rows.RemoveAll(r => r.SchemaMappingId == schemaMappingId);
            foreach (var row in mappings)
            {
                var clone = row.Clone();
                clone.Id = _nextRowId++;
                clone.SchemaMappingId = schemaMappingId;
                _rows.Add(clone);
            }
        }

        public IEnumerable<SchemaMapping> GetInheritedMappings()
            => _mappings.Where(m => m.IsInherited).Select(m => m.Clone()).ToList();

        public void ClearCache()
        {
        }
    }

    [Fact]
    public async Task Artifact_Process_GetArtifact_RoundTrip_PreservesFieldsAndChecksum()
    {
        var repository = new InMemoryRepository();
        var contentTypeService = Substitute.For<Umbraco.Cms.Core.Services.IContentTypeService>();
        contentTypeService.Get(Arg.Any<Guid>())
            .Returns(Substitute.For<Umbraco.Cms.Core.Models.IContentType>());
        var connector = ConnectorTestHarness.Build(repository, contentTypeService);

        var udi = new GuidUdi(SchemeWeaverDeployConstants.MappingUdiEntityType, Key);
        var original = new SchemaMappingArtifact(udi, new[]
        {
            new ArtifactDependency(
                new GuidUdi(Constants.UdiEntityType.DocumentType, Key), ordering: true, ArtifactDependencyMode.Exist),
        })
        {
            Name = "SchemeWeaver mapping: recipePage",
            Alias = "recipePage",
            ContentTypeAlias = "recipePage",
            ContentTypeKey = Key,
            SchemaTypeName = "Recipe",
            IsEnabled = true,
            IsInherited = true,
            IdOverride = "{url}#recipe",
            PropertyMappings = new List<PropertyMappingArtifact>
            {
                new()
                {
                    SchemaPropertyName = "Name",
                    SourceType = "property",
                    ContentTypePropertyAlias = "title",
                    IsAutoMapped = true,
                },
                new()
                {
                    SchemaPropertyName = "RecipeIngredient",
                    SourceType = "blockContent",
                    ContentTypePropertyAlias = "ingredients",
                    NestedSchemaTypeName = "HowToSupply",
                    ResolverConfig = /*lang=json,strict*/ """{"extractAs":"stringList","labelProperty":"ingredientName"}""",
                },
                new()
                {
                    SchemaPropertyName = "Author",
                    SourceType = "parent",
                    SourceContentTypeAlias = "authorPage",
                    TransformType = "complexType",
                    DynamicRootConfig = /*lang=json,strict*/ """{"originAlias":"Root","querySteps":[{"alias":"NearestAncestorOrSelf","unique":"9f36a2ac-0b8f-4f42-9137-1a534d64a1a5"}]}""",
                },
                new()
                {
                    SchemaPropertyName = "Publisher",
                    SourceType = "reference",
                    TargetPieceKey = "organization",
                },
                new()
                {
                    SchemaPropertyName = "Genre",
                    SourceType = "static",
                    StaticValue = "cooking",
                },
            },
        };

        // Deploy the artifact into the (empty) fake target...
        var state = ArtifactDeployState.Create<SchemaMappingArtifact, SchemaMapping>(original, null, connector, 3);
        await connector.ProcessAsync(state, Substitute.For<IDeployContext>(), 3);

        // ...then re-export from the target, as the target environment would.
        var roundTripped = await connector.GetArtifactAsync(udi, new DictionaryCache());

        roundTripped.Should().NotBeNull();
        roundTripped!.Udi.Should().Be(original.Udi);
        roundTripped.Name.Should().Be(original.Name);
        roundTripped.Alias.Should().Be(original.Alias);
        roundTripped.ContentTypeAlias.Should().Be(original.ContentTypeAlias);
        roundTripped.ContentTypeKey.Should().Be(original.ContentTypeKey);
        roundTripped.SchemaTypeName.Should().Be(original.SchemaTypeName);
        roundTripped.IsEnabled.Should().Be(original.IsEnabled);
        roundTripped.IsInherited.Should().Be(original.IsInherited);
        roundTripped.IdOverride.Should().Be(original.IdOverride);
        roundTripped.PropertyMappings.Should().BeEquivalentTo(
            original.PropertyMappings, options => options.WithStrictOrdering());
        roundTripped.Dependencies.Should().BeEquivalentTo(original.Dependencies);
        roundTripped.Checksum.Should().Be(original.Checksum);
    }

    [Fact]
    public async Task Reprocessing_TheSameArtifact_IsIdempotent()
    {
        var repository = new InMemoryRepository();
        var contentTypeService = Substitute.For<Umbraco.Cms.Core.Services.IContentTypeService>();
        contentTypeService.Get(Arg.Any<Guid>())
            .Returns(Substitute.For<Umbraco.Cms.Core.Models.IContentType>());
        var connector = ConnectorTestHarness.Build(repository, contentTypeService);

        var udi = new GuidUdi(SchemeWeaverDeployConstants.MappingUdiEntityType, Key);
        var artifact = new SchemaMappingArtifact(udi)
        {
            Name = "SchemeWeaver mapping: blogPost",
            Alias = "blogPost",
            ContentTypeAlias = "blogPost",
            ContentTypeKey = Key,
            SchemaTypeName = "BlogPosting",
            IsEnabled = true,
            PropertyMappings = new List<PropertyMappingArtifact>
            {
                new() { SchemaPropertyName = "Headline", SourceType = "property", ContentTypePropertyAlias = "title" },
            },
        };

        var first = ArtifactDeployState.Create<SchemaMappingArtifact, SchemaMapping>(artifact, null, connector, 3);
        await connector.ProcessAsync(first, Substitute.For<IDeployContext>(), 3);

        var existing = repository.GetAll().Single();
        var second = ArtifactDeployState.Create<SchemaMappingArtifact, SchemaMapping>(artifact, existing, connector, 3);
        await connector.ProcessAsync(second, Substitute.For<IDeployContext>(), 3);

        repository.GetAll().Should().ContainSingle();
        repository.GetPropertyMappings(existing.Id).Should().ContainSingle();
    }
}
