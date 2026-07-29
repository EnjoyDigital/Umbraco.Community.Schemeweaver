using Umbraco.Cms.Core;
using Umbraco.Community.SchemeWeaver.Models.Entities;

namespace Umbraco.Community.SchemeWeaver.Deploy;

public static class SchemaMappingUdiExtensions
{
    /// <summary>
    /// A <see cref="SchemaMapping"/> has no GUID of its own; the mapped content
    /// type's key is the stable cross-environment identity (mapping ↔ doc type is
    /// 1:1 via the unique <c>ContentTypeAlias</c> index), so it seeds the UDI.
    /// </summary>
    public static GuidUdi GetUdi(this SchemaMapping mapping)
        => (GuidUdi)new GuidUdi(SchemeWeaverDeployConstants.MappingUdiEntityType, mapping.ContentTypeKey).EnsureClosed();
}
