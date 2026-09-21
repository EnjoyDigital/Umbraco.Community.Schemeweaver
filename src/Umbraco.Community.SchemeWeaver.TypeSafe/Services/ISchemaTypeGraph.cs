namespace Umbraco.Community.SchemeWeaver.TypeSafe.Services;

/// <summary>
/// The Schema.org type tree as SchemeWeaver's own registry exposes it, with a direct-subtype
/// relation on top. The nested-type descent and the schema-type suggester walk this.
/// </summary>
/// <remarks>
/// Schema.org has multiple inheritance; C# does not, so Schema.NET expresses a type's extra
/// supertypes as interfaces (<c>HowToStep : CreativeWorkAndItemListAndListItem</c>, which
/// implements <c>ICreativeWork</c>, <c>IItemList</c> and <c>IListItem</c>) and synthesises
/// an abstract combining base class the registry never lists. Implementations must derive
/// the subtype relation from the CLR types' Schema.NET interfaces — the same walk the core's
/// <c>ISchemaRangeChecker</c> does — not from the registry's <c>ParentTypeName</c> alone, or
/// every multiply-inherited type becomes an orphan and <c>Recipe.recipeInstructions</c> can
/// never reach <c>HowToStep</c>.
/// </remarks>
public interface ISchemaTypeGraph
{
    /// <summary>Direct subtypes of a type: the concrete registry types whose nearest supertype set includes it. Sorted by name.</summary>
    IReadOnlyList<string> ChildrenOf(string typeName);

    /// <summary>Nearest supertypes of a type, with Schema.NET combining classes flattened to their parts.</summary>
    IReadOnlyList<string> ParentsOf(string typeName);

    /// <summary><c>true</c> when <paramref name="candidate"/> is <paramref name="ancestor"/> or descends from it (case-insensitive).</summary>
    bool IsSubtypeOf(string candidate, string ancestor);

    /// <summary>
    /// The declared range of a property, restricted to types the registry knows. Primitives
    /// (Text, URL, Number, DateTime…) drop out; they are the "plain value" case handled as a
    /// <c>property</c> mapping, not a nested object. Empty when the type or property is unknown.
    /// </summary>
    IReadOnlyList<string> RangeOf(string typeName, string propertyName);
}
