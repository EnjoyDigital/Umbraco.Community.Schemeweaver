using System.Xml.Linq;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Umbraco.Cms.Core.Cache;
using Umbraco.Cms.Core.Strings;
using Umbraco.Community.SchemeWeaver.Models.Entities;
using Umbraco.Community.SchemeWeaver.Persistence;
using uSync.BackOffice;
using uSync.BackOffice.Configuration;
using uSync.BackOffice.Models;
using uSync.BackOffice.Services;
using uSync.BackOffice.SyncHandlers;
using uSync.BackOffice.SyncHandlers.Interfaces;
using uSync.BackOffice.SyncHandlers.Models;
using uSync.Core;

namespace Umbraco.Community.SchemeWeaver.uSync;

/// <summary>
/// uSync dashboard handler for SchemeWeaver schema mappings. Makes mappings a
/// first-class uSync entity so they appear in the uSync dashboard and take part
/// in <em>Import All</em> / <em>Export All</em> — not just first-boot seeding.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="SchemaMapping"/> is a plain NPoco entity, not an Umbraco
/// <c>IEntity</c>, so this derives from <see cref="SyncHandlerRoot{TObject,TContainer}"/>
/// (with <c>TObject == TContainer == SchemaMapping</c>) rather than one of the
/// tree-based handler bases. <c>SyncHandlerRoot</c> places no constraint on
/// <c>TObject</c>, so no <c>IEntity</c> implementation is required — this mirrors
/// the established pattern used by other community uSync addons for custom
/// objects (e.g. Growcreate's SchemaGenerator addon).
/// </para>
/// <para>
/// The per-item XML is produced by the existing <see cref="SchemaMappingSerializer"/>
/// (resolved by uSync from the serializer collection via the shared item type
/// <see cref="SchemeWeaverMappingConstants.ItemType"/>). The handler's
/// <see cref="SyncHandlerAttribute"/> folder is
/// <see cref="SchemeWeaverMappingPaths.MappingsFolderName"/>, and uSync writes
/// flat <c>{alias}.config</c> files, so exports land in exactly the same place
/// (<c>uSync/{version}/SchemeWeaverMappings/{alias}.config</c>) that the
/// export-on-save handler and first-boot importer already use — existing
/// exports round-trip cleanly.
/// </para>
/// <para>
/// uSync registers every <see cref="ISyncHandler"/> automatically via its type
/// loader, so no explicit collection-builder registration is needed (adding one
/// would register the handler twice).
/// </para>
/// </remarks>
[SyncHandler(
    "schemeWeaverMappingHandler",
    "SchemeWeaver Mappings",
    SchemeWeaverMappingPaths.MappingsFolderName,
    SchemeWeaverMappingHandlerPriority,
    Icon = "icon-diagram-alt",
    EntityType = SchemeWeaverMappingConstants.ItemType)]
public class SchemaMappingHandler : SyncHandlerRoot<SchemaMapping, SchemaMapping>, ISyncHandler
{
    // uSync's own handlers occupy 1000–1050; sit just above them so SchemeWeaver
    // mappings import after the doc types they reference.
    private const int SchemeWeaverMappingHandlerPriority = 1055;

    private readonly IServiceScopeFactory _scopeFactory;

    public SchemaMappingHandler(
        ILogger<SyncHandlerRoot<SchemaMapping, SchemaMapping>> logger,
        AppCaches appCaches,
        IShortStringHelper shortStringHelper,
        ISyncFileService syncFileService,
        ISyncEventService mutexService,
        ISyncConfigService uSyncConfig,
        ISyncItemFactory itemFactory,
        IServiceScopeFactory scopeFactory)
        : base(logger, appCaches, shortStringHelper, syncFileService, mutexService, uSyncConfig, itemFactory)
    {
        _scopeFactory = scopeFactory;
    }

    /// <summary>
    /// Every import path (Import All and single-item import) routes through
    /// <see cref="ImportElementAsync"/>, so wrapping it in the re-entrancy guard
    /// keeps the export-on-save handler dormant while uSync writes mappings back
    /// through the repository — preventing any import → export churn.
    /// </summary>
    public override async Task<IEnumerable<uSyncAction>> ImportElementAsync(
        XElement node, string filename, HandlerSettings settings, uSyncImportOptions options)
    {
        using (SchemeWeaverImportGuard.Enter())
        {
            return await base.ImportElementAsync(node, filename, settings, options);
        }
    }

    /// <summary>SchemeWeaver mappings are a flat list, so the root request (parent is null) returns them all.</summary>
    protected override Task<IEnumerable<SchemaMapping>> GetChildItemsAsync(SchemaMapping? parent)
        => parent is not null
            ? Task.FromResult(Enumerable.Empty<SchemaMapping>())
            : GetAllItems();

    /// <summary>Mappings are not foldered.</summary>
    protected override Task<IEnumerable<SchemaMapping>> GetFoldersAsync(SchemaMapping? parent)
        => Task.FromResult(Enumerable.Empty<SchemaMapping>());

    /// <summary>
    /// The serializer's deserialize path performs its own find-or-create against
    /// the repository, so the handler does not need to resolve a live item here.
    /// </summary>
    protected override Task<SchemaMapping?> GetFromServiceAsync(SchemaMapping? item)
        => Task.FromResult<SchemaMapping?>(default);

    /// <summary>Mappings have no container hierarchy, so there is nothing to prune.</summary>
    protected override Task<IEnumerable<uSyncAction>> DeleteMissingItemsAsync(
        SchemaMapping parent, IEnumerable<Guid> keysToKeep, bool reportOnly)
        => Task.FromResult(Enumerable.Empty<uSyncAction>());

    /// <summary>The content type alias is the mapping's natural display name.</summary>
    protected override string GetItemName(SchemaMapping item) => item.ContentTypeAlias;

    /// <summary>
    /// Source of every mapping for export. The repository is scoped while this
    /// handler is a singleton, so resolve it per call through a fresh scope.
    /// <see cref="ISchemaMappingRepository.GetAll"/> returns a fully materialised
    /// list, so it is safe to dispose the scope before the items are consumed.
    /// </summary>
    private Task<IEnumerable<SchemaMapping>> GetAllItems()
    {
        using var scope = _scopeFactory.CreateScope();
        var repository = scope.ServiceProvider.GetRequiredService<ISchemaMappingRepository>();
        return Task.FromResult(repository.GetAll());
    }
}
