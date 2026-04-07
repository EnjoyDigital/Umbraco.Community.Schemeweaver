namespace Umbraco.Community.SchemeWeaver;

/// <summary>
/// Configuration options for SchemeWeaver, bound to the "SchemeWeaver" section in appsettings.json.
/// </summary>
public class SchemeWeaverOptions
{
    /// <summary>
    /// Maximum recursion depth for nested property resolution (content pickers, block lists).
    /// Prevents infinite loops in circular content structures. Default is 3.
    /// </summary>
    public int MaxRecursionDepth { get; set; } = 3;
}
