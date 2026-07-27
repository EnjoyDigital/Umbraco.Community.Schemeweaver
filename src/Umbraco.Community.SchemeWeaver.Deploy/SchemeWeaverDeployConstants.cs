namespace Umbraco.Community.SchemeWeaver.Deploy;

public static class SchemeWeaverDeployConstants
{
    /// <summary>
    /// UDI entity type for a SchemeWeaver schema mapping
    /// (<c>umb://schemeweaver-mapping/{contentTypeKey}</c>). Must match the
    /// <c>UdiDefinition</c> on <see cref="Connectors.SchemaMappingServiceConnector"/>.
    /// </summary>
    public const string MappingUdiEntityType = "schemeweaver-mapping";
}
