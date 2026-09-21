using System.Text.Json.Serialization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Umbraco.Cms.Core.Models;
using Umbraco.Cms.Core.Models.Entities;
using Umbraco.Cms.Core.Services;
using Umbraco.Community.SchemeWeaver.TypeSafe.Configuration;
using Umbraco.Extensions;
using Editors = Umbraco.Community.SchemeWeaver.SchemeWeaverConstants.PropertyEditors;

namespace Umbraco.Community.SchemeWeaver.TypeSafe.Services.Judgments;

/// <summary>Where a neighbour type sits relative to the content type being mapped.</summary>
internal enum NeighbourRelation
{
    /// <summary>A type whose pages can directly contain pages of this type (depth 1).</summary>
    Parent,

    /// <summary>A type further up (depth 2 and beyond, nearest first).</summary>
    Ancestor,

    /// <summary>A type whose pages sit beside pages of this type under the same parent.</summary>
    Sibling,
}

/// <summary>One property of a neighbour type that a related-node resolver can read.</summary>
internal sealed record NeighbourProperty(string Alias, string Name, string EditorAlias);

/// <summary>
/// One neighbour type: its relation, alias, name, depth (1 = parent, 2 = grandparent; 0 for a
/// sibling, which sits on the same level as the type itself) and readable properties.
/// </summary>
internal sealed record NeighbourType(
    NeighbourRelation Relation,
    string Alias,
    string Name,
    int Depth,
    IReadOnlyList<NeighbourProperty> Properties)
{
    /// <summary>The core source-type value for this relation (<c>parent</c>, <c>ancestor</c>, <c>sibling</c>).</summary>
    public string SourceType => Relation switch
    {
        NeighbourRelation.Parent => SchemeWeaverConstants.SourceTypes.Parent,
        NeighbourRelation.Ancestor => SchemeWeaverConstants.SourceTypes.Ancestor,
        _ => SchemeWeaverConstants.SourceTypes.Sibling,
    };
}

/// <summary>
/// The content types that sit above and beside the one being mapped, with the properties a
/// cross-node mapping could read from them. Built from the document types' allowed-children
/// declarations, from the real content tree (sampled nodes of the type), or both; see
/// <see cref="TypeSafeOptions.NeighbourhoodDiscovery"/>.
/// </summary>
/// <remarks>
/// <para>
/// Discovery is the snapshot's job; this class is the contract the cross-node round consumes.
/// Parents come first, then ancestors nearest-first, then siblings: that is the option order
/// the model sees, and order was measured to matter. A type is presented under one relation
/// only (parent wins, then ancestor, then sibling). Element types and the type itself never
/// appear.
/// </para>
/// <para>
/// Every alias here is read from real Umbraco structure or real nodes, never composed: the
/// cross-node round can only offer what this class found, so it cannot emit a
/// <c>SourceContentTypeAlias</c> that does not exist. At render time the core reads the actual
/// parent for <c>parent</c> (ignoring the type alias), the nearest ancestor of the alias that
/// has the property for <c>ancestor</c>, and the first sibling of the alias that has the
/// property for <c>sibling</c>; a neighbour that was merely declared but never observed is
/// still a legal target, it just resolves to nothing on nodes that do not have it. Because
/// "the first sibling of that type" only names a page when there is exactly one, an observed
/// sibling type is offered only when no sampled parent holds more than one page of it; a
/// declared sibling stays offered, since structure cannot say how many there are.
/// </para>
/// </remarks>
internal sealed class ContentTypeNeighbourhood
{
    public static readonly ContentTypeNeighbourhood Empty = new([], [], [], allowedAsRoot: false);

    /// <summary>
    /// Editors beyond the text, media and date sets that a related-node resolver reads as a
    /// scalar: a tag list, a dropdown or radio selection, a URL picker and a number all arrive
    /// as usable values.
    /// </summary>
    private static readonly HashSet<string> OtherReadableEditors = new(StringComparer.OrdinalIgnoreCase)
    {
        "Umbraco.Tags",
        "Umbraco.MultiUrlPicker",
        "Umbraco.RadioButtonList",
        "Umbraco.Integer",
        "Umbraco.Decimal",
    };

