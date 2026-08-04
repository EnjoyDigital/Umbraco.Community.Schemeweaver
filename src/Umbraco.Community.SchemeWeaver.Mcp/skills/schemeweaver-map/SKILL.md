---
name: schemeweaver-map
description: Map an Umbraco content type to Schema.org and emit rich JSON-LD with SchemeWeaver. Use when the user wants to map a content type / document type to structured data, schema.org or JSON-LD, build or improve a SchemeWeaver mapping, or get a page winning Google rich results. Drives the full SchemeWeaver MCP agentic loop (inspect -> pick type -> rank props -> improve on the heuristic -> save -> preview + validate -> fix until clear).
---

# SchemeWeaver — map a content type to Schema.org

Drive the SchemeWeaver MCP tools to produce a **rich** mapping — better than the
name-only heuristic — by reasoning about MEANING, not just matching names. The
goal is JSON-LD that wins Google rich results, then a clean `validate-mapping`.

Requires the `schemeweaver` MCP server (this plugin) connected to a running
Umbraco instance.

## The loop

Work one content type at a time, top to bottom, then loop the last steps until clean.

1. **Inspect the content type** — `get-content-type-properties` (alias, editor,
   description, and `valueSchema` — the stored-value JSON Schema on Umbraco 17.4+).
   The editor is a strong signal: a MediaPicker feeds image-typed props, RichText
   feeds text props (add `transformType: "stripHtml"`), DateTime feeds date props.
   For every Block List / Block Grid property, also call **`get-block-element-types`**
   to learn each block's element types and their inner properties — and check
   `propertyInfos[].nestedBlockElementTypes` for **blocks nested inside blocks**.
2. **Pick the MOST SPECIFIC fitting Schema.org type** — `search-schema-types`.
   Prefer `BlogPosting` over `Article`, `Recipe`/`HowTo`/`Product`/`Event` over a
   generic `CreativeWork`/`WebPage`. Specificity is what unlocks rich results.
3. **Rank the target's properties** — `get-schema-type-properties` with
   `ranked=true`. Map the high-`confidence`/`isPopular`/required props FIRST
   (these are what Google requires/recommends). Don't pad with obscure ones.
4. **Get the heuristic baseline** — `suggest-property-mappings`. Treat it as a
   FLOOR, not the answer: keep its correct rows, fix the wrong ones, and ADD the
   mappings it cannot express (it only does flat name matches — no meaning, units,
   nested objects or block structure).
5. **Draft a rich mapping** using the source-type catalogue and worked examples
   below. Reason semantically: `strapline -> alternativeName`, `intro`/`standfirst
   -> description`, `bodyText -> articleBody`, `heroImage -> image` even when the
   names don't overlap.
6. **Save** — `save-schema-mapping`. It REPLACES the whole mapping, so include
   every row you want to keep. Pass `contentTypeKey` from `list-content-types`.
   Read the response `warnings` (props mapped outside their Schema.org range are
   **silently dropped** — re-home them), and `reachability` /`persistedTo`.
7. **Preview + validate, then FIX-LOOP** — `preview-json-ld` against a real
   content node (get a `contentKey` from the node), and `validate-mapping` for the
   severity-ranked doctor checklist (`critical` > `warning` > `suggestion` >
   `info`). **Re-save and re-validate until `allClear` is true** — i.e. no
   critical/warning/suggestion items remain. Do not stop at the first save.
8. **(Optional) Confirm live** — `get-rendered-json-ld` reads the real
   Delivery-API output as ground truth.

## Simplicity principle

PREFER THE SIMPLEST VALID MAPPING. Use `property` whenever a single content scalar
feeds the schema property — even if Schema.org technically permits a wrapper
object. Only reach for `complexType` when the schema property denotes a distinct
named ENTITY (Person, Organization, Place, PostalAddress, Offer…), the content has
several sub-fields to assemble into one object, or Google specifically requires a
nested object there.

- `Vehicle.brand <- brand`, `JobPosting.baseSalary <- salary` → plain **property**.
  Do NOT wrap a lone scalar in `Brand`/`QuantitativeValue`/`MonetaryAmount`.
- `BlogPosting.author`, `Event.location`, `Event.organizer` → **complexType**
  (these are *things*, not values) — nest even from a single field.

**Name discipline:** map the page's primary title/heading to schema `name`/
`headline`. Map a more specific property (`legalName`, `siteName`, `sku`) only to
its own matching schema property — never to `name`. Emit `alternateName` only when
a genuinely distinct secondary-name property exists; never copy the title into it.

**Built-ins** always available as `property`: `__name`, `__url`, `__createDate`,
`__updateDate`.

## Source-type catalogue

Choosing the right `sourceType` is where you beat the heuristic.

- **`property`** — value straight from a property on this node. The default; use
  for scalars and media unless an entity, block, or related node is genuinely involved.
