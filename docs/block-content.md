# Block Content Mapping

Block content mapping allows SchemeWeaver to extract structured data from Umbraco's BlockList and BlockGrid editors, transforming block elements into nested Schema.org types within your JSON-LD output. This is essential for schemas that require arrays of structured objects -- FAQ questions, product reviews, recipe steps, and similar patterns.

---

## Overview

Umbraco's block editors store collections of typed elements. Each block element is an `IPublishedElement` with its own content type and properties. SchemeWeaver's `BlockContentResolver` reads these collections and maps each element to a Schema.NET `Thing` (or extracts simple string values), producing nested JSON-LD that search engines can consume.

The block content source type (`blockContent`) is available when:

- The matched Umbraco property uses a block editor (`Umbraco.BlockList` or `Umbraco.BlockGrid`)
- The Schema.org property is flagged as a complex type

---

## How Resolution Works

The `BlockContentResolver` follows this process:

1. **Extract block items** -- Reads the property value and extracts `IPublishedElement` content items from either a `BlockListModel` or `BlockGridModel`.

2. **Check for string extraction mode** -- If the resolver config specifies `"extractAs": "stringList"`, each block item has a single named property extracted as a plain string. The result is a `List<string>` rather than a list of Things. This is used for Schema.org properties that expect string arrays (e.g., `recipeIngredient`).

3. **Map to Things** -- For each block element, the resolver creates an instance of the configured Schema.org type (`NestedSchemaTypeName`) and populates it using either:
   - **Configured nested mappings** from the `ResolverConfig` JSON, or
   - **Auto-mapping by name** if no nested mappings are configured (matches block property aliases to schema property names, case-insensitive)

4. **Apply wrapping** -- If a nested mapping specifies `wrapInType`, the resolved value is wrapped in an intermediate Schema.org type before being set on the parent Thing.

5. **Return collection** -- The resolver returns a `List<Thing>` which Schema.NET serialises as a JSON array in the output.

Recursion is limited to a maximum depth of 3 (configurable via `PropertyResolverContext.MaxRecursionDepth`) to prevent infinite loops.

---

## String Extraction Mode

Some Schema.org properties expect flat string arrays rather than nested objects. For example, `recipeIngredient` expects a list of ingredient strings, not a list of `Thing` objects.

String extraction mode is activated by setting `"extractAs": "stringList"` in the resolver config, along with `"contentProperty"` to name the block element property to read from.

**Resolver config:**

```json
{
  "extractAs": "stringList",
  "contentProperty": "ingredient"
}
```

**Behaviour:** Each block element in the BlockList has its `ingredient` property read as a string. The result is a JSON array of strings:

```json
{
  "recipeIngredient": [
    "200g plain flour",
    "100g caster sugar",
    "2 large eggs"
  ]
}
```

---

## The Nested Mapping Wizard

When you configure a `blockContent` mapping in the SchemeWeaver UI, a three-step wizard guides you through the process.

### Step 1: Select Block Element Type

The wizard loads the block element types configured on the BlockList/BlockGrid property and presents them as selectable cards, each showing the element's name, alias, and available properties.

If only one block element type is configured, it is auto-selected and the wizard advances to step 2 automatically.

If no block types can be detected (e.g., when the property configuration cannot be read), you can manually type in a block element alias.

### Step 2: Map Properties

A mapping table shows every property of the target Schema.org type in the left column, with dropdown selectors for the corresponding block element property in the centre column. The right column shows the **Wrap In Type** for complex schema properties.

The wizard includes an **Auto-Map** button that runs a three-tier matching algorithm:

1. **Exact name match** (case-insensitive) between block property and schema property
2. **Partial/contains match** where one name is a substring of the other
3. **Complex type sub-property match** that looks into accepted types' properties (e.g., the block has `ratingValue`, the schema property `reviewRating` accepts `Rating`, and `Rating` has a property called `ratingValue` -- so it matches with wrapping)

Auto-mapping only fills empty mappings and never overwrites manual selections.

### Step 3: Preview

Shows a summary of all configured mappings and a collapsible JSON preview of the `ResolverConfig` that will be stored. Review the configuration and click **Save** to apply.

---

## wrapInType Configuration

