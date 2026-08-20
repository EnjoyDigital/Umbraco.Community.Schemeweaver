# Quick Start for Developers

Get SchemeWeaver installed, rendering, and emitting a full JSON-LD graph in about fifteen minutes.

## 1. Install

```bash
dotnet add package Umbraco.Community.SchemeWeaver
```

NuGet resolves the right package line for your site automatically: 17.x targets Umbraco 17, 18.x targets Umbraco 18. See the [README](../README.md#umbraco-17-vs-18) for the compatibility matrix. On first run SchemeWeaver creates its two database tables via a standard migration and registers the backoffice UI; there is nothing to configure up front.

## 2. Render the tag helper

Add the tag helper to your layout, typically in `<head>`:

```cshtml
@addTagHelper *, Umbraco.Community.SchemeWeaver
<scheme-weaver content="@Model" />
```

Nothing renders yet for unmapped pages: SchemeWeaver never breaks or bloats a page it has nothing to say about.

## 3. Map your first document type

1. In the backoffice, go to **Settings, then Document Types**, and open a type (say, your blog post type).
2. Open the **Schema.org** tab and select **Map to Schema.org**.
3. Pick a type. The picker opens on a curated shortlist; search for anything else (around 800 types are available). Choose the most specific fit: `BlogPosting` beats `Article`.
4. Select **Auto-map** to let the heuristic mapper suggest property mappings, then adjust. Each row maps one Schema.org property to a source on your content.
5. Save.

![The Schema.org tab with a completed property mapping table](images/mapping-table.png)

## 4. Create the site settings node

The graph's site-level entities (`Organization` and `WebSite`) come from a singleton settings node:

1. Create a document type with alias `schemaSiteSettings` holding your organisation name, logo and social links.
2. Publish one node of it anywhere in the content tree.
3. Map that document type to `Organization` on its Schema.org tab.

Skip this and pages still emit their own entity and breadcrumb, but the Organization and WebSite entities will be absent. Details in [The JSON-LD Output Model](json-ld-output.md).

## 5. See the output

View source on a published page of the mapped type. You should see a single script element:

```html
<script type="application/ld+json">
{
  "@context": "https://schema.org",
  "@graph": [
    { "@type": "Organization", "@id": "https://example.com/#organization", "...": "..." },
    { "@type": "WebSite", "@id": "https://example.com/#website", "...": "..." },
    { "@type": "BreadcrumbList", "@id": "https://example.com/blog/my-post/#breadcrumb", "...": "..." },
    { "@type": "BlogPosting", "@id": "https://example.com/blog/my-post/#blogposting", "...": "..." }
  ]
}
</script>
```

One graph, entities cross-referenced by `@id`, the same shape Yoast emits. If you prefer one script per entity, set `SchemeWeaver:UseGraphModel` to `false`; both modes are supported long-term.

## 6. Verify

- **In the backoffice**: every content node of a mapped type gains a **JSON-LD** tab showing a live preview plus validation against Google Rich Results rules (Critical, Warning, Info and Suggestion severities).
- **Externally**: paste a published URL into [Google's Rich Results Test](https://search.google.com/test/rich-results) or the [Schema.org Validator](https://validator.schema.org/).

![The JSON-LD preview tab on a content node](images/jsonld-preview-tab.png)

## Headless?

The same output is available through the Delivery API:

```
GET /umbraco/delivery/api/v2/schemeweaver/json-ld?id={key}&culture=en-US
```

See [Delivery API](delivery-api.md).

## Next steps

- [Property Mappings](property-mappings.md): all eight source types, transforms and resolvers
- [Block Content](block-content.md): structured objects from Block List and Block Grid
- [The JSON-LD Output Model](json-ld-output.md): graph pieces, `@id` templates, legacy mode
- [Language Variants](language-variants.md): multilingual sites
- [MCP Server](mcp-server.md): let an AI assistant build your mappings for you
- Handing over to an editor or SEO? Point them at the [Quick Start for SEOs](quickstart-seo.md)
