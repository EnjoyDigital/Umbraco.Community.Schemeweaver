using Microsoft.Extensions.Logging.Abstractions;
using Umbraco.Community.SchemeWeaver.Seo.Templates;
using Umbraco.Community.SchemeWeaver.Services;

namespace Umbraco.Community.SchemeWeaver.Tests.Unit.Seo;

internal static class SeoTestSupport
{
    private static readonly Lazy<ISchemaTypeRegistry> RegistryInstance = new(() =>
    {
        var registry = new SchemaTypeRegistry();
        registry.EnsureInitialised();
        return registry;
    });

    private static readonly Lazy<SeoTemplateCatalogue> CatalogueInstance = new(() =>
        new SeoTemplateCatalogue(Registry, NullLogger<SeoTemplateCatalogue>.Instance));

    public static ISchemaTypeRegistry Registry => RegistryInstance.Value;

    public static SeoTemplateCatalogue Catalogue => CatalogueInstance.Value;
}