The `wrapInType` feature is one of the most important aspects of block content mapping. Many Schema.org properties expect values wrapped in an intermediate type rather than a raw string. Without wrapping, the JSON-LD would be structurally invalid for these properties.

### What It Does

When a nested mapping includes `wrapInType`, the resolver:

1. Resolves the raw value from the block element property
2. Creates a new instance of the specified Schema.org wrapper type
3. Sets the resolved value on the wrapper type's property (specified by `wrapInProperty`, or inferred)
4. Sets the wrapper instance as the value of the parent Thing's property

### When It Is Needed

Wrapping is needed whenever a Schema.org property expects a structured type but your block element stores the data as a simple value. Common scenarios:

- **FAQ answers**: Schema.org's `acceptedAnswer` expects an `Answer` object with a `text` property, but your block likely stores the answer as a plain rich text field
- **Review ratings**: `reviewRating` expects a `Rating` object with a `ratingValue` property, but your block stores the rating as a number
- **Nested sub-types**: Any case where a schema property's accepted type has its own properties and your block stores only one of those sub-properties

### JSON Config Format

The `wrapInType` and `wrapInProperty` fields are set per nested mapping entry:

```json
{
  "nestedMappings": [
    {
      "schemaProperty": "acceptedAnswer",
      "contentProperty": "answer",
      "wrapInType": "Answer",
      "wrapInProperty": "Text"
    }
  ]
}
```

- **`wrapInType`** (required for wrapping): The Schema.org type name to create as a wrapper (e.g., `"Answer"`, `"Rating"`)
- **`wrapInProperty`** (optional): The property on the wrapper type to set the value on. If omitted, the resolver infers the best property by:
  1. Exact name match between the content property and the wrapper type's schema properties
  2. Partial/contains match
  3. Fallback to `"Text"`

### Concrete Examples

**FAQ -- wrapping an answer in an Answer type:**

Without wrapping, the answer text would be set directly on `acceptedAnswer`, which is invalid because Schema.org expects an `Answer` object. With wrapping:

```json
{
  "nestedMappings": [
    {
      "schemaProperty": "name",
      "contentProperty": "question"
    },
    {
      "schemaProperty": "acceptedAnswer",
      "contentProperty": "answer",
      "wrapInType": "Answer",
      "wrapInProperty": "Text"
    }
  ]
}
```

**Output:**
```json
{
  "@type": "Question",
  "name": "What is SchemeWeaver?",
  "acceptedAnswer": {
    "@type": "Answer",
    "text": "A community package for Umbraco that generates JSON-LD."
  }
}
```

**Product Review -- wrapping a rating value in a Rating type:**

```json
{
  "nestedMappings": [
    {
      "schemaProperty": "author",
      "contentProperty": "reviewAuthor"
    },
    {
      "schemaProperty": "reviewRating",
      "contentProperty": "ratingValue",
      "wrapInType": "Rating",
      "wrapInProperty": "RatingValue"
    },
    {
      "schemaProperty": "reviewBody",
      "contentProperty": "reviewBody"
    }
  ]
}
```

**Output:**
```json
{
  "@type": "Review",
  "author": "Jane Smith",
  "reviewRating": {
    "@type": "Rating",
    "ratingValue": "5"
  },
  "reviewBody": "Excellent product, highly recommended."
}
```

### Auto-Detection in the Wizard

The nested mapping wizard automatically detects when wrapping is needed. When you select a content property for a complex schema property, the wizard:

1. Checks all accepted types for the schema property
2. Looks for exact or partial name matches between the content property name and the accepted type's sub-properties
3. Falls back to the first accepted type with a `Text` property

The auto-detected wrap type is shown as a badge in the "Wrap In Type" column and can be overridden by clicking the edit button.

---

## Common Patterns

The auto-mapper includes pre-configured resolver configs for popular Schema.org patterns. These are applied automatically when the appropriate schema type and property combination is detected.

### FAQ Question/Answer

**Schema type:** `FAQPage` | **Property:** `mainEntity` | **Nested type:** `Question`

```json
{
  "nestedMappings": [
    {
      "schemaProperty": "name",
      "contentProperty": "question"
    },
    {
      "schemaProperty": "acceptedAnswer",
      "contentProperty": "answer",
      "wrapInType": "Answer",
      "wrapInProperty": "Text"
    }
  ]
}
```

