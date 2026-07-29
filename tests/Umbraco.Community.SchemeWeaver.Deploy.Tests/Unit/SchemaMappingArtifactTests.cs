using System.Globalization;
using FluentAssertions;
using NSubstitute;
using Umbraco.Cms.Core;
using Umbraco.Cms.Core.Deploy;
using Umbraco.Community.SchemeWeaver.Deploy.Artifacts;
using Umbraco.Deploy.Core;
using Umbraco.Community.SchemeWeaver.Models.Entities;
using Xunit;

namespace Umbraco.Community.SchemeWeaver.Deploy.Tests.Unit;

public class SchemaMappingArtifactTests
{
    private static readonly Guid Key = new("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");

    private static SchemaMappingArtifact BuildArtifact(
        string alias = "blogPost",
        string schemaType = "BlogPosting",
        bool isEnabled = true,
        bool isInherited = false,
        string? idOverride = null,
        IEnumerable<PropertyMappingArtifact>? rows = null)
    {
        var udi = new GuidUdi(SchemeWeaverDeployConstants.MappingUdiEntityType, Key);
        return new SchemaMappingArtifact(udi, new[]
        {
            new ArtifactDependency(
                new GuidUdi(Constants.UdiEntityType.DocumentType, Key), ordering: true, ArtifactDependencyMode.Exist),
        })
        {
            Name = $"SchemeWeaver mapping: {alias}",
            Alias = alias,
            ContentTypeAlias = alias,
            ContentTypeKey = Key,
            SchemaTypeName = schemaType,
            IsEnabled = isEnabled,
            IsInherited = isInherited,
            IdOverride = idOverride,
            PropertyMappings = (rows ?? new[] { DefaultRow() }).ToList(),
        };
    }

    private static PropertyMappingArtifact DefaultRow(string schemaProperty = "Headline", string? alias = "title") => new()
    {
        SchemaPropertyName = schemaProperty,
        SourceType = "property",
        ContentTypePropertyAlias = alias,
        IsAutoMapped = false,
    };

    [Fact]
    public void Checksum_IsStable_ForIdenticalData()
    {
        BuildArtifact().Checksum.Should().Be(BuildArtifact().Checksum);
    }

    [Fact]
    public void Checksum_Changes_WhenScalarFieldChanges()
    {
        var baseline = BuildArtifact().Checksum;

        BuildArtifact(schemaType: "Article").Checksum.Should().NotBe(baseline);
        BuildArtifact(isEnabled: false).Checksum.Should().NotBe(baseline);
        BuildArtifact(isInherited: true).Checksum.Should().NotBe(baseline);
        BuildArtifact(idOverride: "{url}#custom").Checksum.Should().NotBe(baseline);
    }

    [Fact]
    public void Checksum_Changes_WhenPropertyRowChanges()
    {
        var baseline = BuildArtifact().Checksum;

        BuildArtifact(rows: new[] { DefaultRow("Description") }).Checksum.Should().NotBe(baseline);
        BuildArtifact(rows: new[] { DefaultRow(), DefaultRow("Description", "standfirst") }).Checksum
            .Should().NotBe(baseline);
        BuildArtifact(rows: Array.Empty<PropertyMappingArtifact>()).Checksum.Should().NotBe(baseline);
    }

    [Fact]
    public void Checksum_Changes_WhenRowOrderChanges()
    {
        // Row order is load-bearing (resolvers emit values in row order), so a
        // reorder is a real difference and MUST register as one.
        var rowA = DefaultRow("Headline", "title");
        var rowB = DefaultRow("Description", "standfirst");

        BuildArtifact(rows: new[] { rowA, rowB }).Checksum.Should()
            .NotBe(BuildArtifact(rows: new[] { rowB, rowA }).Checksum);
    }

    [Fact]
    public void Checksum_IsCultureInvariant()
    {
        var original = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("en-US");
            var enChecksum = BuildArtifact().Checksum;

            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("de-DE");
            var deChecksum = BuildArtifact().Checksum;

            deChecksum.Should().Be(enChecksum);
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }

    [Fact]
    public void ArtifactTypeName_IsPinned()
    {
        // Deploy embeds the artifact's assembly + full type name in every .uda file
        // and includes it in the checksum: the name is a permanent wire contract.
        // If this test fails you are breaking every existing artifact in every
        // consumer's source control — don't.
        var type = typeof(SchemaMappingArtifact);
        type.Assembly.GetName().Name.Should().Be("Umbraco.Community.SchemeWeaver.Deploy");
        type.FullName.Should().Be("Umbraco.Community.SchemeWeaver.Deploy.Artifacts.SchemaMappingArtifact");
    }

    [Fact]
    public void Dependencies_ContainDocumentTypeUdi_WithExistModeAndOrdering()
    {
        var dependency = BuildArtifact().Dependencies.Should().ContainSingle().Subject;

        dependency.Udi.Should().Be(new GuidUdi(Constants.UdiEntityType.DocumentType, Key));
        dependency.Ordering.Should().BeTrue();
        dependency.Mode.Should().Be(ArtifactDependencyMode.Exist);
    }

    [Fact]
    public void Udi_UsesSchemeweaverMappingEntityType_AndContentTypeKey()
    {
        var udi = BuildArtifact().Udi;

        udi.EntityType.Should().Be("schemeweaver-mapping");
        udi.Guid.Should().Be(Key);
    }
}
