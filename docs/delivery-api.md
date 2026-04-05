# Delivery API Integration

SchemeWeaver automatically indexes JSON-LD structured data into Umbraco's Delivery API when content is published. This allows headless and decoupled front-ends to consume Schema.org data without any server-side rendering.

## Overview

When you install SchemeWeaver and map your content types to Schema.org types, the package hooks into Umbraco's content indexing pipeline. Every time content is published, SchemeWeaver generates the JSON-LD and stores it as a `schemaOrg` field on the indexed content. Your front-end can then fetch this field via the Delivery API and inject it into the page's `<head>`.

No additional configuration is required -- if a mapping exists and is enabled, the JSON-LD is indexed automatically.

## How It Works

SchemeWeaver registers a `SchemaJsonLdContentIndexHandler` that implements Umbraco's `IContentIndexHandler` interface. This handler is called during content indexing and performs the following steps:

1. **Resolves the published content** from the Umbraco content cache using the content's key.
2. **Generates inherited schemas** -- walks up the ancestor chain from the parent node, collecting JSON-LD from any content types marked as "inherited" (e.g. a `WebSite` schema on the home page). These are returned in root-first order.
3. **Generates the main page schema** -- produces the JSON-LD for the current content node based on its mapping.
4. **Generates block element schemas** -- scans BlockList and BlockGrid properties for block elements that have their own schema mappings, and generates JSON-LD for each.

All generated JSON-LD strings are stored in a single indexed field:

| Field | Type | Description |
|---|---|---|
| `schemaOrg` | `StringRaw` | Array of JSON-LD strings (one per schema block). Not analysed or tokenised -- stored as raw strings. |

The `StringRaw` field type ensures the JSON-LD is stored verbatim without any search index processing, so it is returned exactly as generated.

### What Is Included

- The current page's own schema (if mapped and enabled)
- Inherited schemas from ancestor nodes (e.g. `WebSite`, `Organization`) in root-first order
- Schemas from mapped block elements within BlockList/BlockGrid properties

### What Is NOT Included

- **BreadcrumbList** -- breadcrumb structured data is deliberately excluded from the Delivery API index. Breadcrumbs depend on the URL routing structure, which a headless front-end controls independently. Use the tag helper for server-rendered breadcrumbs, or build your own `BreadcrumbList` from your front-end's routing data.

## Consuming via the Delivery API

The `schemaOrg` field is available on Delivery API content responses. Here is how to fetch and use it.

### Fetching Content with JSON-LD

```typescript
// Fetch a content item by route
const response = await fetch(
  'https://your-site.com/umbraco/delivery/api/v2/content/item/about-us',
  {
    headers: {
      'Accept': 'application/json',
    },
  }
);

const data = await response.json();

// The schemaOrg field contains an array of JSON-LD strings
const jsonLdBlocks: string[] = data.properties?.schemaOrg ?? [];
```

### JavaScript: Injecting into the DOM

```javascript
function injectJsonLd(jsonLdBlocks) {
  for (const jsonLd of jsonLdBlocks) {
    const script = document.createElement('script');
    script.type = 'application/ld+json';
    script.textContent = jsonLd;
    document.head.appendChild(script);
  }
}

// Usage
const response = await fetch('/umbraco/delivery/api/v2/content/item/about-us');
const data = await response.json();
injectJsonLd(data.properties?.schemaOrg ?? []);
```

### TypeScript: Typed Fetch Helper

```typescript
interface DeliveryApiContent {
  name: string;
  route: { path: string };
  properties: {
    schemaOrg?: string[];
    [key: string]: unknown;
  };
}

async function fetchContentWithSchema(path: string): Promise<DeliveryApiContent> {
  const response = await fetch(
    `/umbraco/delivery/api/v2/content/item${path}`
  );

  if (!response.ok) {
    throw new Error(`Failed to fetch content: ${response.status}`);
  }

  return response.json();
}

// Usage
const content = await fetchContentWithSchema('/about-us');
const schemas = content.properties.schemaOrg ?? [];
console.log(`Found ${schemas.length} JSON-LD block(s)`);
```