Your BlockList should have a block element type with at least two properties: one for the question text (e.g., `question`) and one for the answer text (e.g., `answer`). The answer is wrapped in an `Answer` type with the value set on the `Text` property.


### Product Review with Rating

**Schema type:** `Product` | **Property:** `review` | **Nested type:** `Review`

```json
{
  "nestedMappings": [
    {
      "schemaProperty": "author",
      "contentProperty": "reviewAuthor"
    },
    {
      "schemaProperty": "reviewRating",
      "contentProperty": "ratingValue",
      "wrapInType": "Rating",
      "wrapInProperty": "RatingValue"
    },
    {
      "schemaProperty": "reviewBody",
      "contentProperty": "reviewBody"
    }
  ]
}
```

Your review block element should have properties for the reviewer's name, a numeric rating value, and the review text. The rating value is wrapped in a `Rating` type.


### Recipe HowToStep

**Schema type:** `Recipe` | **Property:** `recipeInstructions` | **Nested type:** `HowToStep`

```json
{
  "nestedMappings": [
    {
      "schemaProperty": "name",
      "contentProperty": "stepName"
    },
    {
      "schemaProperty": "text",
      "contentProperty": "stepText"
    }
  ]
}
```

Each block element represents a single step with a name and description text. The same pattern applies to `HowTo.step`.


### Recipe Ingredients as String List

**Schema type:** `Recipe` | **Property:** `recipeIngredient` | **Nested type:** *(none)*

```json
{
  "extractAs": "stringList",
  "contentProperty": "ingredient"
}
```

This uses string extraction mode rather than Thing mapping. Each block element's `ingredient` property is read as a plain string, producing a JSON array of strings in the output.

The same pattern applies to `HowTo.tool`:

```json
{
  "extractAs": "stringList",
  "contentProperty": "toolName"
}
```

---

## Block Element Auto-Mapping

SchemeWeaver supports a separate auto-mapping path for block elements that have their own independent schema mappings. The `GenerateBlockElementJsonLdStrings` method on the `JsonLdGenerator`:

1. Loads all enabled schema mappings and indexes them by content type alias
2. Identifies which block properties on the current page are already explicitly mapped as `blockContent` (to avoid duplicate output)
3. Iterates through all BlockList/BlockGrid properties on the current content node
4. For each block element, checks whether its content type alias has a schema mapping
5. If a mapping exists, generates a standalone Thing from the block element using only `property` and `static` source types (block elements have no parents or ancestors)

This means you can create schema mappings for your block element types directly (e.g., mapping a `faqItem` element type to `Question`), and they will be emitted as separate JSON-LD objects on the page. This approach is an alternative to the nested `blockContent` source type and is useful when block elements represent standalone entities.

---

## Troubleshooting Nested Types

### Empty nested objects in JSON-LD output

If a nested type appears as an empty `{}` or is missing entirely:

- Check that the block element's content properties match the aliases specified in your `nestedMappings` config (case-sensitive for property aliases)
- Verify that the block elements have published content with non-null values
- Check the `nestedSchemaTypeName` is a valid Schema.org type name (e.g., `Question`, not `question`)

### String values where objects are expected

If you see `"acceptedAnswer": "The answer text"` instead of a wrapped object:

- Add `wrapInType` and `wrapInProperty` to the nested mapping entry
- The auto-mapper's pre-configured defaults handle common cases, but custom block structures may need manual wrapping configuration

### Block elements not appearing in wizard

If the wizard shows "no block types" in step 1:

- Ensure the BlockList/BlockGrid property has element types configured in its data type settings
- The wizard reads element types from the property's configuration; if this cannot be resolved, you can manually type the block element alias

### Recursion depth limit

Nested resolution is limited to a depth of 3 by default. If you have deeply nested block structures (blocks containing blocks containing blocks), values beyond depth 3 will return null. This is a safety measure to prevent infinite loops.

### Duplicate JSON-LD objects

If the same structured data appears twice on a page:

- Check whether the block element type has both a direct schema mapping (via `GenerateBlockElementJsonLdStrings`) and a `blockContent` mapping on the parent page. The generator explicitly excludes properties already mapped as `blockContent` to avoid this, but verify your mapping configuration if duplicates occur.
