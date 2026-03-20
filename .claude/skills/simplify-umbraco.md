---
description: Simplify Umbraco content modelling and uSync configuration — compositions, data types, culture variants, orphaned configs. Run after CMS schema changes.
---

# Umbraco Content Model Simplifier

Clean up and simplify Umbraco content modelling, document types, and uSync configuration.

## When to Use

Run this skill after making CMS schema changes (content types, data types, compositions) to ensure the content model is clean, consistent, and maintainable.

## Simplification Targets

### Composition Hierarchies
- Merge redundant compositions that share identical properties
- Remove compositions that add only one property — inline it on the document type instead
- Flatten deep composition chains (3+ levels) where intermediate types add no reuse value
- Verify all compositions are still referenced by at least one document type

### Data Type Definitions
- Consolidate near-identical data type definitions (e.g., two text strings with the same validation)
- Remove unused data types
- Verify property editor aliases are valid for the current Umbraco version
- Check that MNTP (Multi-Node Tree Picker) and content picker configurations reference existing content types

### Culture Variant Settings
- Verify `<Variations>Culture</Variations>` is set on properties that need per-language values
- Check that invariant properties on culture-variant content types are intentional
- Ensure block list element types have correct variant settings for their usage context

### uSync File Hygiene
- Identify orphaned `.config` files for content types or data types that no longer exist
- Check for duplicate keys or aliases across uSync config files
- Verify content items exist in all expected cultures (missing translations cause blank listings)
- Ensure `<SortOrder>` values are sequential and consistent

### Block List References
- Verify all block type GUIDs referenced in data type configurations still exist as element types
- Check that block list allowed content types match current document type aliases
- Remove references to deleted or renamed block types

### Clean Up
- Remove commented-out XML in uSync config files
- Ensure consistent formatting and indentation
- Remove deprecated property editor references

## Workflow

1. **Identify files in scope**
   - Focus on uSync config files changed in the current session, or use user-specified scope
   - Include ContentTypes, DataTypes, and related content configs
2. **Analyse each file** for simplification opportunities
3. **Cross-reference** compositions, data types, and block references for consistency
4. **Apply simplifications** incrementally
5. **Verify**: `dotnet build` (backend compiles uSync handlers)
6. **Report** what was simplified and any remaining opportunities

## Arguments

Optionally specify directories or specific config files to simplify.

Usage:
- `/simplify-umbraco` — Simplify recently changed uSync configs
- `/simplify-umbraco uSync/v16/ContentTypes/` — Simplify content type definitions
- `/simplify-umbraco uSync/v16/DataTypes/` — Simplify data type definitions

## Important

- Preserve existing content — never remove a content type that has published content using it
- Use the Umbraco MCP server for cross-referencing if available
- Changes to base compositions affect all inheriting types — flag for careful review
- Use British English in comments and descriptions
- Run `dotnet build` after changes to confirm compilation
