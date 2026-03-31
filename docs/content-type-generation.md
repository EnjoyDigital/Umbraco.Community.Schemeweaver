# Content Type Generation

SchemeWeaver can generate Umbraco document types directly from Schema.org type definitions. This is useful when you are building content structures specifically to support structured data -- rather than retrofitting schema mappings onto existing content types, you can start from a Schema.org type and let SchemeWeaver create a document type with the right properties already in place.

---

## Overview

The content type generator:

- Reads the full property list from a Schema.org type via the `SchemaTypeRegistry`
- Creates an Umbraco `ContentType` with properties mapped to appropriate Umbraco property editors
- Organises properties into sensible groups (Content, Metadata, SEO)
- Generates camelCase aliases and friendly display names from Schema.org property names

The generated document type is a starting point. It is **not** automatically mapped to the Schema.org type -- you still need to create a schema mapping separately after generation. This separation is intentional: you may want to adjust the document type, add additional properties, or modify the property editors before setting up the mapping.

---

## How to Access

Content type generation is available as an entity action on the **Document Types** tree in the Umbraco backoffice Settings section.

1. Navigate to **Settings > Document Types**
2. Right-click on any document type (or the Document Types root node) to open the context menu
3. Select **Generate from Schema.org**

This opens the generation modal. The entity action resolves the document type's GUID to an alias internally, but the generation process creates a brand new document type -- it does not modify the one you right-clicked on.

---

## Step-by-Step Guide

### Step 1: Select a Schema.org Type

The modal opens with a searchable list of all available Schema.org types (loaded from the Schema.NET assembly via `SchemaTypeRegistry`). Each entry shows the type name and description.

Use the search input to filter the list. The search is debounced (300ms delay) and queries the server for matching types.

Click on a type to select it and advance to the property selection step.

### Step 2: Configure Name and Alias

After selecting a schema type, two fields are pre-populated:

- **Content Type Name** -- defaults to the Schema.org type name (e.g., `Recipe`)
- **Content Type Alias** -- defaults to the camelCase version (e.g., `recipe`)

You can edit both fields. The alias must be unique -- the generator will reject the request if a content type with the same alias already exists.

### Step 3: Select Properties

All properties of the selected Schema.org type are listed with checkboxes. By default, all properties are selected. Each property shows its Schema.org type (e.g., `Text`, `URL`, `DateTime`).

Deselect properties you do not need. Only selected properties will be created on the document type.

> **Tip:** You do not need every Schema.org property. Focus on the properties you actually intend to populate with content. You can always add more properties later.

### Step 4: Generate

Click **Generate** to create the document type. The generator:

1. Validates that no content type with the specified alias already exists
2. Creates a new `ContentType` with the alias, name, and a description noting it was generated from Schema.org
3. For each selected property, resolves the appropriate Umbraco property editor, finds a matching data type, and adds the property to the content type
4. Saves the content type via Umbraco's `IContentTypeService`

A success notification appears when the document type has been created.

---

## Property Editor Mapping

The generator maps Schema.org property types to Umbraco property editors using a fixed mapping table:

| Schema.org Type | Umbraco Property Editor |
|---|---|
| `Text` | Textbox (`Umbraco.TextBox`) |
| `String` | Textbox (`Umbraco.TextBox`) |
| `URL` | Textbox (`Umbraco.TextBox`) |
| `Uri` | Textbox (`Umbraco.TextBox`) |
| `Date` | Date/Time (`Umbraco.DateTime`) |
| `DateTime` | Date/Time (`Umbraco.DateTime`) |
| `Number` | Numeric (`Umbraco.Integer`) |
| `Int32` | Numeric (`Umbraco.Integer`) |
| `Integer` | Numeric (`Umbraco.Integer`) |
| `Boolean` | True/False (`Umbraco.TrueFalse`) |

Any Schema.org type not in this table (including complex types like `Person`, `Organization`, `ImageObject`) defaults to **Textbox**. If you need a richer editor (e.g., Media Picker for `image`, Rich Text for `articleBody`), you should change the property editor manually after generation.

The generator also strips nullable markers (`?`) from type names before looking up the mapping.

### Data Type Resolution

For each editor alias, the generator queries `IDataTypeService.GetByEditorAliasAsync` and uses the first matching data type. This means it will use your existing data type configurations (e.g., if you have a custom "Date Only" data type using the DateTime editor, it may be selected).

If no data type is found for a given editor alias, the property is skipped with a warning logged.

---

## Property Groups

Properties are organised into groups on the generated document type. A set of well-known property names are assigned to specific groups:

| Property Name | Group |
|---|---|
| `name` | Content |
| `headline` | Content |
| `description` | Content |
| `articleBody` | Content |
| `image` | Content |
| `url` | SEO |
| `keywords` | SEO |
| `datePublished` | Metadata |
| `dateModified` | Metadata |
| `author` | Metadata |
| `inLanguage` | Metadata |

All other properties are placed in the default group, which is `"Content"` unless a different `PropertyGroupName` is specified in the request.

---

## Property Naming

Schema.org property names are transformed for Umbraco:

- **Alias** -- converted to camelCase (first character lowered). E.g., `RecipeYield` becomes `recipeYield`
- **Display Name** -- split on capital letters with spaces inserted. E.g., `articleBody` becomes `Article Body`
- **Description** -- set to `Schema.org: {propertyName} ({propertyType})` for reference

---

## What to Do After Generation

The generated document type is a scaffold. There are several steps you will typically want to take:

### 1. Review and Adjust Property Editors

The automatic editor mapping is conservative -- most properties default to Textbox. Consider changing:

- `image` to a **Media Picker** (`Umbraco.MediaPicker3`)
- `articleBody` or `description` to **Rich Text** (`Umbraco.RichText`)
- `url` to a **URL Picker** (`Umbraco.MultiUrlPicker`) if you want a link picker rather than free text
- Properties expecting lists (e.g., `recipeIngredient`) to a **Block List** (`Umbraco.BlockList`) or **Multiple Textstring** (`Umbraco.MultipleTextstring`)

### 2. Configure Document Type Settings

The generated document type has:

- `AllowedAsRoot` set to `false`
- Icon set to `icon-science`
- No allowed child types configured
- No template assigned

Adjust these settings as needed for your content architecture.

### 3. Add Compositions or Inheritance

If you have shared property groups (e.g., SEO fields, metadata), consider moving properties into compositions rather than duplicating them across generated types.

### 4. Create a Schema Mapping

The generated document type does **not** have a schema mapping created automatically. To start generating JSON-LD:

1. Navigate to the SchemeWeaver dashboard in the Settings section
2. Find your new document type in the list
3. Click to create a mapping, selecting the Schema.org type you generated from
4. Use the auto-mapper to quickly match the generated properties to their Schema.org counterparts -- since the properties were named after Schema.org properties, the exact-match tier (confidence 100) should match most of them automatically

### 5. Create Content

Create content nodes using the new document type and publish them. SchemeWeaver will generate JSON-LD automatically for any mapped content types when pages are rendered.
