namespace Umbraco.Community.SchemeWeaver.Services.Validation;

/// <summary>
/// Shared Schema.org range-membership check: whether a chosen object type is
/// assignable to a property's accepted types, walking the Schema.NET interface
/// DAG exactly the way the runtime accepts values. Single home for the subtype
/// walk so <see cref="SchemaRangeValidator"/> (save/preview warnings) and the
/// block suggester's row-scoped <c>FitsTarget</c> annotation cannot drift apart.
/// </summary>
public interface ISchemaRangeChecker
{
    /// <summary>
    /// Resolves <paramref name="chosenTypeName"/> through the registry and checks it
    /// against <paramref name="acceptedTypes"/>. Unknown type names return false —
    /// an unresolvable type can never be proven assignable.
    /// </summary>
    bool IsInRange(string chosenTypeName, IReadOnlyList<string> acceptedTypes);

    /// <summary>
    /// Faithful to runtime acceptance: a value is accepted when its CLR type
    /// implements the Schema.NET interface for any of the property's accepted
    /// types (the interface DAG mirrors Schema.org multiple inheritance, e.g.
    /// LocalBusiness : IPlace, IOrganization), or when an accepted concrete type
    /// is assignable from it. Scalar accepted entries ("String", "Uri", …) have
    /// no Thing CLR type and no I-prefixed interface, so they correctly drop out
    /// and a Thing under a scalar-only property fails the check.
    /// </summary>
    bool IsInRange(Type chosenClr, IReadOnlyList<string> acceptedTypes);
}
