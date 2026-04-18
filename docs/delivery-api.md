# Delivery API Integration

SchemeWeaver exposes JSON-LD structured data to headless consumers through a dedicated
Delivery API endpoint, alongside writing the same data into Umbraco's Examine-backed content
index (useful for filter/sort/search).

> **⚠️ Breaking change in 1.3.0**
>
> Earlier versions of this document claimed `schemaOrg` appeared on standard Delivery API
> content responses under `properties.schemaOrg`. That was never surfaced — `IContentIndexHandler`
> feeds the Examine index only; Umbraco builds the response body's `properties` dict from
> `IPublishedContent.Properties`, so index-handler fields never reach the API consumer.
>
> As of 1.3.0 `schemaOrg` is served from a dedicated endpoint instead:
> `GET /umbraco/delivery/api/v2/schemeweaver/json-ld`.
> Consumers fetch it in parallel with their content fetch and inject the strings into
> `<script type="application/ld+json">` tags. See the Next.js example below.

## Overview

When you map a doc type to a Schema.org type, SchemeWeaver generates JSON-LD at request time
via `IJsonLdBlocksProvider` and caches the result in-process. The provider is invalidated by
publish / unpublish / move / delete notifications, so the cache stays fresh without any
manual busting.

Each response contains a string array in the documented order:

1. Inherited schemas from ancestor nodes (root-first). E.g. a `WebSite` mapping on Home with
   `IsInherited = true` appears on every descendant.
2. `BreadcrumbList` derived from the content tree (opt-out via config — see below).
3. The current page's own schema.
4. Schemas from mapped block elements (BlockList / BlockGrid).

## Endpoints

Both endpoints return:

```json
{
  "schemaOrg": [
    "{\"@context\":\"https://schema.org\",\"@type\":\"WebSite\",...}",
    "{\"@context\":\"https://schema.org\",\"@type\":\"BreadcrumbList\",...}",
    "{\"@context\":\"https://schema.org\",\"@type\":\"Article\",...}"
  ]
}
```

### `GET /umbraco/delivery/api/v2/schemeweaver/json-ld?id={guid}[&culture={string}]`

Resolves by content key. Use when your consumer already has the Delivery API content
response (and therefore the `id`).

### `GET /umbraco/delivery/api/v2/schemeweaver/json-ld/by-route?route={string}[&culture={string}]`

Resolves by route path. Convenient for SSR consumers that know the URL but not the key yet.
`route` is normalised to start with `/`.

### Auth

The endpoint honours the same Api-Key protection as the rest of the Delivery API — when
`Umbraco:CMS:DeliveryApi:PublicAccess` is `false`, callers must send the `Api-Key` header.
Preview requests additionally require preview access.

- `200 OK` with payload on success.
- `400 Bad Request` for a missing or empty `route`/`id`.
- `401 Unauthorized` when Api-Key is missing or invalid.
- `404 Not Found` when the key or route doesn't resolve to published content.

## Configuration

```json
{
  "SchemeWeaver": {
    "EmitBreadcrumbsInDeliveryApi": true,
    "CacheDuration": "00:30:00"
  }
}
```

- **`EmitBreadcrumbsInDeliveryApi`** (default `true`) — when `false`, the `BreadcrumbList`
  block is not included. Useful when your headless front-end has a URL structure that
  diverges from the Umbraco content tree, so you'd rather build the breadcrumb client-side
  from your routing data.
- **`CacheDuration`** (default `00:30:00`) — absolute cache expiration per `(content key,
  culture)` entry. Acts as a safety-net only; real invalidation is event-driven via
  `ContentPublished` / `ContentUnpublished` / `ContentMoved` / `ContentMovedToRecycleBin` /
  `ContentDeleted` notifications. Tune longer if your publish cadence is high and you're
  confident the invalidation handlers cover your use cases.

## Examine index field

SchemeWeaver also writes the same block array into the Delivery API Examine content index
under the `schemaOrg` field. This is **only** useful if you want to filter / sort / search
content by its JSON-LD payload at the Delivery API query layer (e.g.
`/content?filter=schemaOrg:\"@type:WebSite\"`). The response body of that query will still
not include the field — use the dedicated endpoint described above to read the data.

## Consuming the endpoint

### Vanilla fetch

