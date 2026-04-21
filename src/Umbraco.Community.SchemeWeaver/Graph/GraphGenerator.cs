using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Schema.NET;
using Umbraco.Cms.Core.Models.PublishedContent;
using Umbraco.Cms.Core.Routing;
using Umbraco.Community.SchemeWeaver.Services;

namespace Umbraco.Community.SchemeWeaver.Graph;

/// <summary>
/// Default <see cref="IGraphGenerator"/>. Two-phase assembly:
/// 1. Ask every registered <see cref="IGraphPiece"/> for its @id (null → skip).
///    This populates <see cref="GraphPieceContext.Ids"/>.
/// 2. Ask every needed piece to build its Schema.NET Thing. Pieces read from
///    <see cref="GraphPieceContext.Ids"/> to construct cross-references.
/// Built things are then serialised, the per-node <c>@context</c> is stripped,
/// and the result is wrapped as <c>{"@context": "https://schema.org", "@graph": [...]}</c>.
///
/// Serialises individual Things via the same fallback chain as
/// <see cref="Services.JsonLdGenerator.SafeSerialize"/> — Schema.NET's native
/// <c>ToString()</c> for the ~697 types that round-trip cleanly, falling back
/// to <see cref="DeduplicatingTypeInfoResolver"/> for the ~83 with property
/// name collisions. This keeps Phase B behaviour-compatible with the existing
/// per-mapping serialiser.
/// </summary>
public sealed class GraphGenerator : IGraphGenerator
{
    private readonly IReadOnlyList<IGraphPiece> _pieces;
    private readonly ISiteSettingsResolver _siteSettingsResolver;
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly IPublishedUrlProvider _urlProvider;
    private readonly ILogger<GraphGenerator> _logger;

