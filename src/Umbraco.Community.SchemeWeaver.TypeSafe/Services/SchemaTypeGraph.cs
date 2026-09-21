using Umbraco.Community.SchemeWeaver.Services;

namespace Umbraco.Community.SchemeWeaver.TypeSafe.Services;

/// <summary>
/// The Schema.org type tree derived from the CLR types behind SchemeWeaver's
/// <see cref="ISchemaTypeRegistry"/>. Built once, lazily and thread-safely, on first use.
/// </summary>
/// <remarks>
/// <para>
/// Schema.org allows multiple inheritance; C# does not. Schema.NET therefore gives every
/// concrete type a matching interface (<c>Place : Thing, IPlace</c>) and, where a type has
/// several supertypes, an abstract combining base class named after its parts
/// (<c>HowToStep : CreativeWorkAndItemListAndListItem</c>). The registry only indexes
/// concrete classes, so <c>SchemaTypeInfo.ParentTypeName</c> dangles for every such type
/// and <c>Recipe.recipeInstructions</c> could never descend to <c>HowToStep</c>.
/// </para>
/// <para>
/// The eval harness (<c>eval/schema-graph.mjs</c>) worked round that by splitting the
/// dangling name on <c>And</c>. That is fragile (four real types have <c>And</c> in their
/// names) and unnecessary in-process: the CLR type carries the complete set of Schema.NET
/// interfaces it implements, so a type's supertypes are exactly the registry types
/// <c>N</c> for which it implements an interface named <c>I&lt;N&gt;</c>. This is the same
/// naming the core's <c>SchemaRangeChecker</c> relies on for range validation, so the two
/// can never disagree about what is in range.
/// </para>
/// </remarks>
public sealed class SchemaTypeGraph : ISchemaTypeGraph
{
    private readonly ISchemaTypeRegistry _registry;
    private readonly Lazy<GraphData> _data;

    private static readonly string[] Empty = [];

    public SchemaTypeGraph(ISchemaTypeRegistry registry)
    {
        _registry = registry;
        _data = new Lazy<GraphData>(Build, LazyThreadSafetyMode.ExecutionAndPublication);
    }

    /// <inheritdoc />
    public IReadOnlyList<string> ChildrenOf(string typeName)
    {
        var data = _data.Value;
        return data.TryCanonical(typeName, out var name) && data.Children.TryGetValue(name, out var children)
            ? children
            : Empty;
    }

    /// <inheritdoc />
    public IReadOnlyList<string> ParentsOf(string typeName)
    {
        var data = _data.Value;
        return data.TryCanonical(typeName, out var name) && data.Parents.TryGetValue(name, out var parents)
            ? parents
            : Empty;
    }

    /// <inheritdoc />
    public bool IsSubtypeOf(string candidate, string ancestor)
    {
        if (string.IsNullOrEmpty(candidate) || string.IsNullOrEmpty(ancestor))
            return false;

        if (string.Equals(candidate, ancestor, StringComparison.OrdinalIgnoreCase))
            return true;

        var data = _data.Value;
        return data.TryCanonical(candidate, out var name)
            && data.Supertypes.TryGetValue(name, out var supertypes)
            && supertypes.Contains(ancestor);
    }

    /// <inheritdoc />
    public IReadOnlyList<string> RangeOf(string typeName, string propertyName)
    {
        if (string.IsNullOrEmpty(typeName) || string.IsNullOrEmpty(propertyName))
            return Empty;

        var property = _registry.GetProperties(typeName)
            .FirstOrDefault(p => string.Equals(p.Name, propertyName, StringComparison.OrdinalIgnoreCase));
        if (property is null)
            return Empty;

        var data = _data.Value;
        var range = new List<string>();
        foreach (var accepted in property.AcceptedTypes)
        {
            // Primitives (Text, URL, DateTime…) are not registry types and drop out here.
            if (data.TryCanonical(accepted, out var name) && !range.Contains(name, StringComparer.Ordinal))
                range.Add(name);
        }

        return range;
    }

    /// <summary>
    /// One pass over the registry: for each concrete type, its full supertype set from the
    /// CLR interfaces; then the nearest supertypes (those not implied by another supertype);
    /// then the inverse relation. O(types × interfaces), a few milliseconds for ~780 types.
    /// </summary>
    private GraphData Build()
    {
        // The registry can list a name more than once (it indexes interface aliases too), so
        // de-duplicate by name before anything else.
        var canonical = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var info in _registry.GetAllTypes().Where(i => !string.IsNullOrEmpty(i.Name)))
            canonical.TryAdd(info.Name, info.Name);

        var supertypes = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        foreach (var name in canonical.Values)
        {
            var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var clr = _registry.GetClrType(name);
            if (clr is not null)
            {
                foreach (var ifaceName in clr.GetInterfaces().Select(i => i.Name))
                {
                    if (ifaceName.Length < 2 || ifaceName[0] != 'I' || !char.IsUpper(ifaceName[1]))
                        continue;

                    if (canonical.TryGetValue(ifaceName[1..], out var superName)
                        && !string.Equals(superName, name, StringComparison.OrdinalIgnoreCase))
                    {
                        set.Add(superName);
                    }
                }
            }

            supertypes[name] = set;
        }

        // Nearest supertypes: P is direct when no other supertype Q of T already has P above it.
        var parents = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);
        var children = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        foreach (var (name, all) in supertypes)
        {
            var direct = new List<string>();
            foreach (var p in all)
            {
                var implied = all.Any(q => !ReferenceEquals(p, q)
                    && !string.Equals(p, q, StringComparison.OrdinalIgnoreCase)
                    && supertypes.TryGetValue(q, out var qSupers)
                    && qSupers.Contains(p));

                if (!implied)
                    direct.Add(p);
            }

            direct.Sort(StringComparer.Ordinal);
            parents[name] = direct;

            foreach (var p in direct)
            {
                if (!children.TryGetValue(p, out var list))
                    children[p] = list = [];
                list.Add(name);
            }
        }

        var sortedChildren = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);
        foreach (var (name, list) in children)
        {
            list.Sort(StringComparer.Ordinal);
            sortedChildren[name] = list;
        }

        return new GraphData(canonical, supertypes, parents, sortedChildren);
    }

    private sealed class GraphData(
        Dictionary<string, string> canonical,
        Dictionary<string, HashSet<string>> supertypes,
        Dictionary<string, IReadOnlyList<string>> parents,
        Dictionary<string, IReadOnlyList<string>> children)
    {
        public Dictionary<string, HashSet<string>> Supertypes { get; } = supertypes;
        public Dictionary<string, IReadOnlyList<string>> Parents { get; } = parents;
        public Dictionary<string, IReadOnlyList<string>> Children { get; } = children;

        /// <summary>Resolves any casing of a registry type name to the registry's own spelling.</summary>
        public bool TryCanonical(string? typeName, out string name)
        {
            if (!string.IsNullOrEmpty(typeName) && canonical.TryGetValue(typeName, out var found))
            {
                name = found;
                return true;
            }

            name = string.Empty;
            return false;
        }
    }
}