    /// <summary>The built-ins every neighbour offers, in option order: the name first, then the URL.</summary>
    private static readonly string[] ReservedBuiltIns =
    [
        SchemeWeaverConstants.BuiltInProperties.Name,
        SchemeWeaverConstants.BuiltInProperties.Url,
    ];

    private const string DateTimeEditor = "Umbraco.DateTime";
    private const string DropDownEditorPrefix = "Umbraco.DropDown";
    private const string LabelEditor = "Umbraco.Label";

    /// <summary>
    /// Not in the core's text-producing set (a block's basic text must not join e-mail
    /// addresses), but an organisation's <c>email</c> is exactly what a cross-node row reads.
    /// </summary>
    private const string EmailAddressEditor = "Umbraco.EmailAddress";

    public ContentTypeNeighbourhood(
        IReadOnlyList<NeighbourType> parents,
        IReadOnlyList<NeighbourType> ancestors,
        IReadOnlyList<NeighbourType> siblings,
        bool allowedAsRoot)
    {
        Parents = parents;
        Ancestors = ancestors;
        Siblings = siblings;
        AllowedAsRoot = allowedAsRoot;
    }

    public IReadOnlyList<NeighbourType> Parents { get; }

    public IReadOnlyList<NeighbourType> Ancestors { get; }

    public IReadOnlyList<NeighbourType> Siblings { get; }

    /// <summary>The type may also sit at the root, where it has no parent at all.</summary>
    public bool AllowedAsRoot { get; }

    public bool IsEmpty => Parents.Count == 0 && Ancestors.Count == 0 && Siblings.Count == 0;

    /// <summary>Every neighbour in option order: parents, ancestors nearest-first, siblings.</summary>
    public IEnumerable<NeighbourType> All
        => Parents.Concat(Ancestors.OrderBy(a => a.Depth)).Concat(Siblings);

    /// <summary>
    /// Discovers the neighbourhood of <paramref name="self"/>: structurally (the allowed-children
    /// declarations), from the observed tree (sampled nodes), or both, per
    /// <see cref="TypeSafeOptions.NeighbourhoodDiscovery"/>. Never throws: each source degrades
    /// to nothing with a Debug log, and an all-round failure returns <see cref="Empty"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Both sources exist because neither is enough alone. Structure is what the editor
    /// declared, so it is complete for a tightly modelled site but blind to what is actually
    /// there; on a loosely modelled site (most of them) a page type allows "any page" and the
    /// declarations say nothing useful. Observation reads the real tree and is what works on
    /// those sites, but it only sees types that have content today.
    /// </para>
    /// <para>
    /// <see cref="IContentService"/> and <see cref="IEntityService"/> are resolved lazily from
    /// the provider rather than injected: the mapper must not take a dependency that could reach
    /// <c>ISchemaAutoMapper</c> (which this satellite decorates), and a host without one of them
    /// (a unit test, a trimmed composition) simply gets the structural half.
    /// </para>
    /// </remarks>
    public static Task<ContentTypeNeighbourhood> DiscoverAsync(
        IContentType self,
        IContentTypeService contentTypeService,
        IServiceProvider serviceProvider,
        TypeSafeOptions options,
        ILogger logger,
        CancellationToken cancellationToken)
        => Task.FromResult(Discover(self, contentTypeService, serviceProvider, options, logger, cancellationToken));

