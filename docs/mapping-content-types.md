# Mapping Content Types

This guide covers the full process of mapping an Umbraco Content Type to a Schema.org type, from choosing the right schema to saving and managing your mappings.

## Overview

Each mapping connects exactly **one Umbraco Content Type** to **one Schema.org type**. Within that mapping, individual property mappings define where each schema property gets its value -- from the current content node, a static string, a related node, block content, or a complex sub-type.

Mappings are created and maintained from the **Schema.org** tab in the document type editor. Open a document type under **Settings > Document Types**, switch to the Schema.org tab, and use **Map to Schema.org** to start a new mapping.

## Step 1: Choose a Schema.org type

When you begin a new mapping, the **Schema.org type picker modal** opens. This modal presents all available Schema.org types (780) discovered from the Schema.NET.Pending library at startup.


### Searching

A search input at the top of the modal lets you filter types by name. The search is debounced (300ms delay) and queries the server, so results update smoothly as you type. For example, typing "product" will surface `Product`, `ProductGroup`, `ProductModel`, and related types.

### Grouped by parent type

Types are organised into groups by their parent type in the Schema.org hierarchy. Each group has a header showing the parent name (e.g. "CreativeWork", "Organization", "Event"), and the types within each group are sorted alphabetically. This grouping helps you find related types -- for instance, `Article`, `BlogPosting`, `NewsArticle`, and `TechArticle` all appear under the `Article` group.

### Type details

Each type in the list shows:

- **Name** -- the Schema.org type name (e.g. `BlogPosting`).
- **Parent** -- displayed as "extends [ParentType]" in italic text (e.g. "extends SocialMediaPosting").
- **Description** -- a brief description of the type from Schema.org.
- **Property count** -- the number of properties defined for this type (including inherited properties).

### Selecting

Click a type to highlight it. The selected type is visually distinguished with a coloured background and border. Click **Select** in the modal footer to confirm your choice and proceed to property mapping. Click **Cancel** to close the modal without creating a mapping.

> **Tip:** If you have the optional `Umbraco.Community.SchemeWeaver.AI` package installed, the **AI Analyse** entity action on a document type can suggest the most appropriate Schema.org type based on the content type's properties. See [AI Integration](ai-integration.md).

## Step 2: Review auto-mapped properties

After selecting a Schema.org type, a **property mapping modal** opens with auto-mapped suggestions.

SchemeWeaver's auto-mapper runs automatically, analysing your content type's properties and suggesting mappings to the schema's properties. The suggestions use three confidence tiers:

| Tier | Score | Matching strategy |
|---|---|---|
| High | 100% | Exact property name match (e.g. `description` to `description`) |
| Medium | 80% | Synonym match (e.g. `title` to `name`, `bodyText` to `articleBody`) |
| Low | 50% | Substring match |

### Smart ordering

The property mapping table uses intelligent ordering to surface the most relevant properties:

1. **Popular Schema.org properties** appear first, in a fixed order: `name`, `headline`, `description`, `image`, `url`, `author`, `datePublished`, `dateModified`, `sku`, `price`.
2. **Mapped properties** (those with a content type property or static value assigned) come next, sorted by confidence score (highest first).

### Adding and removing properties

Only mapped properties (those with auto-mapped suggestions or manually configured values) are shown in the table. To add additional Schema.org properties, use the **Add property** combobox below the table. It presents all remaining schema properties grouped into Popular, Complex Type, and Other categories, with a search filter.

To remove a property row, hover over the schema property name and click the trash icon that appears. Removed properties can be re-added via the combobox at any time.

If no properties are mapped yet (for example, before auto-mapping runs), a hint message appears: "No properties are mapped yet."

### Property table columns

The mapping table has three columns:

| Column | Description |
|---|---|
| **Schema Property** | The Schema.org property name, with its expected type shown below. Complex editor types (Block List, Block Grid, Media Picker, Content Picker, Rich Text) show a badge. |
| **Source** | An icon and label indicating where the value comes from. Click to change via the source origin picker. |
| **Value** | A dropdown of available content type properties, a text input for static values, or nested type configuration -- depending on the source type. Confidence tags (High/Medium/Low) appear alongside auto-mapped values. |


## Step 3: Adjust mappings

