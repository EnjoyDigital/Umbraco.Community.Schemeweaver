using Microsoft.Extensions.DependencyInjection;
using Umbraco.Cms.Core.Composing;
using Umbraco.Cms.Core.DeliveryApi;
using Umbraco.Cms.Core.DependencyInjection;
using Umbraco.Cms.Core.Notifications;
using Umbraco.Community.SchemeWeaver;
using Umbraco.Community.SchemeWeaver.DeliveryApi;
using Umbraco.Community.SchemeWeaver.Graph;
using Umbraco.Community.SchemeWeaver.Graph.Pieces;
using Umbraco.Community.SchemeWeaver.Notifications;
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
        builder.Services.AddOptions<SchemeWeaverOptions>()
            .BindConfiguration("SchemeWeaver");

        builder.Services.AddControllers()
            .AddApplicationPart(typeof(SchemeWeaverComposer).Assembly);

        builder.Services.AddSingleton<ISchemaTypeRegistry, SchemaTypeRegistry>();
        builder.Services.AddScoped<ISchemaMappingRepository, SchemaMappingRepository>();
        builder.Services.AddScoped<ISchemeWeaverService, SchemeWeaverService>();
        builder.Services.AddScoped<ISchemaAutoMapper, SchemaAutoMapper>();
        builder.Services.AddScoped<IJsonLdGenerator, JsonLdGenerator>();
        builder.Services.AddScoped<IContentTypeGenerator, ContentTypeGenerator>();

        // JSON-LD blocks are cached in-process, keyed on (contentKey, culture). Singleton
        // so the per-content CancellationTokenSource dictionary persists across requests;
        // the generator itself is resolved per-call via IServiceScopeFactory because it's
        // scoped.
        builder.Services.AddSingleton<IJsonLdBlocksProvider, JsonLdBlocksProvider>();

        // Existing index-time pipeline. Refactored to delegate to the provider so the
        // cached blocks drive both the Examine index field and the dedicated endpoint.
        builder.Services.AddSingleton<IContentIndexHandler, SchemaJsonLdContentIndexHandler>();

        // Cache invalidation. Each handler evicts the target content + all descendants —
        // inherited schemas ripple from ancestor to every descendant, so touching any node
        // dirties the cache for the subtree.
        builder.AddNotificationHandler<ContentPublishedNotification, InvalidateJsonLdCacheOnPublish>();
        builder.AddNotificationHandler<ContentUnpublishedNotification, InvalidateJsonLdCacheOnUnpublish>();
        builder.AddNotificationHandler<ContentMovedNotification, InvalidateJsonLdCacheOnMove>();
        builder.AddNotificationHandler<ContentMovedToRecycleBinNotification, InvalidateJsonLdCacheOnMove>();
        builder.AddNotificationHandler<ContentDeletedNotification, InvalidateJsonLdCacheOnDelete>();

        // Property value resolvers — extensible via DI
        builder.Services.AddScoped<IPropertyValueResolver, DefaultPropertyValueResolver>();
        builder.Services.AddScoped<IPropertyValueResolver, MediaPickerResolver>();
        builder.Services.AddScoped<IPropertyValueResolver, RichTextResolver>();
        builder.Services.AddScoped<IPropertyValueResolver, ContentPickerResolver>();
        builder.Services.AddScoped<IPropertyValueResolver, BlockContentResolver>();
        builder.Services.AddScoped<IPropertyValueResolver, BuiltInPropertyResolver>();
        builder.Services.AddScoped<IPropertyValueResolver, DateTimeResolver>();
        builder.Services.AddScoped<IPropertyValueResolver, NumericResolver>();
        builder.Services.AddScoped<IPropertyValueResolver, BooleanResolver>();
        builder.Services.AddScoped<IPropertyValueResolver, TagsResolver>();
        builder.Services.AddScoped<IPropertyValueResolver, MultipleTextStringResolver>();
        builder.Services.AddScoped<IPropertyValueResolver, DropdownListResolver>();
        builder.Services.AddScoped<IPropertyValueResolver, ColorPickerResolver>();
        builder.Services.AddScoped<IPropertyValueResolver, MultiUrlPickerResolver>();
        builder.Services.AddScoped<IPropertyValueResolverFactory, PropertyValueResolverFactory>();

        // Graph (pieces) model (Yoast-style).
        builder.Services.AddScoped<IGraphGenerator, GraphGenerator>();
        builder.Services.AddScoped<ISiteSettingsResolver, SiteSettingsResolver>();

        // Built-in pieces. Order is set on each piece (100-spaced) so custom
        // pieces can slot between them; registration order here doesn't matter.
        builder.Services.AddSchemeWeaverGraphPiece<OrganizationPiece>();
        builder.Services.AddSchemeWeaverGraphPiece<WebSitePiece>();
        builder.Services.AddSchemeWeaverGraphPiece<MainEntityPiece>();
        builder.Services.AddSchemeWeaverGraphPiece<BreadcrumbListPiece>();
        builder.Services.AddSchemeWeaverGraphPiece<PrimaryImagePiece>();
    }
}