    /// <summary>
    /// The synchronous body of <see cref="DiscoverAsync"/>: every Umbraco call it makes is
    /// synchronous, and the Task-shaped entry point exists so the contract can grow an awaited
    /// source later without changing its callers.
    /// </summary>
    private static ContentTypeNeighbourhood Discover(
        IContentType self,
        IContentTypeService contentTypeService,
        IServiceProvider serviceProvider,
        TypeSafeOptions options,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        var mode = options.NeighbourhoodDiscovery;
        var useStructure = mode is TypeSafeNeighbourhoodDiscovery.Structure or TypeSafeNeighbourhoodDiscovery.Both;
        var useObserved = mode is TypeSafeNeighbourhoodDiscovery.Observed or TypeSafeNeighbourhoodDiscovery.Both;
        var maxDepth = Math.Max(1, options.MaxAncestorDepth);

        // Every non-element document type, indexed both ways: allowed-children entries carry a
        // Key and an Alias and either may be the one that is populated (uSync imports set both,
        // hand-built ones sometimes only the key).
        Dictionary<Guid, IContentType> byKey;
        Dictionary<string, IContentType> byAlias;
        try
        {
            var all = contentTypeService.GetAll().Where(t => !t.IsElement).ToList();
            byKey = new Dictionary<Guid, IContentType>();
            byAlias = new Dictionary<string, IContentType>(StringComparer.OrdinalIgnoreCase);
            foreach (var type in all)
            {
                byKey.TryAdd(type.Key, type);
                if (!string.IsNullOrEmpty(type.Alias))
                    byAlias.TryAdd(type.Alias, type);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Boundary catch by policy: the neighbourhood is an enrichment of the state, and
            // failing to build it must degrade to the v1 state, never fail the mapping.
            logger.LogDebug(ex, "Neighbourhood of {ContentType}: document types unavailable; no cross-node sources", self.Alias);
            return Empty;
        }

        var candidates = new Candidates();
        var sampled = 0;

        if (useStructure)
        {
            try
            {
                DiscoverStructural(self, byKey, byAlias, maxDepth, candidates);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogDebug(ex, "Neighbourhood of {ContentType}: structural discovery failed; continuing with the observed tree", self.Alias);
            }
        }

        if (useObserved)
        {
            IServiceScope? scope = null;
            try
            {
                var contentService = Resolve<IContentService>(serviceProvider, ref scope);
                var entityService = Resolve<IEntityService>(serviceProvider, ref scope);
                if (contentService is not null && entityService is not null)
                {
                    sampled = DiscoverObserved(self, contentService, entityService, byAlias, options, maxDepth, candidates, cancellationToken);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogDebug(ex, "Neighbourhood of {ContentType}: observed-tree discovery failed; continuing with structure alone", self.Alias);
            }
            finally
            {
                scope?.Dispose();
            }
        }

        cancellationToken.ThrowIfCancellationRequested();

        var neighbourhood = Assemble(self, candidates, sampled, options);

        logger.LogDebug(
            "Neighbourhood of {ContentType} ({Mode}): {Parents} parent(s), {Ancestors} ancestor(s), {Siblings} sibling(s), {Properties} propert(ies) from {Sampled} sampled node(s); allowedAsRoot={AllowedAsRoot}",
            self.Alias, mode, neighbourhood.Parents.Count, neighbourhood.Ancestors.Count, neighbourhood.Siblings.Count,
            neighbourhood.All.Sum(t => t.Properties.Count), sampled, neighbourhood.AllowedAsRoot);

        return neighbourhood;
    }

    // ---------------------------------------------------------------------------
    // Structural discovery: the allowed-children declarations
    // ---------------------------------------------------------------------------

    private static void DiscoverStructural(
        IContentType self,
        Dictionary<Guid, IContentType> byKey,
        Dictionary<string, IContentType> byAlias,
        int maxDepth,
        Candidates candidates)
    {
        var all = byKey.Values.ToList();

        // Parents: every type that allows self as a child. Self-nesting (productListing under
        // productListing) is legal and common, but self never appears as its own neighbour.
        var parents = all.Where(t => !IsSelf(t, self) && AllowsChild(t, self)).ToList();
        foreach (var parent in parents)
            candidates.Structural(NeighbourRelation.Parent, parent, depth: 1);

        // Ancestors: breadth-first over the same predicate from the parents, depth 2 and up.
        // Cycles are legal (a page type that allows its own parent type), so a visited set
        // bounds the walk; a type keeps the depth at which it was first reached (nearest).
        var visited = new HashSet<Guid>(parents.Select(p => p.Key)) { self.Key };
        var frontier = parents;
        var ancestors = new List<IContentType>();
        for (var depth = 2; depth <= maxDepth && frontier.Count > 0; depth++)
        {
            var next = new List<IContentType>();
            foreach (var type in all)
            {
                if (visited.Contains(type.Key) || IsSelf(type, self))
                    continue;

                if (frontier.Any(child => AllowsChild(type, child)))
                {
                    visited.Add(type.Key);
                    next.Add(type);
                    candidates.Structural(NeighbourRelation.Ancestor, type, depth);
                }
            }

            ancestors.AddRange(next);
            frontier = next;
        }

        // Siblings: the parents' other allowed children, resolved by key (then alias) through
        // the non-element set, so an allowed element type or a dangling declaration is skipped.
        var excluded = new HashSet<Guid>(visited);
        foreach (var parent in parents)
        {
            foreach (var sort in parent.AllowedContentTypes ?? [])
            {
                var sibling = ResolveSort(sort, byKey, byAlias);
                if (sibling is null || IsSelf(sibling, self) || excluded.Contains(sibling.Key))
                    continue;

                candidates.Structural(NeighbourRelation.Sibling, sibling, depth: 0);
            }
        }
    }

    private static bool AllowsChild(IContentType parent, IContentType child)
    {
        foreach (var sort in parent.AllowedContentTypes ?? [])
        {
            if (sort.Key == child.Key && sort.Key != Guid.Empty)
                return true;

            if (!string.IsNullOrEmpty(sort.Alias) && string.Equals(sort.Alias, child.Alias, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    private static IContentType? ResolveSort(
        ContentTypeSort sort,
        Dictionary<Guid, IContentType> byKey,
        Dictionary<string, IContentType> byAlias)
    {
        if (sort.Key != Guid.Empty && byKey.TryGetValue(sort.Key, out var byK))
            return byK;

        if (!string.IsNullOrEmpty(sort.Alias) && byAlias.TryGetValue(sort.Alias, out var byA))
            return byA;

        return null;
    }

    private static bool IsSelf(IContentType type, IContentType self)
        => (type.Key != Guid.Empty && type.Key == self.Key)
           || string.Equals(type.Alias, self.Alias, StringComparison.OrdinalIgnoreCase);

    // ---------------------------------------------------------------------------
    // Observed discovery: sampled nodes of the type and their actual relatives
    // ---------------------------------------------------------------------------

    /// <summary>Samples nodes of the type and counts the types of their actual relatives. Returns the sample size.</summary>
    /// <remarks>
    /// Two things keep this cheap on the large sites the observed mode exists for. A sibling's
    /// type is read straight off the child entity <see cref="IEntityService.GetChildren(int, UmbracoObjectTypes)"/>
    /// returns, so a listing's children (thousands, on such a site) never become SQL parameters;
    /// only parent and ancestor ids (at most the sample size times the depth cap) go through
    /// <see cref="IEntityService.GetAll(UmbracoObjectTypes, int[])"/>, which binds one parameter
    /// per id without grouping, so they are sent in groups no wider than SQL Server allows. And
    /// a parent's children are read once however many sampled nodes share it, which in the tree
    /// order the sample comes back in is usually all of them.
    /// </remarks>
    private static int DiscoverObserved(
        IContentType self,
        IContentService contentService,
        IEntityService entityService,
        Dictionary<string, IContentType> byAlias,
        TypeSafeOptions options,
        int maxDepth,
        Candidates candidates,
        CancellationToken cancellationToken)
    {
        var pageSize = Math.Max(1, options.MaxSampledNodes);
        var nodes = contentService
            .GetPagedOfTypes([self.Id], 0, pageSize, out _, null)
            .ToList();
        if (nodes.Count == 0)
            return 0;

        // Per node: the parent id and the ancestor ids with their distance, collected first so
        // the ids resolve to type aliases in as few entity-service round trips as the parameter
        // limit allows rather than one per relative.
        var perNode = new List<(int? ParentId, List<(int Id, int Depth)> Ancestors)>(nodes.Count);
        var ids = new HashSet<int>();

        // Per parent id: how many children of each type sit under it, read once per parent.
        // The sampled nodes' own type is counted too and dropped later as self.
        var childTypesByParent = new Dictionary<int, Dictionary<string, int>>();
        foreach (var node in nodes)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var pathIds = ParsePath(node.Path, node.Id);
            int? parentId = node.ParentId > 0 ? node.ParentId : null;
            if (parentId is { } p)
            {
                ids.Add(p);
                if (!childTypesByParent.ContainsKey(p))
                    childTypesByParent[p] = CountChildrenByType(entityService, p);
            }

            // Path is root-first; the nearest ancestor is the last entry. Depth 1 is the parent
            // (already counted), so the ancestor list starts at depth 2.
            var ancestors = new List<(int, int)>();
            for (var i = pathIds.Count - 2; i >= 0; i--)
            {
                var depth = pathIds.Count - i;
                if (depth > maxDepth)
                    break;

                ancestors.Add((pathIds[i], depth));
                ids.Add(pathIds[i]);
            }

            perNode.Add((parentId, ancestors));
        }

        var aliasById = new Dictionary<int, string>();
        foreach (var group in ids.InGroupsOf(Umbraco.Cms.Core.Constants.Sql.MaxParameterCount))
        {
            foreach (var entity in entityService.GetAll(UmbracoObjectTypes.Document, group.ToArray()))
            {
                if (entity is IContentEntitySlim slim && !string.IsNullOrEmpty(slim.ContentTypeAlias))
                    aliasById[entity.Id] = slim.ContentTypeAlias;
            }
        }

        IContentType? TypeOfAlias(string alias)
            => byAlias.TryGetValue(alias, out var type) && !IsSelf(type, self) ? type : null;

        IContentType? TypeOf(int id)
            => aliasById.TryGetValue(id, out var alias) ? TypeOfAlias(alias) : null;

        foreach (var (parentId, ancestors) in perNode)
        {
            if (parentId is { } p && TypeOf(p) is { } parentType)
                candidates.Observed(NeighbourRelation.Parent, parentType, depth: 1);

            // Distinct per node so a share is "nodes that have this ancestor type", and the
            // nearest occurrence wins the depth (self-nesting puts the same type at 2 and 3).
            var seenAncestors = new HashSet<Guid>();
            foreach (var (id, depth) in ancestors)
            {
                if (TypeOf(id) is { } ancestorType && seenAncestors.Add(ancestorType.Key))
                    candidates.Observed(NeighbourRelation.Ancestor, ancestorType, depth);
            }

            // One count per type per node (a share is "nodes with this type beside them"). A
            // type that sits beside a node more than once is marked repeated instead: a sibling
            // row resolves to whichever sibling of that type comes first at render time, which
            // only names a page when there is exactly one (the contact page beside a department,
            // not one of thirty news articles beside a blog post).
            if (parentId is { } parent && childTypesByParent.TryGetValue(parent, out var childTypes))
            {
                foreach (var (alias, count) in childTypes)
                {
                    if (TypeOfAlias(alias) is not { } siblingType)
                        continue;

                    if (count > 1)
                        candidates.Repeated(NeighbourRelation.Sibling, siblingType);
                    else
                        candidates.Observed(NeighbourRelation.Sibling, siblingType, depth: 0);
                }
            }
        }

        return nodes.Count;
    }

    /// <summary>
    /// The children of <paramref name="parentId"/> counted by content type alias, read from the
    /// slim entities themselves so no child id needs a second lookup.
    /// </summary>
    private static Dictionary<string, int> CountChildrenByType(IEntityService entityService, int parentId)
    {
        var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var child in entityService.GetChildren(parentId, UmbracoObjectTypes.Document))
        {
            if (child is IContentEntitySlim slim && !string.IsNullOrEmpty(slim.ContentTypeAlias))
                counts[slim.ContentTypeAlias] = counts.GetValueOrDefault(slim.ContentTypeAlias) + 1;
        }

        return counts;
    }

    /// <summary>The ids of <paramref name="path"/> ("-1,1055,1060,...") root-first, without the -1 root marker and without the node itself.</summary>
    private static List<int> ParsePath(string? path, int selfId)
    {
        var result = new List<int>();
        if (string.IsNullOrEmpty(path))
            return result;

        foreach (var part in path.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (int.TryParse(part, out var id) && id > 0 && id != selfId)
                result.Add(id);
        }

        return result;
    }

    // ---------------------------------------------------------------------------
    // Assembly: merge, one relation per type, properties, caps
    // ---------------------------------------------------------------------------

    private static ContentTypeNeighbourhood Assemble(IContentType self, Candidates candidates, int sampled, TypeSafeOptions options)
    {
        var minCount = sampled > 0 ? (int)Math.Ceiling(Math.Clamp(options.MinObservedShare, 0, 1) * sampled) : 0;
        var assigned = new HashSet<Guid>();

        // Parents win, then ancestors, then siblings. Within a relation the structural
        // candidates come first (the declared structure is the site's own statement of intent)
        // and the observed-only ones follow; inside each group the most-observed lead, because
        // option order was measured to matter and "seen on most nodes" is the best ranking
        // signal available. The observed share applies to every relation alike: a type seen
        // beside one sampled node is a one-off neighbour, not a sibling of the type.
        var parents = Pick(candidates, NeighbourRelation.Parent, minCount, assigned);
        var ancestors = Pick(candidates, NeighbourRelation.Ancestor, minCount, assigned);
        var siblings = Pick(candidates, NeighbourRelation.Sibling, minCount, assigned);

        var maxTypes = Math.Max(1, options.MaxNeighbourTypes);
        var maxPerType = Math.Max(1, options.MaxPropertiesPerNeighbour);
        var maxTotal = Math.Max(1, options.MaxNeighbourProperties);

        var ordered = parents.Concat(ancestors.OrderBy(a => a.Depth)).Concat(siblings).Take(maxTypes).ToList();

        var result = new List<NeighbourType>();
        var budget = maxTotal;
        foreach (var candidate in ordered)
        {
            if (budget <= 0)
                break;

            var properties = ReadableProperties(candidate.Type, maxPerType);
            if (properties.Count > budget)
                properties = properties.Take(budget).ToList();
            if (properties.Count == 0)
                continue;

            budget -= properties.Count;
            result.Add(new NeighbourType(
                candidate.Relation,
                candidate.Type.Alias,
                string.IsNullOrEmpty(candidate.Type.Name) ? candidate.Type.Alias : candidate.Type.Name,
                candidate.Depth,
                properties));
        }

        return new ContentTypeNeighbourhood(
            result.Where(t => t.Relation == NeighbourRelation.Parent).ToList(),
            result.Where(t => t.Relation == NeighbourRelation.Ancestor).ToList(),
            result.Where(t => t.Relation == NeighbourRelation.Sibling).ToList(),
            self.AllowedAsRoot);
    }

    private static List<Candidate> Pick(Candidates candidates, NeighbourRelation relation, int minCount, HashSet<Guid> assigned)
    {
        // Ancestors are nearest-first before anything else (the depth is what the model and
        // the render-time "nearest ancestor of that type" rule both key on); parents and
        // siblings have one depth, so structure-first then most-observed decides their order.
        // Structure is always eligible; an observed-only candidate must clear the share and,
        // for a sibling, never have been seen more than once under a sampled parent.
        var eligible = candidates.For(relation).Where(c => c.Structural || (!c.Repeated && c.Count >= Math.Max(1, minCount)));
        var ordered = relation == NeighbourRelation.Ancestor
            ? eligible.OrderBy(c => c.Depth).ThenByDescending(c => c.Structural)
            : eligible.OrderByDescending(c => c.Structural);

        return ordered
            .ThenByDescending(c => c.Count)
            .ThenBy(c => c.Type.Alias, StringComparer.OrdinalIgnoreCase)
            .Where(c => assigned.Add(c.Type.Key))
            .Select(c => new Candidate(relation, c.Type, c.Depth))
            .ToList();
    }

    /// <summary>
    /// The properties of a neighbour type a related-node resolver can actually read, in the
    /// order the model sees them: text, media, dates, other scalars, then the <c>__name</c> and
    /// <c>__url</c> built-ins. Block editors and content pickers are left out because the core's
    /// cross-node path reads a scalar from the related node, and a label holds nothing.
    /// </summary>
    /// <remarks>
    /// The two built-ins are reserved outside the per-type cap: a parent's <c>__name</c> is the
    /// single most common cross-node source there is (the heuristic's own category rule reads
    /// it), and it must not fall off the end of a wide type.
    /// </remarks>
    private static List<NeighbourProperty> ReadableProperties(IContentType type, int maxPerType)
    {
        var text = new List<NeighbourProperty>();
        var media = new List<NeighbourProperty>();
        var dates = new List<NeighbourProperty>();
        var other = new List<NeighbourProperty>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var property in type.CompositionPropertyTypes)
        {
            var editor = property.PropertyEditorAlias ?? string.Empty;
            if (string.IsNullOrEmpty(property.Alias) || !seen.Add(property.Alias))
                continue;

            var bucket = Classify(editor);
            if (bucket is null)
                continue;

            var row = new NeighbourProperty(property.Alias, string.IsNullOrEmpty(property.Name) ? property.Alias : property.Name, editor);
            var target = bucket.Value switch
            {
                Bucket.Text => text,
                Bucket.Media => media,
                Bucket.Date => dates,
                _ => other,
            };
            target.Add(row);
        }

        // In the order the remarks give, __name first; the core's own list puts __url first.
        var builtIns = ReservedBuiltIns
            .Select(alias => SchemeWeaverConstants.BuiltInProperties.All.First(b => string.Equals(b.Alias, alias, StringComparison.Ordinal)))
            .Select(b => new NeighbourProperty(b.Alias, b.DisplayName, b.EditorAlias))
            .ToList();

        var room = Math.Max(0, maxPerType - builtIns.Count);
        return text.Concat(media).Concat(dates).Concat(other).Take(room).Concat(builtIns).ToList();
    }

    private static Bucket? Classify(string editor)
    {
        if (Editors.BlockEditorAliases.Contains(editor) || Editors.ContentPickerAliases.Contains(editor))
            return null;
        if (string.Equals(editor, LabelEditor, StringComparison.OrdinalIgnoreCase))
            return null;
        if (Editors.TextProducingEditorAliases.Contains(editor) || string.Equals(editor, EmailAddressEditor, StringComparison.OrdinalIgnoreCase))
            return Bucket.Text;
        if (Editors.MediaPickerAliases.Contains(editor))
            return Bucket.Media;
        if (string.Equals(editor, DateTimeEditor, StringComparison.OrdinalIgnoreCase))
            return Bucket.Date;
        if (OtherReadableEditors.Contains(editor) || editor.StartsWith(DropDownEditorPrefix, StringComparison.OrdinalIgnoreCase))
            return Bucket.Other;
        return null;
    }

    private enum Bucket
    {
        Text,
        Media,
        Date,
        Other,
    }

    /// <summary>
    /// Resolves a core service at call time: from the injected provider when it can hand one
    /// out, else from a child scope that the caller disposes (the pattern
    /// <c>ContentTypeSnapshot.ResolveSchemeWeaverService</c> established). <c>null</c> when
    /// the host has not registered it.
    /// </summary>
    private static T? Resolve<T>(IServiceProvider serviceProvider, ref IServiceScope? scope)
        where T : class
    {
        try
        {
            return serviceProvider.GetService<T>();
        }
        catch (InvalidOperationException)
        {
            scope ??= serviceProvider.CreateScope();
            return scope.ServiceProvider.GetService<T>();
        }
    }

    private sealed record Candidate(NeighbourRelation Relation, IContentType Type, int Depth);

    /// <summary>
    /// Per (relation, type): whether structure declared it, how many sampled nodes showed it,
    /// whether a sampled parent ever held more than one page of it (siblings only), and the
    /// nearest depth either source reported.
    /// </summary>
    private sealed class Candidates
    {
        private readonly Dictionary<(NeighbourRelation, Guid), Entry> _entries = [];

        public void Structural(NeighbourRelation relation, IContentType type, int depth)
        {
            var entry = Get(relation, type);
            entry.Structural = true;
            entry.Depth = entry.Depth == 0 ? depth : Math.Min(entry.Depth, depth);
        }

        public void Observed(NeighbourRelation relation, IContentType type, int depth)
        {
            var entry = Get(relation, type);
            entry.Count++;
            entry.Depth = entry.Depth == 0 ? depth : Math.Min(entry.Depth, depth);
        }

        /// <summary>Marks a type seen more than once under one sampled parent; observation alone never offers it again.</summary>
        public void Repeated(NeighbourRelation relation, IContentType type)
            => Get(relation, type).Repeated = true;

        public IEnumerable<Entry> For(NeighbourRelation relation)
            => _entries.Where(e => e.Key.Item1 == relation).Select(e => e.Value);

        private Entry Get(NeighbourRelation relation, IContentType type)
        {
            if (!_entries.TryGetValue((relation, type.Key), out var entry))
                _entries[(relation, type.Key)] = entry = new Entry(type);
            return entry;
        }

        public sealed class Entry(IContentType type)
        {
            public IContentType Type { get; } = type;

            public bool Structural { get; set; }

            public int Count { get; set; }

            public bool Repeated { get; set; }

            public int Depth { get; set; }
        }
    }

    // ---------------------------------------------------------------------------
    // Wire shape
    // ---------------------------------------------------------------------------

    /// <summary>The <c>neighbourhood</c> node of the cross-node request state.</summary>
    public object ToState()
        => new NeighbourhoodShape(
            "Pages of this type sit under 'parents'. 'ancestors' are further up (depth 2 = grandparent). "
            + "'siblings' sit beside them under the same parent. At render time a related page's value is read "
            + "from the actual parent, from the nearest ancestor of that type that has the property, or from the "
            + "first sibling of that type that has the property.",
            AllowedAsRoot,
            Parents.Select(Shape).ToList(),
            Ancestors.OrderBy(a => a.Depth).Select(Shape).ToList(),
            Siblings.Select(Shape).ToList());

    private static NeighbourShape Shape(NeighbourType t)
        => new(t.Alias, t.Name, t.Depth, t.Properties.Select(p => new NeighbourPropertyShape(p.Alias, p.Name, p.EditorAlias)).ToList());

    private sealed record NeighbourhoodShape(
        [property: JsonPropertyName("note")] string Note,
        [property: JsonPropertyName("allowedAsRoot")] bool AllowedAsRoot,
        [property: JsonPropertyName("parents")] IReadOnlyList<NeighbourShape> Parents,
        [property: JsonPropertyName("ancestors")] IReadOnlyList<NeighbourShape> Ancestors,
        [property: JsonPropertyName("siblings")] IReadOnlyList<NeighbourShape> Siblings);

    private sealed record NeighbourShape(
        [property: JsonPropertyName("alias")] string Alias,
        [property: JsonPropertyName("name")] string Name,
        [property: JsonPropertyName("depth")] int Depth,
        [property: JsonPropertyName("properties")] IReadOnlyList<NeighbourPropertyShape> Properties);

    private sealed record NeighbourPropertyShape(
        [property: JsonPropertyName("alias")] string Alias,
        [property: JsonPropertyName("name")] string Name,
        [property: JsonPropertyName("editor")] string Editor);
}