You can change any aspect of the suggested mappings before saving.

### Changing the source type

Click the source button on any property row to open the **Source Origin Picker** modal. The available source types are:

| Source type | Description |
|---|---|
| **Current Node** | Map from a property on the current content node. This is the default. |
| **Static Value** | Use a fixed text string that is the same for all content of this type. |
| **Parent Node** | Read a property from the direct parent node. Opens a content type picker to select which parent type, then shows its properties. |
| **Ancestor Node** | Walk up the content tree to find a value. Opens a content type picker. |
| **Sibling Node** | Read from a node at the same level. Opens a content type picker. |
| **Block Content** | Map from Block List or Block Grid items. Used for properties that contain structured repeated data (e.g. FAQ items, review ratings). |
| **Schema.org Type** | Build a complex nested type from multiple content type properties (e.g. map `author` to a `Person` with `name` and `email` sub-properties). |

When you select Parent, Ancestor, or Sibling, a content type picker modal opens so you can specify which content type to read from. Once selected, the property dropdown updates to show that content type's properties.

### Changing the target property

For Current Node, Parent, Ancestor, and Sibling sources, use the property dropdown to select which Umbraco property to read. The dropdown shows all properties from the relevant content type (including built-in properties like `url`, `name`, `createDate`, and `updateDate`, which use a `__` prefix convention internally).

For Static Value, type the fixed string directly into the text input.

### Configuring block content and complex types

Block Content and Schema.org Type sources have additional configuration. These are covered in detail in the property mappings and block content guides.

## Step 4: Save the mapping

Click **Save** in the property mapping modal. SchemeWeaver persists the schema mapping immediately and shows a success notification. The Schema.org tab on the document type editor then shows the saved mapping inline.

Only property rows that have data are saved -- rows where no content type property, static value, or resolver config has been set are excluded from the saved mapping.

## Editing existing mappings

To edit an existing mapping, navigate to the document type and switch to the **Schema.org** tab. If a mapping exists, the schema type name is shown as a tag and all property mappings are listed in the table. Edit the mappings inline and save the document type when you are done.

On the workspace view, you can also click **Auto-map** to re-run the auto-mapper. This merges new suggestions with your existing mappings: if a property already has user-provided data (a content type property alias, static value, or resolver config), the user's choices are preserved and only the confidence score is updated. New schema properties from the suggestions are added as new rows.

> **AI Auto-Map:** When the `Umbraco.Community.SchemeWeaver.AI` package is installed, auto-mapping uses AI to semantically match properties, then merges the results with heuristic suggestions. For each property, the higher-confidence suggestion wins. See [AI Integration](ai-integration.md).


## Inherited schemas toggle

On the workspace view, beneath the Schema Type display, there is an **Inherited** toggle switch with the description: "When enabled, this schema will also be output on all descendant pages."

When enabled:

- The JSON-LD for this mapping is output not only on pages of this content type, but also on every descendant page in the content tree, regardless of the descendant's own content type.
- Inherited schemas are rendered in root-first order, before the page's own schema and before the BreadcrumbList.

This is useful for organisation-level schemas. For example, you might map your "Site Settings" content type to `Organization` and mark it as inherited, so every page on the site includes the organisation's structured data.

## Deleting mappings

To delete a mapping, open the document type's context menu in **Settings > Document Types** and click **Delete Schema.org Mapping**. The mapping and all its property mappings are removed from the database immediately. A success notification ("Mapping deleted successfully") confirms the action, and the Schema.org tab refreshes to show the content type as unmapped.

Deleting a mapping means published pages of that content type will no longer output JSON-LD for that schema type on their next render. Already-cached pages may still show the old output until they are re-rendered or the cache expires.

## Further reading

- **Property Mappings** (property-mappings.md) -- detailed guide to each source type, transforms, confidence scoring, and the property value resolver architecture.
- **Block Content** (block-content.md) -- mapping Block List and Block Grid editors to Schema.org types, including nested type configuration and the nested mapping modal.
- **[AI Integration](ai-integration.md)** -- optional AI-powered schema suggestions, bulk analysis, and Copilot tools.
- **[Getting Started](getting-started.md)** -- installation, tag helper setup, and your first mapping walkthrough.
