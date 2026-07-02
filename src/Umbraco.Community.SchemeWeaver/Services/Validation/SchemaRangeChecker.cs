namespace Umbraco.Community.SchemeWeaver.Services.Validation;

/// <inheritdoc />
public class SchemaRangeChecker : ISchemaRangeChecker
{
    private readonly ISchemaTypeRegistry _registry;

    public SchemaRangeChecker(ISchemaTypeRegistry registry) => _registry = registry;

    /// <inheritdoc />
    public bool IsInRange(string chosenTypeName, IReadOnlyList<string> acceptedTypes)
    {
        var chosenClr = _registry.GetClrType(chosenTypeName);
        return chosenClr is not null && IsInRange(chosenClr, acceptedTypes);
    }

    /// <inheritdoc />
    public bool IsInRange(Type chosenClr, IReadOnlyList<string> acceptedTypes)
    {
        var interfaces = chosenClr.GetInterfaces();

        foreach (var accepted in acceptedTypes)
        {
            var interfaceName = "I" + accepted;
            if (interfaces.Any(i => string.Equals(i.Name, interfaceName, StringComparison.Ordinal)))
                return true;

            var acceptedClr = _registry.GetClrType(accepted);
            if (acceptedClr is not null && acceptedClr.IsAssignableFrom(chosenClr))
                return true;
        }

        return false;
    }
}
