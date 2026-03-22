using Microsoft.Extensions.DependencyInjection;
using Umbraco.Cms.Core.Composing;
using Umbraco.Cms.Core.DeliveryApi;
using Umbraco.Cms.Core.DependencyInjection;
using Umbraco.Community.SchemeWeaver.DeliveryApi;
using Umbraco.Community.SchemeWeaver.Persistence;
using Umbraco.Community.SchemeWeaver.Services;
using Umbraco.Community.SchemeWeaver.Services.Resolvers;

namespace Umbraco.Community.SchemeWeaver.Composing;

/// <summary>
/// Registers SchemeWeaver services and controllers with Umbraco.
/// </summary>
public class SchemeWeaverComposer : IComposer
{
    public void Compose(IUmbracoBuilder builder)
    {
        builder.Services.AddControllers()
            .AddApplicationPart(typeof(SchemeWeaverComposer).Assembly);

        builder.Services.AddSingleton<ISchemaTypeRegistry, SchemaTypeRegistry>();
        builder.Services.AddScoped<ISchemaMappingRepository, SchemaMappingRepository>();
        builder.Services.AddScoped<ISchemeWeaverService, SchemeWeaverService>();
        builder.Services.AddScoped<ISchemaAutoMapper, SchemaAutoMapper>();
        builder.Services.AddScoped<IJsonLdGenerator, JsonLdGenerator>();
        builder.Services.AddScoped<IContentTypeGenerator, ContentTypeGenerator>();
        builder.Services.AddSingleton<IContentIndexHandler, SchemaJsonLdContentIndexHandler>();

        // Property value resolvers — extensible via DI
        builder.Services.AddScoped<IPropertyValueResolver, DefaultPropertyValueResolver>();
        builder.Services.AddScoped<IPropertyValueResolver, MediaPickerResolver>();
        builder.Services.AddScoped<IPropertyValueResolver, RichTextResolver>();
        builder.Services.AddScoped<IPropertyValueResolver, ContentPickerResolver>();
        builder.Services.AddScoped<IPropertyValueResolver, BlockContentResolver>();
        builder.Services.AddScoped<IPropertyValueResolver, BuiltInPropertyResolver>();
        builder.Services.AddScoped<IPropertyValueResolverFactory, PropertyValueResolverFactory>();
    }
}