- **`static`** — a fixed literal (set `contentTypePropertyAlias=null`, put the
  value in `staticValue`).
- **`complexType`** — the schema property expects a nested Schema.org entity. Set
  `nestedSchemaTypeName` and a `resolverConfig` with `complexTypeMappings`.
- **`blockContent`** — the source is a Block List / Block Grid property. Pick the
  shape from the block element-type structure:
  - **stringList** — each block carries essentially ONE meaningful text prop (a
    label): flatten to strings. `resolverConfig {"extractAs":"stringList",
    "contentProperty":"<inner alias>"}`. Use for simple lists (RecipeIngredient,
    Tool, supply, keywords). Do NOT wrap a one-field block in a nested object.
  - **nestedMappings** — blocks have SEVERAL meaningful fields: map each block to
    one nested object. Set `nestedSchemaTypeName` and cover every salient field of
    every allowed element type (titles→Name, sub-headings→Headline, body→Text/
    Description, media→Image). Wrap a sub-value that is itself an entity with
    `wrapInType`/`wrapInProperty`.
  - **routes** — blocks are NESTED (a block contains another Block List/Grid area)
    or different block aliases need different target types: route per block alias,
    recursing. This is the preferred and required shape for nesting.
  - **CONTAINER/LAYOUT BLOCKS:** a Block List/Grid holding the page's body sections
    (aliases like `contentGrid`, `sections`, `blocks`, `rows`, `modules`,
    `components`) IS the page's structural content. Map it to `mainEntity` or
    `hasPart` as `blockContent` with `nestedSchemaTypeName` `WebPageElement` (or
    `routes` when its blocks nest further). Never leave such a container unmapped.
