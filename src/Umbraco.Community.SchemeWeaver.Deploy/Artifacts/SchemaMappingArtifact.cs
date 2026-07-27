using Umbraco.Cms.Core;
using Umbraco.Cms.Core.Deploy;
using Umbraco.Deploy.Infrastructure.Artifacts;

namespace Umbraco.Community.SchemeWeaver.Deploy.Artifacts;

/// <summary>
/// Deploy artifact for a SchemeWeaver schema mapping, serialised to a
/// <c>schemeweaver-mapping__{contentTypeKey}.uda</c> file in the Deploy revision folder.
/// </summary>
/// <remarks>
/// <para>
/// WIRE CONTRACT: Deploy embeds this type's assembly-qualified name (<c>__type</c>)
/// in every serialised .uda file and includes it in the checksum. Renaming or moving
/// this class (or the assembly) invalidates every artifact in every consumer's
/// source control — never do it after a release.
/// </para>
/// <para>
/// <see cref="PropertyMappingArtifact.ResolverConfig"/> and
/// <see cref="PropertyMappingArtifact.DynamicRootConfig"/> are carried as opaque JSON
/// strings, exactly like the uSync addon: the blobs are owned by the picker/block UI,
/// content keys inside them are environment-stable in a Deploy-managed estate, and a
/// missing referenced node degrades gracefully at render time. No content dependencies
/// are declared for them — a schema-phase dependency on content could never be
/// satisfied during a schema deployment and would fail it outright.
/// </para>
/// </remarks>
public class SchemaMappingArtifact : DeployArtifactBase<GuidUdi>
{
    public SchemaMappingArtifact(GuidUdi udi, IEnumerable<ArtifactDependency>? dependencies = null)
        : base(udi, dependencies)
    {
    }

    public string ContentTypeAlias { get; set; } = string.Empty;

    /// <summary>
    /// Always equal to <c>Udi.Guid</c>; kept as an explicit property for .uda
    /// readability and parity with the uSync file format.
    /// </summary>
    public Guid ContentTypeKey { get; set; }

    public string SchemaTypeName { get; set; } = string.Empty;

    public bool IsEnabled { get; set; }

    public bool IsInherited { get; set; }

    public string? IdOverride { get; set; }

    /// <summary>Rows in stored order (ordered by row Id — insertion order). Order is load-bearing.</summary>
    public List<PropertyMappingArtifact> PropertyMappings { get; set; } = new();
}