```typescript
const response = await fetch(
  '/umbraco/delivery/api/v2/schemeweaver/json-ld/by-route?route=/about',
  {
    headers: {
      'Accept': 'application/json',
      'Api-Key': process.env.UMBRACO_DELIVERY_API_KEY!,
    },
  }
);

if (!response.ok) return; // 401/404 etc. — render page without JSON-LD

const { schemaOrg }: { schemaOrg: string[] } = await response.json();
```

### JavaScript: injecting into the DOM

```javascript
function injectJsonLd(blocks) {
  for (const jsonLd of blocks) {
    const script = document.createElement('script');
    script.type = 'application/ld+json';
    script.textContent = jsonLd;
    document.head.appendChild(script);
  }
}
```

## Next.js (App Router) integration

The endpoint is ISR-friendly. Fetch the content and the JSON-LD in parallel from a server
component; render `<script type="application/ld+json">` tags inline.

```tsx
// app/[[...slug]]/page.tsx
import type { Metadata } from 'next';
import { notFound } from 'next/navigation';

interface ContentResponse {
  id: string;
  name: string;
  properties: Record<string, unknown>;
}
interface SchemaOrgResponse {
  schemaOrg: string[];
}

async function getContent(path: string): Promise<ContentResponse> {
  const res = await fetch(
    `${process.env.UMBRACO_URL}/umbraco/delivery/api/v2/content/item/${path.replace(/^\//, '')}`,
    {
      headers: { 'Api-Key': process.env.UMBRACO_DELIVERY_API_KEY! },
      next: { revalidate: 60, tags: ['umbraco:all'] },
    },
  );
  if (!res.ok) throw new Error('content-not-found');
  return res.json();
}

async function getSchemaOrg(path: string): Promise<string[]> {
  const res = await fetch(
    `${process.env.UMBRACO_URL}/umbraco/delivery/api/v2/schemeweaver/json-ld/by-route?route=${encodeURIComponent(path)}`,
    {
      headers: { 'Api-Key': process.env.UMBRACO_DELIVERY_API_KEY! },
      next: { revalidate: 60, tags: ['umbraco:all'] },
    },
  );
  if (!res.ok) return []; // degrade gracefully when schemaOrg is unavailable
  const data: SchemaOrgResponse = await res.json();
  return data.schemaOrg ?? [];
}

export default async function Page({ params }: { params: { slug?: string[] } }) {
  const path = '/' + (params.slug?.join('/') ?? '');

  const [content, schemaOrg] = await Promise.all([
    getContent(path).catch(() => null),
    getSchemaOrg(path),
  ]);
  if (!content) notFound();

  return (
    <>
      {schemaOrg.map((jsonLd, i) => (
        <script
          key={`jsonld-${i}`}
          type="application/ld+json"
          dangerouslySetInnerHTML={{ __html: jsonLd }}
        />
      ))}
      <main>
        <h1>{content.name}</h1>
        {/* ... render blocks ... */}
      </main>
    </>
  );
}
```

### Reusable component

```tsx
// components/JsonLd.tsx
interface JsonLdProps {
  blocks: string[];
}

export function JsonLd({ blocks }: JsonLdProps) {
  if (blocks.length === 0) return null;
  return (
    <>
      {blocks.map((jsonLd, i) => (
        <script
          key={`jsonld-${i}`}
          type="application/ld+json"
          dangerouslySetInnerHTML={{ __html: jsonLd }}
        />
      ))}
    </>
  );
}
```

### Site-level JSON-LD for non-CMS routes

Pages that aren't backed by a CMS node (custom listing pages, forms, etc.) can still render
the inherited site schemas by fetching the root:

```tsx
// app/layout.tsx
const siteBlocks = await getSchemaOrg('/');
// render <JsonLd blocks={siteBlocks} /> in the document shell
```

CMS pages that call `getSchemaOrg(path)` for their own route automatically include the
inherited Website block already, so you need to dedupe across the layout + page boundary (a
simple string-set equality works — SchemeWeaver serializes deterministically).

## BreadcrumbList considerations

`BreadcrumbList` is derived from the Umbraco content tree and uses `IPublishedUrlProvider`
to build each `ListItem.item` URL. If your front-end has its own routing on top of a flatter
URL scheme, those URLs will point to the Umbraco-hosted paths, not your consumer URLs. Two
options:

1. Keep emission on and rewrite the URLs client-side (parse the block, replace the
   `item` URLs, serialize back).
2. Turn emission off (`SchemeWeaverOptions.EmitBreadcrumbsInDeliveryApi = false`) and build
   the breadcrumb client-side from your own routing data:

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
