# Getting Started

This guide walks you through installing SchemeWeaver, creating your first Schema.org mapping, and verifying the JSON-LD output on your published pages.

## Requirements

| Requirement | Version |
|---|---|
| Umbraco CMS | 17.x **or** 18.x (one stable build per major) |
| .NET | 10 |
| Schema.NET.Pending | 13.0.0 (installed automatically as a dependency) |

SchemeWeaver targets `net10.0` and uses the `Microsoft.NET.Sdk.Razor` SDK. No additional runtime dependencies are required beyond what Umbraco already provides.

## Installation

Install the NuGet package into your Umbraco web project:

```bash
dotnet add package Umbraco.Community.SchemeWeaver
```

Or via the .NET CLI in your solution directory:

```bash
dotnet add src/MyUmbracoSite/MyUmbracoSite.csproj package Umbraco.Community.SchemeWeaver
```

Both Umbraco majors ship as stable packages, so no `--prerelease` flag is needed. NuGet picks the build matching your project's Umbraco major automatically (`17.x` for Umbraco 17, `18.x` for Umbraco 18); see [Umbraco 17 vs 18](../README.md#umbraco-17-vs-18) for how the major-aligned scheme works.

## What happens on first run

On first start after installation, SchemeWeaver sets itself up automatically: the `SchemeWeaverComposer` registers every service with dependency injection, database migrations create the two mapping tables (`SchemeWeaverSchemaMapping` and `SchemeWeaverPropertyMapping`), the `SchemaTypeRegistry` scans the Schema.NET.Pending assembly to discover around 800 Schema.org types, and the backoffice UI (the Schema.org workspace view and the document type entity actions) is served from the package's static web assets. No configuration in `appsettings.json` is needed, and there are no feature flags to enable.

## Adding the tag helper

To output JSON-LD on your rendered pages, add the SchemeWeaver tag helper to your Razor layout. Open your master layout file (typically `Views/Shared/_Layout.cshtml` or `Views/_ViewImports.cshtml`) and add:

```html
@addTagHelper *, Umbraco.Community.SchemeWeaver
```

Then place the tag helper inside your `<head>` element:

```html
<head>
    <meta charset="utf-8" />
    <title>@Model.Name</title>
    <!-- other head elements -->

    <scheme-weaver content="@Model" />
</head>
```

The `content` attribute accepts any `IPublishedContent` instance. On each page render, the tag helper emits a single `<script type="application/ld+json">` element containing a `@graph` document:

```json
{
  "@context": "https://schema.org",
  "@graph": [ ... ]
}
```

The graph holds cross-referenced entities: the site-level Organization and WebSite, the page's BreadcrumbList and primary image, and the page's own mapped entity. The site-level entities are read from a **site settings node** (a published node whose document type is mapped to `Organization` or a subtype); without one, the Organization and WebSite entities are simply absent from the graph. See [The JSON-LD Output Model](json-ld-output.md) for the full model, the `@id` conventions, and the legacy one-script-per-source mode. If nothing at all can be emitted for the page, the tag helper outputs nothing.

### Headless / Delivery API

If you are using Umbraco's Delivery API rather than server-rendered templates, no tag helper is needed: fetch the page's JSON-LD from SchemeWeaver's dedicated endpoint and inject each returned string as a `<script type="application/ld+json">` tag (under the default graph output the array holds a single `@graph` document):

```typescript
const response = await fetch(
  '/umbraco/delivery/api/v2/schemeweaver/json-ld/by-route?route=/my-blog-post',
  { headers: { 'Api-Key': process.env.UMBRACO_DELIVERY_API_KEY! } },
);
const { schemaOrg }: { schemaOrg: string[] } = await response.json();
```

See the [Delivery API guide](delivery-api.md) for the full endpoint surface and a Next.js example.

## Your first mapping

### Step 1: Open a document type

Navigate to **Settings > Document Types** in the Umbraco backoffice. Open the document type you want to map (for example, "Blog Post" or "Product Page") and click the **Schema.org** tab.

