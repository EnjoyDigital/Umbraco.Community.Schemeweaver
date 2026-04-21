# Spec: Highlight the current mapping's node in the JSON-LD preview

**Status:** proposed
**Target:** v1.4.1 (quick-win follow-up to v1.4 `@graph`)
**Supersedes:** [backoffice-preview-default-to-main-entity.md](./backoffice-preview-default-to-main-entity.md)

## Context

v1.4 changed the backoffice preview to show the full `@graph` so the editor sees exactly what ships via the Delivery API / tag helper / Examine index. Good for correctness, less good for "find my node": the piece produced by the mapping the editor is editing right now is one of several, and it's on the editor to scan `@type` fields to locate it.

The fix isn't to split the preview in two (see the superseded spec). It's to make the current mapping's node **obvious** inside the graph — the graph stays canonical, the hunt disappears.

## Proposal

When the preview is rendered on a content item whose doctype has a mapping, visually emphasise the `@graph` node that corresponds to that mapping, and scroll it into view on first paint / refresh.

### Identifying the "current" node

The preview component already knows:
- The content item's URL / `@id` (via the preview response — nodes in the graph carry `@id`).
- The content type alias, which maps to a schema target type (via `SchemeWeaverContext.requestMapping`).

Match strategy (most specific wins):
1. If the graph contains a node whose `@id` ends with the content item's URL path, pick that one.
2. Otherwise, the node whose `@type` matches the mapped schema target type for the current doctype.
3. Otherwise, do nothing (no highlight, no scroll) — never guess.

Matching by `@id` first handles the case where the site has multiple pieces of the same `@type` (rare but possible).

### Visual treatment

- A left border in `--uui-color-focus` and a faint tinted background across the node's block.
- A small floating "this page" / "this mapping" badge in the top-right corner of the highlighted block, localisable.
- The highlight is decorative only — the JSON is still valid, still copyable byte-for-byte as before. Copy-to-clipboard ignores the decoration.

On load (and after refresh), `scrollIntoView({ block: 'start', behavior: 'smooth' })` on the highlighted node. No-op if the node isn't visible (tab hidden, etc.).

### Implementation sketch

`jsonld-preview.element.ts` currently tokenises the full JSON string and emits a flat token stream inside one `<pre>`. To wrap a subtree we need to know where it starts and ends in character offsets.

Cheapest path:
- Parse the JSON once to find the target node's **path** (e.g. `@graph[3]`).
- Re-stringify with `JSON.stringify(..., 2)` (already the case) and use a second walk to compute the character offset where `@graph[3]` starts and the offset where it closes (`}` at matching depth).
- During tokenisation (or via a lightweight wrapping pass after tokenisation), insert an opening `<span class="json-highlight-start">` at the start offset and a close at the end offset. Tokens inside the range get rendered inside the wrapper as normal.
- Accept the `jsonld-preview` element takes a new optional `highlightedNodeId?: string` property. The content-view element passes the resolved `@id` (or schema `@type` fallback).

Alternative (even cheaper) if precise offset tracking gets hairy: parse into a tree, render `@graph` entries as separate `<pre>` blocks inside the outer envelope, highlight the matching block. Risk: the preview stops being one continuous valid JSON string visually. Prefer the offset approach unless it becomes a time sink.

### Spec'd behaviour, written as tests

1. Graph contains a node with `@id` ending in the current content's URL → that node is highlighted, others are not.
2. Graph contains no URL-matching node but has exactly one node with `@type` equal to the mapping's target → that node is highlighted.
3. Graph has multiple nodes of the mapping's target type and none match by URL → no highlight.
4. Preview is refreshed → highlight re-resolves from the new response (no stale highlight from previous render).
5. `formattedJson` getter (used by copy-to-clipboard) returns the unadorned JSON, identical byte-for-byte to pre-highlight output.
6. Switching cultures in the workspace causes `@id` to change → highlight follows the new `@id`.

### Out of scope

- A "jump to next / previous node" navigation bar.
- Collapse/expand of non-highlighted nodes. If the user asks, add it later — it's a separate feature.
- Highlighting in the Delivery API / tag helper output. This is backoffice-only decoration.
- Diffing between preview refreshes.

## Why this over the rejected toggle

- Keeps the preview byte-for-byte what ships. No divergence between editor view and production.
- One component change, no new endpoint, no `PreviewMode`, no localStorage.
- The cognitive-load argument that motivated the toggle (editors hunting for their node) is resolved directly.
- If it turns out editors still want a "just my mapping" view after this ships, we can revisit — the toggle spec will still be in the git history.

## References

- `src/Umbraco.Community.SchemeWeaver/App_Plugins/SchemeWeaver/src/components/jsonld-preview.element.ts` — tokenisation + rendering, the file that changes
- `src/Umbraco.Community.SchemeWeaver/App_Plugins/SchemeWeaver/src/workspace-views/jsonld-content-view.element.ts` — resolves the current content's `@id` / doctype and passes it to `<schemeweaver-jsonld-preview>`
- `src/Umbraco.Community.SchemeWeaver/Services/JsonLd/JsonLdGraphGenerator.cs` — reference for how `@id`s are composed, so URL matching stays honest