- **`parent` / `ancestor` / `sibling`** — value from a related node up/around the
  tree; set `sourceContentTypeAlias` (for ancestor/sibling). For a grouping prop
  like `category` that names the section the node lives under and has no local
  property, map it from the parent: `sourceType "parent"`, `contentTypePropertyAlias
  "title"` (the parent's name). Also valid as `complexTypeMappings` sub-rows, where
  they resolve relative to the PAGE — except inside a `pickedComplexType` (below),
  where the base node is the picked node and they walk ITS branch of the tree.
- **`reference`** — points at a shared graph piece; set `targetPieceKey`
  (e.g. `organization`, `website`) — used for publisher/author org references.

### Content pickers and multi-node tree pickers

A `Umbraco.ContentPicker` / `Umbraco.MultiNodeTreePicker` property stays
`sourceType "property"` — the picker is a *reference* to another node, and what
you get out of it is decided by `resolverConfig` + `nestedSchemaTypeName`. Four
modes, resolved as a precedence ladder (first configured rung wins):

1. **Drill** — `resolverConfig {"pickedPropertyAlias":"jobTitle"}` reads ONE
   property off the picked node, through the full value pipeline: a drilled
   MediaPicker yields an `ImageObject`, a DateTime formats as ISO 8601, and the
   built-ins (`__name`, `__url`, `__createDate`, `__updateDate`) resolve against
   the PICKED node. Use it when the page needs a single scalar from the pick
   (`author.url` from the author node's `__url`).
2. **Per-usage object** — `resolverConfig {"pickedComplexType":{"selectedSubType":
   "Person","complexTypeMappings":[…]}}`, plus `nestedSchemaTypeName` set to the
   same type. Builds an inline Schema.org object whose sub-rows read the PICKED
   node's properties. The picked type needs NO mapping of its own, so the same
   picked type can be shaped differently on different pages. Use it when this page
   wants a bespoke shape, or the picked type is an element/utility type you would
   not map in its own right.
3. **Whole item** — `nestedSchemaTypeName` only. Renders the picked node via the
   picked content type's OWN saved mapping. Use it when that type already has (or
   deserves) a mapping worth reusing everywhere it is picked — one definition, many
   pages.
4. **Name** (no config) — the picked node's name as a plain string. The fallback,
   and the right answer when the schema property just wants a label (`Person.name`,
   `about`, a bare `keywords` entry) and nothing structured is wanted.

Rungs 1 and 2 never fall back to the name: an explicitly configured drill or object
that resolves nothing emits nothing (a bespoke shape must not silently degrade into
an unrelated string). Set `nestedSchemaTypeName` on any picker row that emits an
object — it is what the range validator checks against the schema property's
accepted types.

**MNTP fan-out:** every picked node runs the same ladder, so several picks emit an
ARRAY. Mixed results are homogenised — Things win and loose strings are dropped —
so keep one mode per row rather than relying on a partial config.

**Pickers as complexType sub-rows:** a sub-row whose `contentTypePropertyAlias` is
itself a picker carries the config in the SUB-ROW's own `resolverConfig` — either
`{"pickedPropertyAlias":"…"}` to drill, or `{"nestedSchemaTypeName":"…"}` to render
the whole picked node via its own mapping (a `complexTypeMappings` entry has no
`nestedSchemaTypeName` field, so for a sub-row the type name travels inside
`resolverConfig`). Leave it off and the sub-row emits only the picked node's name.

## Worked examples

**Recipe.recipeIngredient** — a Block List `ingredients` whose block has one
`ingredient` text prop → flatten to a string list:
```json
{ "schemaPropertyName": "recipeIngredient", "sourceType": "blockContent",
  "contentTypePropertyAlias": "ingredients",
  "resolverConfig": "{\"extractAs\":\"stringList\",\"contentProperty\":\"ingredient\"}" }
```

**HowTo.step** — a Block List `howToSteps` (blocks have `stepName`, `stepText`) →
nested HowToStep objects:
```json
{ "schemaPropertyName": "step", "sourceType": "blockContent",
  "contentTypePropertyAlias": "howToSteps", "nestedSchemaTypeName": "HowToStep",
  "resolverConfig": "{\"nestedMappings\":[{\"schemaProperty\":\"Name\",\"contentProperty\":\"stepName\"},{\"schemaProperty\":\"Text\",\"contentProperty\":\"stepText\"}]}" }
```

**BlogPosting.author** — from a single `authorName` text prop (schema expects a
Person) → complexType, NOT a plain property:
```json
{ "schemaPropertyName": "author", "sourceType": "complexType",
  "contentTypePropertyAlias": "authorName", "nestedSchemaTypeName": "Person",
  "resolverConfig": "{\"complexTypeMappings\":[{\"schemaProperty\":\"Name\",\"sourceType\":\"property\",\"contentTypePropertyAlias\":\"authorName\"}]}" }
```

**Article.author from a content picker** — an `authorNode` Content Picker pointing
at an `authorProfile` doc type (`fullName`, `jobTitle`, `photo`) → a per-usage
`Person` built from the PICKED node, so `authorProfile` needs no mapping of its own:
```json
{ "schemaPropertyName": "author", "sourceType": "property",
  "contentTypePropertyAlias": "authorNode", "nestedSchemaTypeName": "Person",
  "resolverConfig": "{\"pickedComplexType\":{\"selectedSubType\":\"Person\",\"complexTypeMappings\":[{\"schemaProperty\":\"name\",\"sourceType\":\"property\",\"contentTypePropertyAlias\":\"fullName\"},{\"schemaProperty\":\"jobTitle\",\"sourceType\":\"property\",\"contentTypePropertyAlias\":\"jobTitle\"},{\"schemaProperty\":\"image\",\"sourceType\":\"property\",\"contentTypePropertyAlias\":\"photo\"},{\"schemaProperty\":\"url\",\"sourceType\":\"property\",\"contentTypePropertyAlias\":\"__url\"}]}}" }
```
Want only the author's name instead? Drop to a drill:
`"resolverConfig": "{\"pickedPropertyAlias\":\"fullName\"}"` (and leave
`nestedSchemaTypeName` off). Already mapped `authorProfile` to `Person` elsewhere?
Drop the `resolverConfig` entirely and keep `nestedSchemaTypeName: "Person"` — the
picked node then renders through its own mapping. On an MNTP the same row emits an
array of `Person` objects, one per pick.

**Vehicle.brand** — from a `brand` text prop → plain property, NOT a Brand object:
```json
{ "schemaPropertyName": "brand", "sourceType": "property",
  "contentTypePropertyAlias": "brand" }
```

**Container with nested blocks (routes)** — a Block List `chapters` whose `chapter`
block (`chapterTitle`, `lessons`) contains a nested `lessons` Block List of
`lesson`(`lessonTitle`, `lessonBody`) → route, recursing into the inner list:
```json
{ "schemaPropertyName": "hasPart", "sourceType": "blockContent",
  "contentTypePropertyAlias": "chapters",
  "resolverConfig": "{\"routes\":[{\"blockAlias\":\"chapter\",\"nestedSchemaType\":\"CreativeWork\",\"propertyMappings\":[{\"schemaProperty\":\"name\",\"contentProperty\":\"chapterTitle\"},{\"schemaProperty\":\"hasPart\",\"contentProperty\":\"lessons\",\"routes\":[{\"blockAlias\":\"lesson\",\"nestedSchemaType\":\"CreativeWork\",\"propertyMappings\":[{\"schemaProperty\":\"name\",\"contentProperty\":\"lessonTitle\"},{\"schemaProperty\":\"text\",\"contentProperty\":\"lessonBody\"}]}]}]}]}" }
```

## Stopping condition

A mapping is done when `validate-mapping` returns `allClear: true` (no critical/
warning/suggestion items) and `preview-json-ld` shows the high-priority schema
properties populated from a real node. Until then, keep fixing and re-saving.