### Step 2: Pick a Schema.org type

Click **Map to Schema.org** to open the Schema.org type picker modal.

![The Schema.org tab before a mapping exists](images/schema-tab-empty.png)

Use the search field to find your target type. Types are grouped by their parent type in the Schema.org hierarchy, so `Article`, `BlogPosting`, and `NewsArticle` all appear under the `CreativeWork` group. Each type shows its description and property count to help you choose.

Select a type and click **Select** to proceed.

Not a permanent decision: you can switch to a different Schema.org type later with the **Change** button next to the type tag, keeping the property mappings the new type still accepts. See [Changing the Schema.org type](mapping-content-types.md#changing-the-schemaorg-type).

### Step 3: Review auto-mapped properties

After selecting a Schema.org type, a **property mapping modal** opens. SchemeWeaver's auto-mapper analyses your content type's properties and suggests mappings using three confidence tiers:

| Confidence | Score | How it matches |
|---|---|---|
| High | 100% | Exact property name match (e.g. `description` to `description`) |
| Medium | 80% | Synonym match (e.g. `title` to `name`, `bodyText` to `articleBody`) |
| Low | 50% | Substring match |

The property table uses smart ordering: popular Schema.org properties (`name`, `headline`, `description`, `image`, `url`, `author`, `datePublished`, `dateModified`, `sku`, `price`) appear first, followed by mapped properties sorted by confidence. To add additional schema properties, use the **Add property** combobox below the table. To remove a property, hover over its name and click the trash icon.

### Step 4: Save the mapping

Review the suggested mappings in the modal and adjust any that need changing. You can:

- Change the **source type** (Current Node, Static Value, Parent Node, Ancestor Node, Sibling Node, Block Content, or Schema.org Type)
- Pick a different **content type property** from the dropdown
- Enter a **static value** for properties that should always output the same text

When you are satisfied, click **Save** in the modal. SchemeWeaver persists the mapping immediately. The Schema.org tab on the document type editor then shows the saved mapping inline, where you can continue to edit it.

### Step 5: Publish content and verify

Publish (or re-publish) a piece of content that uses the mapped content type. View the page source in your browser and look for the `<script type="application/ld+json">` element. You should see a `@graph` document containing your mapped entity, similar to:

```json
{
  "@context": "https://schema.org",
  "@graph": [
    {
      "@type": "BlogPosting",
      "@id": "https://example.com/blog/10-tips/#blogposting",
      "headline": "10 Tips for Better SEO",
      "author": {
        "@type": "Person",
        "name": "Jane Smith"
      },
      "datePublished": "2024-01-15"
    }
  ]
}
```

## Verifying your JSON-LD

Once JSON-LD is rendering on your pages, validate it using these tools:

- **[Google Rich Results Test](https://search.google.com/test/rich-results)**: paste a URL or code snippet to see which rich result types Google can extract from your markup.
- **[Schema.org Validator](https://validator.schema.org/)**: validates your JSON-LD against the full Schema.org specification, highlighting any missing required properties or type mismatches.
- **SchemeWeaver's built-in preview**: open any content item that uses a mapped content type, switch to the **JSON-LD** tab, and view the generated JSON-LD with a valid/invalid indicator, copy button, and refresh button.

## Next steps

- **[Quick Start for Developers](quickstart-developer.md)**: the fastest route from install to a full `@graph` on your pages.
- **[Quick Start for SEOs](quickstart-seo.md)**: what the output means for rich results, no code required.
- **[Mapping Content Types](mapping-content-types.md)**: detailed guide to the schema picker, property mapping table, inherited schemas, and deleting mappings.
- **[Property Mappings](property-mappings.md)**: deep dive into source types, transforms, block content mapping, and complex nested types.
- **[Block Content](block-content.md)**: working with Block List and Block Grid editors in your schema mappings.
- **[The JSON-LD Output Model](json-ld-output.md)**: the `@graph` output, graph pieces, the site settings node, and `@id` templates.
- **[Language Variants](language-variants.md)**: culture support for multilingual sites.
