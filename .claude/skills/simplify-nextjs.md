---
description: Simplify and clean up TypeScript/React/Next.js code — destructuring, optional chaining, array methods, React patterns. Run after completing changes to reduce complexity.
---

# Next.js / TypeScript Code Simplifier

Clean up and simplify TypeScript, React, and Next.js code after making changes.

## When to Use

Run this skill after completing a feature or fix in TypeScript/React/Next.js code to ensure it is clean, readable, and idiomatic.

## Simplification Targets

### Variable Declarations
- Use `const` over `let` when the variable is never reassigned
- Use template literals over string concatenation
- Use destructuring for object and array access where it improves clarity
- Avoid unnecessary intermediate variables

### Null & Undefined Handling
- Use optional chaining (`?.`) over explicit null/undefined checks
- Use nullish coalescing (`??`) over `||` when falsy values (0, '') are valid
- Use `??=` (nullish coalescing assignment) where supported

### Array & Object Operations
- Use `.map()`, `.filter()`, `.find()`, `.some()`, `.every()` over verbose `for` loops
- Use `.reduce()` only when the intent is clear — prefer explicit loops for complex accumulations
- Use `.flatMap()` to combine map and flatten
- Use object spread (`{ ...obj }`) over `Object.assign()`
- Use `Object.entries()`, `Object.keys()`, `Object.values()` over `for...in` loops

### Type Safety
- Use type guards and type narrowing over type casts (`as`)
- Use discriminated unions for variant types
- Avoid `any` — use `unknown` with type narrowing, or find the correct type from generated models
- Add explicit return types to exported functions

### React Patterns
- Use function components (not class components)
- Extract shared stateful logic into custom hooks
- Memoize expensive computations with `useMemo` only when there is a measurable benefit
- Use `useCallback` only when passing callbacks to memoized children
- Avoid unnecessary `useEffect` — derive state during render when possible

### Import Organisation
- Group imports: external packages, then internal modules, then relative imports
- Remove unused imports
- Use named imports over default imports where both are available

### Clean Up
- Remove `console.log` / `console.warn` left from debugging (keep intentional logging)
- Remove commented-out code
- Remove unused variables and functions
- Consolidate duplicate logic into shared utilities
- Extract magic numbers and strings to named constants

## Workflow

1. **Identify files in scope**
   - Focus on files changed in the current session, or use user-specified scope
2. **Analyse each file** for simplification opportunities
3. **Apply simplifications** incrementally, preserving original behaviour
4. **Verify**: `bun run lint && bun run build`, then `bun test` (if tests exist)
5. **Report** what was simplified and any remaining opportunities

## Arguments

Optionally specify files or directories to simplify.

Usage:
- `/simplify-nextjs` — Simplify recently changed TypeScript files
- `/simplify-nextjs src/components/` — Simplify a specific directory
- `/simplify-nextjs MyComponent.tsx` — Simplify a specific file

## Example Transformations

Before:
```typescript
let name;
if (user !== null && user !== undefined) {
    name = user.name;
} else {
    name = 'Anonymous';
}
```

After:
```typescript
const name = user?.name ?? 'Anonymous';
```

Before:
```typescript
const results = [];
for (let i = 0; i < items.length; i++) {
    if (items[i].active) {
        results.push(items[i].label);
    }
}
```

After:
```typescript
const results = items.filter(item => item.active).map(item => item.label);
```

## Important

- **Never touch generated files** in `src/api/` or `src/api-site/`
- Preserve original behaviour — simplification must not change semantics
- Do not refactor working code just for style — only simplify genuine complexity
- Use `bun` for all frontend tooling (never `npm`)
- Use British English in comments and user-facing text
