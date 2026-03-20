---
description: Simplify and clean up C#/.NET code — pattern matching, null handling, LINQ, async, guard clauses. Run after completing changes to reduce complexity.
---

# C# Code Simplifier

Clean up and simplify C#/.NET code after making changes.

## When to Use

Run this skill after completing a feature or fix in C#/.NET code to ensure it is clean, readable, and idiomatic.

## Simplification Targets

### Pattern Matching
- Use `is` patterns over explicit type checks and casts
- Use `switch` expressions over verbose `switch` statements where the result is an assignment or return
- Use `when` clauses to combine guards with pattern matching
- Use property patterns (`{ Prop: value }`) to avoid intermediate variables

### Null Handling
- Use `?.` (null-conditional) over explicit null checks before member access
- Use `??` (null-coalescing) over ternary or if/else for defaults
- Use `??=` (null-coalescing assignment) over check-then-assign patterns
- Prefer pattern matching (`is null`, `is not null`) over `== null` / `!= null`
- Remove redundant null checks where the type system already guarantees non-null

### Collection Expressions (.NET 8+)
- Use `[]` collection expressions where the target type is clear
- Use `..` spread operator to flatten nested collections

### LINQ
- Use LINQ where it improves readability (`.Where()`, `.Select()`, `.Any()`, `.All()`, `.FirstOrDefault()`)
- Do **not** convert loops to LINQ when debugging step-through would suffer
- Prefer method syntax over query syntax for simple operations

### Resource Management
- Use `using var` (declaration form) over `using` blocks to reduce nesting
- Ensure `IDisposable` resources are always in a `using` scope

### Guard Clauses
- Flatten deep nesting by returning or throwing early
- Move precondition checks to the top of the method
- Maximum nesting depth: 3 levels

### Async Patterns
- Use `ConfigureAwait(false)` in library code (non-UI, non-controller layers)
- Replace `.Result` / `.Wait()` / `.GetAwaiter().GetResult()` with `await`
- Ensure async methods return `Task` or `ValueTask`, not `void` (except event handlers)

### Modern .NET APIs
- Replace custom `Chunk<T>()` implementations with built-in `.Chunk()` (System.Linq, .NET 6+)
- Use `[GeneratedRegex]` source generators for hot-path regex patterns (.NET 7+)
- Use `StringComparer.OrdinalIgnoreCase` over `InvariantCultureIgnoreCase` for non-locale comparisons

### Constructor Simplification
- If a class has more than 8 constructor parameters, consider extracting sub-services or using a parameter object
- Group related dependencies into cohesive helper services

### Clean Up
- Remove unused `using` directives
- Remove unused variables and parameters
- Remove commented-out code
- Consolidate duplicate logic into shared helpers
- Extract magic numbers and strings to named constants

## Workflow

1. **Identify files in scope**
   - Focus on files changed in the current session, or use user-specified scope
2. **Analyse each file** for simplification opportunities
3. **Apply simplifications** incrementally, preserving original behaviour
4. **Verify**: `dotnet build` then `dotnet test` (if tests exist)
5. **Report** what was simplified and any remaining opportunities

## Arguments

Optionally specify files or directories to simplify.

Usage:
- `/simplify-csharp` — Simplify recently changed C# files
- `/simplify-csharp src/Services/` — Simplify a specific directory
- `/simplify-csharp MyService.cs` — Simplify a specific file

## Example Transformations

Before:
```csharp
if (result != null)
{
    if (result.Value != null)
    {
        return result.Value.ToString();
    }
    else
    {
        return "default";
    }
}
else
{
    return "default";
}
```

After:
```csharp
return result?.Value?.ToString() ?? "default";
```

Before:
```csharp
var items = new List<string>();
foreach (var item in source)
{
    if (item.IsValid)
    {
        items.Add(item.Name);
    }
}
```

After:
```csharp
var items = source.Where(x => x.IsValid).Select(x => x.Name).ToList();
```

## Important

- Preserve original behaviour — simplification must not change semantics
- Do not refactor working code just for style — only simplify genuine complexity
- Use British English in comments and user-facing text
- Run `dotnet build` after changes to confirm compilation
