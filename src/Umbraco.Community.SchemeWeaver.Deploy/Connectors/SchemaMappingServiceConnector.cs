using System.Runtime.CompilerServices;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Umbraco.Cms.Core;
using Umbraco.Cms.Core.Deploy;
using Umbraco.Cms.Core.Services;
using Umbraco.Community.SchemeWeaver.Deploy.Artifacts;
using Umbraco.Community.SchemeWeaver.Models.Entities;
using Umbraco.Community.SchemeWeaver.Persistence;
using Umbraco.Deploy.Infrastructure.Connectors.ServiceConnectors;

namespace Umbraco.Community.SchemeWeaver.Deploy.Connectors;

/// <summary>
/// Deploy service connector for SchemeWeaver schema mappings.
/// </summary>
/// <remarks>
/// <para>
/// Discovered by the CMS type scanner (<see cref="IDiscoverable"/>) — no DI
/// registration. Connectors are singletons, so the scoped
/// <see cref="ISchemaMappingRepository"/> is resolved through a fresh scope per
/// call, mirroring the uSync serializer. Writes go straight to the repository,
/// which never publishes <c>SchemaMappingSaved/DeletedNotification</c> — so a
/// deployment can never re-trigger the disk refresher (or uSync's
/// export-on-save); the loop is structurally impossible and no re-entrancy
/// guard is needed.
/// </para>
/// <para>
/// Processes at pass 3: document types complete their final pass at 2, so the
/// mapping's doc-type dependency is always fully processed first, regardless of
/// intra-pass ordering.
/// </para>
/// </remarks>
[UdiDefinition(SchemeWeaverDeployConstants.MappingUdiEntityType, UdiType.GuidUdi)]
public class SchemaMappingServiceConnector
    : ServiceConnectorBase<SchemaMappingArtifact, GuidUdi, SchemaMapping>,
      IUniqueIdentifyingServiceConnector
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<SchemaMappingServiceConnector> _logger;

    public SchemaMappingServiceConnector(
        IServiceScopeFactory scopeFactory,
        ILogger<SchemaMappingServiceConnector> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override string[] ValidOpenSelectors => new[]
    {
        Constants.DeploySelector.ThisAndDescendants,
        Constants.DeploySelector.DescendantsOfThis,
    };

    protected override string OpenUdiName => "All SchemeWeaver mappings";

    protected override int[] ProcessPasses => new[] { 3 };

    public override Task<SchemaMappingArtifact?> GetArtifactAsync(
        GuidUdi udi, IContextCache contextCache, CancellationToken cancellationToken = default)
    {
        EnsureType(udi);

        using var scope = _scopeFactory.CreateScope();
        var repository = scope.ServiceProvider.GetRequiredService<ISchemaMappingRepository>();
        var mapping = FindByKey(repository, udi.Guid);
        if (mapping is null)
        {
            return Task.FromResult<SchemaMappingArtifact?>(null);
        }

        // A vanished content type means the doc-type dependency could never be
        // satisfied on a target; returning null keeps the refresher from
        // (re)writing a permanently-undeployable artifact.
        var contentTypeService = scope.ServiceProvider.GetRequiredService<IContentTypeService>();
        if (contentTypeService.Get(udi.Guid) is null)
        {
            _logger.LogWarning(
                "Skipping Deploy artifact for schema mapping {Alias}: content type {ContentTypeKey} no longer exists.",
                mapping.ContentTypeAlias, udi.Guid);
            return Task.FromResult<SchemaMappingArtifact?>(null);
        }

        return Task.FromResult<SchemaMappingArtifact?>(BuildArtifact(mapping, repository));
    }

    public override Task<SchemaMappingArtifact> GetArtifactAsync(
        SchemaMapping entity, IContextCache contextCache, CancellationToken cancellationToken = default)
    {
        if (entity.ContentTypeKey == Guid.Empty)
        {
            // Reachable when a mapping was saved before its content type resolved; an
            // empty key would collapse every such row onto one UDI. Callers reach
            // entities via ranges (which filter these out) or the guarded UDI overload.
            throw new InvalidOperationException(
                $"Schema mapping '{entity.ContentTypeAlias}' has an empty ContentTypeKey and cannot produce a Deploy artifact.");
        }

        using var scope = _scopeFactory.CreateScope();
        var repository = scope.ServiceProvider.GetRequiredService<ISchemaMappingRepository>();
        return Task.FromResult(BuildArtifact(entity, repository));
    }

    public override Task<ArtifactDeployState<SchemaMappingArtifact, SchemaMapping>> ProcessInitAsync(
        SchemaMappingArtifact artifact, IDeployContext context, CancellationToken cancellationToken = default)
    {
        EnsureType(artifact.Udi);

        using var scope = _scopeFactory.CreateScope();
        var repository = scope.ServiceProvider.GetRequiredService<ISchemaMappingRepository>();

        // Key-first; alias fallback adopts (and re-keys) the row when the doc type
        // was recreated at source with a new GUID but the alias survived.
        var entity = FindByKey(repository, artifact.Udi.Guid)
            ?? repository.GetByContentTypeAlias(artifact.ContentTypeAlias);

        return Task.FromResult(CreateInitState(artifact, entity));
    }

    public override Task ProcessAsync(
        ArtifactDeployState<SchemaMappingArtifact, SchemaMapping> state,
        IDeployContext context, int pass, CancellationToken cancellationToken = default)
    {
        state.NextPass = GetNextPass(pass);

        var artifact = state.Artifact;

        using var scope = _scopeFactory.CreateScope();
        var repository = scope.ServiceProvider.GetRequiredService<ISchemaMappingRepository>();

        var mapping = state.Entity ?? new SchemaMapping();
        mapping.ContentTypeAlias = artifact.ContentTypeAlias;
        mapping.ContentTypeKey = artifact.Udi.Guid;
        mapping.SchemaTypeName = artifact.SchemaTypeName;
        mapping.IsEnabled = artifact.IsEnabled;
        mapping.IsInherited = artifact.IsInherited;
        mapping.IdOverride = Normalize(artifact.IdOverride);

        // Alias-collision sweep: a *different* row already holding the incoming alias
        // can only be a stale mapping for a doc-type identity that no longer owns the
        // alias (recreated/renamed at source). Without the sweep the unique alias
        // index would fail the whole deployment; deleting converges on the source
        // state, inside Deploy's ambient scope so it commits atomically with the save.
        // Case-insensitive: the unique index is case-insensitive on default-collation
        // SQL Server, so a clash differing only in alias case would still violate it.
        var clash = repository.GetAll()
            .OrderBy(m => m.Id)
            .FirstOrDefault(m => string.Equals(
                m.ContentTypeAlias, artifact.ContentTypeAlias, StringComparison.OrdinalIgnoreCase));
        if (clash is not null && clash.Id != mapping.Id)
        {
            _logger.LogWarning(
                "Deploy: superseding stale schema mapping row {ClashId} for alias {Alias} (content type key {OldKey} -> {NewKey}).",
                clash.Id, artifact.ContentTypeAlias, clash.ContentTypeKey, artifact.Udi.Guid);
            repository.Delete(clash.Id);
        }

        var saved = repository.Save(mapping);

        var rows = artifact.PropertyMappings.Select(pm => new PropertyMapping
        {
            SchemaMappingId = saved.Id,
            SchemaPropertyName = pm.SchemaPropertyName,
            SourceType = pm.SourceType,
            IsAutoMapped = pm.IsAutoMapped,
            ContentTypePropertyAlias = Normalize(pm.ContentTypePropertyAlias),
            SourceContentTypeAlias = Normalize(pm.SourceContentTypeAlias),
            TransformType = Normalize(pm.TransformType),
            StaticValue = Normalize(pm.StaticValue),
            NestedSchemaTypeName = Normalize(pm.NestedSchemaTypeName),
            ResolverConfig = Normalize(pm.ResolverConfig),
            DynamicRootConfig = Normalize(pm.DynamicRootConfig),
            TargetPieceKey = Normalize(pm.TargetPieceKey),
        }).ToList();

        // Replace-all preserves artifact row order (row Ids are assigned in list order).
        repository.SavePropertyMappings(saved.Id, rows);

        return Task.CompletedTask;
    }

    public override async IAsyncEnumerable<GuidUdi> ExpandRangeAsync(
        UdiRange range, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        if (range.Udi.IsRoot)
        {
            using var scope = _scopeFactory.CreateScope();
            var repository = scope.ServiceProvider.GetRequiredService<ISchemaMappingRepository>();

            // Group by key: the unique index is on alias, not key, so an orphaned
            // old-alias row can share a key with its recreated mapping. One UDI must
            // never be yielded twice; the lowest-Id row wins (matching FindByKey).
            foreach (var group in repository.GetAll()
                .Where(m => m.ContentTypeKey != Guid.Empty)
                .GroupBy(m => m.ContentTypeKey))
            {
                if (group.Count() > 1)
                {
                    _logger.LogWarning(
                        "Multiple schema mappings share content type key {ContentTypeKey} (aliases: {Aliases}); only the oldest row is deployed.",
                        group.Key, string.Join(", ", group.Select(m => m.ContentTypeAlias)));
                }

                yield return group.OrderBy(m => m.Id).First().GetUdi();
            }
        }
        else if (range.Selector == Constants.DeploySelector.This && range.Udi is GuidUdi guidUdi)
        {
            using var scope = _scopeFactory.CreateScope();
            var repository = scope.ServiceProvider.GetRequiredService<ISchemaMappingRepository>();
            var mapping = FindByKey(repository, guidUdi.Guid);
            if (mapping is not null)
            {
                yield return mapping.GetUdi();
            }
        }
        else
        {
            throw new NotSupportedException($"Unexpected selector \"{range.Selector}\".");
        }

        await Task.CompletedTask;
    }

    public override Task<NamedUdiRange> GetRangeAsync(
        GuidUdi udi, string selector, CancellationToken cancellationToken = default)
    {
        if (udi.IsRoot)
        {
            return Task.FromResult(new NamedUdiRange(udi, OpenUdiName, selector));
        }

        using var scope = _scopeFactory.CreateScope();
        var repository = scope.ServiceProvider.GetRequiredService<ISchemaMappingRepository>();
        var mapping = FindByKey(repository, udi.Guid)
            ?? throw new ArgumentException($"Could not find a schema mapping with key \"{udi.Guid}\".", nameof(udi));

        return Task.FromResult(new NamedUdiRange(mapping.GetUdi(), mapping.ContentTypeAlias, selector));
    }

    public override Task<NamedUdiRange> GetRangeAsync(
        string entityType, string sid, string selector, CancellationToken cancellationToken = default)
    {
        EnsureType(entityType);

        if (sid is "-1")
        {
            EnsureOpenSelector(selector);
            return Task.FromResult(new NamedUdiRange(Udi.Create(entityType), OpenUdiName, selector));
        }

        if (!Guid.TryParse(sid, out var key))
        {
            throw new ArgumentException($"Invalid identifier \"{sid}\".", nameof(sid));
        }

        return GetRangeAsync(new GuidUdi(entityType, key), selector, cancellationToken);
    }

    /// <summary>
    /// Same alias + different UDI across two .uda files is a source-control-level
    /// mistake; exposing the alias lets Deploy's collision detector report it as a
    /// named clash instead of failing later on the unique database index.
    /// </summary>
    public string GetUniqueIdentifier(IArtifact artifact) => ((SchemaMappingArtifact)artifact).ContentTypeAlias;

    private SchemaMappingArtifact BuildArtifact(SchemaMapping mapping, ISchemaMappingRepository repository)
    {
        var udi = mapping.GetUdi();
        var dependencies = new[]
        {
            // Exist (not Match): the mapping needs its doc type present, not
            // byte-identical; ordering:true puts the doc type first in the topo sort.
            new ArtifactDependency(
                new GuidUdi(Constants.UdiEntityType.DocumentType, mapping.ContentTypeKey),
                ordering: true,
                ArtifactDependencyMode.Exist),
        };

        var artifact = new SchemaMappingArtifact(udi, dependencies)
        {
            Name = $"SchemeWeaver mapping: {mapping.ContentTypeAlias}",
            Alias = mapping.ContentTypeAlias,
            ContentTypeAlias = mapping.ContentTypeAlias,
            ContentTypeKey = mapping.ContentTypeKey,
            SchemaTypeName = mapping.SchemaTypeName,
            IsEnabled = mapping.IsEnabled,
            IsInherited = mapping.IsInherited,
            IdOverride = Normalize(mapping.IdOverride),
            // The repository fetch has no ORDER BY; explicit Id order makes the
            // checksum deterministic (delete-then-reinsert assigns Ids in row order).
            PropertyMappings = repository.GetPropertyMappings(mapping.Id)
                .OrderBy(pm => pm.Id)
                .Select(pm => new PropertyMappingArtifact
                {
                    SchemaPropertyName = pm.SchemaPropertyName,
                    SourceType = pm.SourceType,
                    IsAutoMapped = pm.IsAutoMapped,
                    ContentTypePropertyAlias = Normalize(pm.ContentTypePropertyAlias),
                    SourceContentTypeAlias = Normalize(pm.SourceContentTypeAlias),
                    TransformType = Normalize(pm.TransformType),
                    StaticValue = Normalize(pm.StaticValue),
                    NestedSchemaTypeName = Normalize(pm.NestedSchemaTypeName),
                    ResolverConfig = Normalize(pm.ResolverConfig),
                    DynamicRootConfig = Normalize(pm.DynamicRootConfig),
                    TargetPieceKey = Normalize(pm.TargetPieceKey),
                })
                .ToList(),
        };

        return artifact;
    }

    /// <summary>
    /// Key lookups order by Id: the unique index is on alias, so two rows can share a
    /// ContentTypeKey (orphaned old-alias row + recreated mapping) and an unordered
    /// FirstOrDefault would be DB-order-dependent — nondeterministic artifacts.
    /// </summary>
    private static SchemaMapping? FindByKey(ISchemaMappingRepository repository, Guid contentTypeKey)
        => repository.GetAll()
            .Where(m => m.ContentTypeKey == contentTypeKey)
            .OrderBy(m => m.Id)
            .FirstOrDefault();

    /// <summary>Empty and null must serialise identically or checksums flap between environments.</summary>
    private static string? Normalize(string? value) => string.IsNullOrEmpty(value) ? null : value;
}