    private static readonly JsonSerializerOptions _fallbackOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingDefault,
        TypeInfoResolver = new DeduplicatingTypeInfoResolver()
    };

    private static readonly JsonWriterOptions _writerOptions = new()
    {
        Indented = false
    };

    public GraphGenerator(
        IEnumerable<IGraphPiece> pieces,
        ISiteSettingsResolver siteSettingsResolver,
        IHttpContextAccessor httpContextAccessor,
        IPublishedUrlProvider urlProvider,
        ILogger<GraphGenerator> logger)
    {
        // Stable ordering: declared Order first, then Key alphabetically. Keeps
        // generated graphs diff-friendly across runs.
        _pieces = pieces
            .OrderBy(p => p.Order)
            .ThenBy(p => p.Key, StringComparer.Ordinal)
            .ToList();
        _siteSettingsResolver = siteSettingsResolver;
        _httpContextAccessor = httpContextAccessor;
        _urlProvider = urlProvider;
        _logger = logger;
    }

    public string? GenerateGraphJson(
        IPublishedContent content,
        string? culture = null,
        PieceScopeFilter scope = PieceScopeFilter.All)
    {
        if (_pieces.Count == 0)
            return null;

        var siteUrl = ResolveSiteUrl();
        var pageUrl = ResolvePageUrl(content);

        // Phase 1: which pieces contribute, and what @id does each declare?
        // ResolveId runs for EVERY piece regardless of the scope filter so that
        // cross-scope @id refs still resolve — a scope=Page WebPage piece can
        // still emit publisher: {"@id": "...#organization"} even though the
        // Organization body isn't in this graph.
        var ids = new Dictionary<string, string>(StringComparer.Ordinal);
        var probeContext = new GraphPieceContext
        {
            Content = content,
            SiteSettings = _siteSettingsResolver.Resolve(),
            Culture = culture,
            SiteUrl = siteUrl,
            PageUrl = pageUrl,
            Ids = ids
        };

        var needed = new List<IGraphPiece>(_pieces.Count);
        foreach (var piece in _pieces)
        {
            try
            {
                var id = piece.ResolveId(probeContext);
                if (string.IsNullOrWhiteSpace(id))
                    continue;
                ids[piece.Key] = id;
                needed.Add(piece);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "Graph piece {PieceKey} threw while resolving @id — skipping",
                    piece.Key);
            }
        }

        if (needed.Count == 0)
            return null;

        // Phase 2: build each needed piece with the @id table visible.
        // The scope filter is applied HERE so that refs in phase-1 Ids are
        // still available to pieces we DO emit.
        var buildContext = new GraphPieceContext
        {
            Content = probeContext.Content,
            SiteSettings = probeContext.SiteSettings,
            Culture = probeContext.Culture,
            SiteUrl = probeContext.SiteUrl,
            PageUrl = probeContext.PageUrl,
            Ids = ids
        };

        var nodes = new List<JsonObject>(needed.Count);
        foreach (var piece in needed)
        {
            if (!PieceMatchesScope(piece, scope))
                continue;

            try
            {
                var thing = piece.Build(buildContext);
                if (thing is null)
                    continue;

                // Ensure the declared @id is reflected on the Thing even when
                // a piece forgets to set it. Schema.NET's `Id` is Uri-typed;
                // we set it only if still absent so pieces can override.
                if (thing.Id is null && ids.TryGetValue(piece.Key, out var declaredId)
                    && Uri.TryCreate(declaredId, UriKind.Absolute, out var uri))
                {
                    thing.Id = uri;
                }

                var node = SerialiseThingAsNode(thing);
                if (node is not null)
                    nodes.Add(node);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "Graph piece {PieceKey} threw while building — skipping",
                    piece.Key);
            }
        }

        if (nodes.Count == 0)
            return null;

        return WrapAsGraph(nodes);
    }

    private static bool PieceMatchesScope(IGraphPiece piece, PieceScopeFilter scope) => scope switch
    {
        PieceScopeFilter.All => true,
        PieceScopeFilter.Site => piece.Scope == PieceScope.Site,
        PieceScopeFilter.Page => piece.Scope == PieceScope.Page,
        _ => true,
    };

    private static JsonObject? SerialiseThingAsNode(Thing thing)
    {
        // Use Schema.NET's native ToString() first — it's fast and handles
        // property-ordering conventions correctly. Fall back to the
        // deduplicating resolver on the ~83 types with interface-level
        // property collisions.
        string json;
        try
        {
            json = thing.ToString();
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("collides with another property"))
        {
            json = JsonSerializer.Serialize<object>(thing, _fallbackOptions);
        }

        var node = JsonNode.Parse(json) as JsonObject;
        if (node is null)
            return null;

        // Strip the per-node @context — it belongs only at the top level of
        // the graph. Keep @type, @id, and everything else.
        node.Remove("@context");

        // Collapse pure references. When a nested value is just {@type, @id}
        // (no other fields), reduce it to {@id}. Yoast-parity: refs within a
        // @graph are conventionally id-only since the referenced node already
        // carries its @type. Leaves embedded rich objects untouched.
        CollapsePureRefsRecursive(node);

        return node;
    }

    private static void CollapsePureRefsRecursive(JsonObject node)
    {
        // Snapshot property names to avoid mutating during iteration.
        var keys = node.Select(kvp => kvp.Key).ToList();
        foreach (var key in keys)
        {
            var value = node[key];
            switch (value)
            {
                case JsonObject childObj:
                    if (TryCollapsePureRef(childObj, out var collapsed))
                        node[key] = collapsed;
                    else
                        CollapsePureRefsRecursive(childObj);
                    break;
                case JsonArray arr:
                    CollapseRefsInArray(arr);
                    break;
            }
        }
    }

    private static void CollapseRefsInArray(JsonArray arr)
    {
        for (var i = 0; i < arr.Count; i++)
        {
            switch (arr[i])
            {
                case JsonObject childObj:
                    if (TryCollapsePureRef(childObj, out var collapsed))
                        arr[i] = collapsed;
                    else
                        CollapsePureRefsRecursive(childObj);
                    break;
                case JsonArray inner:
                    CollapseRefsInArray(inner);
                    break;
            }
        }
    }

    private static bool TryCollapsePureRef(JsonObject obj, out JsonObject collapsed)
    {
        collapsed = null!;
        if (obj.Count != 2) return false;
        if (!obj.ContainsKey("@id")) return false;
        if (!obj.ContainsKey("@type")) return false;

        var idValue = obj["@id"];
        if (idValue is null) return false;

        collapsed = new JsonObject { ["@id"] = idValue.DeepClone() };
        return true;
    }

    private static string WrapAsGraph(IReadOnlyList<JsonObject> nodes)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, _writerOptions))
        {
            writer.WriteStartObject();
            writer.WriteString("@context", "https://schema.org");
            writer.WritePropertyName("@graph");
            writer.WriteStartArray();
            foreach (var node in nodes)
                node.WriteTo(writer);
            writer.WriteEndArray();
            writer.WriteEndObject();
        }
        return System.Text.Encoding.UTF8.GetString(stream.ToArray());
    }

    private Uri? ResolveSiteUrl()
    {
        var request = _httpContextAccessor.HttpContext?.Request;
        if (request is null)
            return null;

        return Uri.TryCreate($"{request.Scheme}://{request.Host}", UriKind.Absolute, out var uri)
            ? uri
            : null;
    }

    private Uri? ResolvePageUrl(IPublishedContent content)
    {
        var url = _urlProvider.GetUrl(content, UrlMode.Absolute);
        if (!string.IsNullOrEmpty(url) && url != "#"
            && Uri.TryCreate(url, UriKind.Absolute, out var absolute))
            return absolute;

        var relative = _urlProvider.GetUrl(content, UrlMode.Relative);
        if (string.IsNullOrEmpty(relative) || relative == "#")
            return null;

        var site = ResolveSiteUrl();
        if (site is null)
            return null;

        return Uri.TryCreate(new Uri(site, relative).ToString(), UriKind.Absolute, out var combined)
            ? combined
            : null;
    }
}