## Next.js Integration

In a Next.js application, you can render the JSON-LD as `<script>` tags in the document head. Below is an example using the App Router.

### Page Component with JSON-LD

```tsx
// app/[[...slug]]/page.tsx
import { Metadata } from 'next';

interface UmbracoContent {
  name: string;
  properties: {
    schemaOrg?: string[];
    [key: string]: unknown;
  };
}

async function getContent(slug: string[]): Promise<UmbracoContent> {
  const path = '/' + (slug?.join('/') ?? '');
  const res = await fetch(
    `${process.env.UMBRACO_URL}/umbraco/delivery/api/v2/content/item${path}`,
    { next: { revalidate: 60 } }
  );

  if (!res.ok) throw new Error('Content not found');
  return res.json();
}

export default async function Page({ params }: { params: { slug?: string[] } }) {
  const content = await getContent(params.slug ?? []);
  const jsonLdBlocks = content.properties.schemaOrg ?? [];

  return (
    <>
      {/* Inject JSON-LD into the head */}
      {jsonLdBlocks.map((jsonLd, index) => (
        <script
          key={`jsonld-${index}`}
          type="application/ld+json"
          dangerouslySetInnerHTML={{ __html: jsonLd }}
        />
      ))}

      {/* Page content */}
      <main>
        <h1>{content.name}</h1>
        {/* ... render your content ... */}
      </main>
    </>
  );
}
```

### Dedicated JSON-LD Component

For reuse across layouts, extract a component:

```tsx
// components/JsonLd.tsx
interface JsonLdProps {
  blocks: string[];
}

export function JsonLd({ blocks }: JsonLdProps) {
  if (blocks.length === 0) return null;

  return (
    <>
      {blocks.map((jsonLd, index) => (
        <script
          key={`jsonld-${index}`}
          type="application/ld+json"
          dangerouslySetInnerHTML={{ __html: jsonLd }}
        />
      ))}
    </>
  );
}
```

```tsx
// In your layout or page:
import { JsonLd } from '@/components/JsonLd';

// ...
<JsonLd blocks={content.properties.schemaOrg ?? []} />
```

### Handling Inherited Schemas in Layouts

Inherited schemas (e.g. `WebSite` or `Organization` on the root node) are already included in every descendant page's `schemaOrg` field. You do not need to fetch the root content separately -- SchemeWeaver walks the ancestor chain at indexing time and includes inherited schemas in the correct order.

## BreadcrumbList Considerations

BreadcrumbList structured data is generated by the server-side tag helper only (`<scheme-weaver content="@Model" />`). It is not included in the Delivery API index because:

- Headless front-ends define their own URL structure and routing, which may differ from the Umbraco content tree.
- Breadcrumb trails should reflect the user's navigation path, which is a front-end concern.

If you need BreadcrumbList in a headless setup, build it from your front-end's routing data:

```typescript
function buildBreadcrumbJsonLd(crumbs: { name: string; url: string }[]) {
  return JSON.stringify({
    '@context': 'https://schema.org',
    '@type': 'BreadcrumbList',
    itemListElement: crumbs.map((crumb, index) => ({
      '@type': 'ListItem',
      position: index + 1,
      name: crumb.name,
      item: crumb.url,
    })),
  });
}
```

## Example Output

The Delivery API returns the same JSON-LD content as raw strings in the `schemaOrg` field.

## Field Ordering

The `schemaOrg` array follows a consistent order:

1. **Inherited schemas** (root-first) -- e.g. `WebSite` from home page, then any intermediate inherited schemas
2. **Main page schema** -- the schema for the current content node
3. **Block element schemas** -- schemas from mapped BlockList/BlockGrid elements

This ordering ensures that broader context (site-level schemas) appears before page-specific detail, which is the recommended pattern for structured data.
