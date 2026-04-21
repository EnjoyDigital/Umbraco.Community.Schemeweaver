# Spec: Default backoffice preview to the main-entity piece

**Status:** rejected (2026-04-21)
**Target:** —
**Related:** v1.4 pieces-based `@graph` model, v1.4 `?scope=site|page|all` filter
**Superseded by:** [backoffice-preview-highlight-current-mapping.md](./backoffice-preview-highlight-current-mapping.md)

## Rejection rationale

1. **"Pre-v1.4 behaviour" isn't a user need.** `@graph` is what the Delivery API emits and what crawlers consume. Splitting the preview into two modes reintroduces the divergence between "what the editor sees" and "what ships" that v1.4 was meant to close.
2. **No reported friction.** This was speculative UX. Editors haven't complained about hunting for their node.
3. **The hunt is small.** 4–5 nodes, each with an obvious `@type`. Scanning is a one-second task, not a cognitive burden worth a toggle + localStorage + two endpoints.
4. **Cheaper alternative wins.** Highlighting the current mapping's node inside the graph view keeps `@graph` canonical, removes the hunt, and ships with one component change. See the superseding spec.

Keeping this file as a record so the idea isn't rediscovered as fresh work.

---


## Context

v1.4 switched the JSON-LD generator from emitting one Thing per mapping to emitting a single `@graph` containing every relevant piece (Organization + WebSite + WebPage + Breadcrumb + main entity + …). The backoffice preview tab on a content item's workspace was updated to call the graph generator too, so what editors see in the preview matches what the Delivery API emits.

That change **regressed the editor experience** for the preview tab. Pre-v1.4 the preview showed "the JSON-LD produced by the mapping you just edited" — one Thing, easy to read, direct feedback on your edit. Post-v1.4 the preview shows the full `@graph` — four or five nodes, the mapping you just edited buried somewhere in the middle, surrounded by Organization / WebSite / Breadcrumb etc. that the editor didn't touch in this session.

Editors lose the tight "I changed property X, here's what changed" loop. They have to hunt for the node corresponding to the current doctype, ignore the rest, and decide whether what they're seeing is a consequence of their edit or ambient site state.

## Proposal

The preview tab defaults to showing **only the main-entity piece** (the Thing produced by the mapping the editor is currently editing), with a toggle to switch to the full `@graph`.

### UI

Add a binary toggle above the preview pane:

```
┌─────────────────────────────────────────────────┐
│  [○ This mapping]  [●  Full graph]              │
├─────────────────────────────────────────────────┤
│  { "@context": "https://schema.org", ... }      │
│                                                 │
└─────────────────────────────────────────────────┘
```

Default selection: **"This mapping"**. Selection persists per-editor (local storage) so editors who prefer the full view aren't re-clicking on every page load.

"This mapping" shows the current doctype's mapped Thing as a standalone JSON-LD document (with `@context`, no `@graph` wrapper). That matches pre-v1.4 behaviour byte-for-byte and is the cheapest mental model: "I edited X, this is what X emits."

"Full graph" shows the complete `@graph` with every piece cross-referenced. Same output as the Delivery API without a scope filter. That's the "does the composition look right across the site" confirmation view.

### API surface

Extend `SchemeWeaverOptions.UseGraphModel` behaviour on the preview controller, or add a dedicated preview scope.

Option A (preferred): preview endpoint accepts a `mode` query parameter.
- `GET /umbraco/management/api/v1/schemeweaver/mappings/{alias}/preview?contentKey=X&mode=mapping`
  → returns `{ jsonLd: "<single Thing JSON>" }` — pre-v1.4 shape.
- `GET …?mode=graph`
  → returns `{ jsonLd: "<full @graph JSON>" }` — current v1.4 behaviour.
- `mode` omitted → `mapping` (default), matching the restored editor experience.

Option B: two endpoints.
- `/preview/mapping` for the Thing-only view.
- `/preview/graph` for the graph view.
Cleaner REST-wise; one more route to maintain. Pick A unless there's a concrete reason otherwise.

### Implementation notes

- `SchemeWeaverService.GeneratePreview` currently switches on `_options.UseGraphModel`. Needs a third input — the caller's requested mode — so:
  - `GeneratePreview(content, culture, PreviewMode.Mapping)` → calls `_generator.GenerateJsonLdString(content, culture)` as in the pre-v1.4 path (single Thing, own `@context`).
  - `GeneratePreview(content, culture, PreviewMode.Graph)` → calls `_graphGenerator.GenerateGraphJson(content, culture, PieceScopeFilter.All)`.
- Existing unit tests stay valid — they test `UseGraphModel=false` (Mapping) and `UseGraphModel=true` (Graph). Rename semantically to test the new mode param. Add a test that the default preview mode is `Mapping`.
- TS side: `jsonld-content-view.element.ts` (the preview component) gets a toggle state + re-fetches on change. Server data source adds a `mode` parameter to the preview request.
- Delivery API + tag helper + Examine index handler **are unchanged** — they continue to emit the graph model. Only the backoffice preview gets the split.

## Why default to "This mapping" and not "Full graph"

1. **Editors are editing one doctype at a time.** Every action that triggers the preview (save mapping, toggle property) was a change to that one mapping. The feedback should match the action.
2. **The full graph is stable across mappings.** Organization / WebSite / Breadcrumb are identical whether you're editing the About page mapping or the Product mapping. Showing them on every preview is noise.
3. **The full graph can't be trusted as a preview of "just my change".** It depends on the site-settings node, breadcrumb trail, primary image detection, other doctypes' mappings. An editor who makes one change and sees the graph flicker in ways they didn't expect will mistrust the preview.
4. **Power users can switch to full graph in one click.** The toggle is trivial. Making the quiet path discoverable is cheap; making the noisy path the default is expensive in editor cognitive load.

## Out of scope / deferred

- **Highlighting the current mapping's node in the full-graph view.** Nice-to-have UX polish (colour border, badge). Not needed for the default-to-mapping fix.
- **Showing a diff between previous and new preview output.** Bigger feature, separate spec.
- **Validating the mapping's output against Schema.NET type contracts.** Separate concern — that's validation, not preview.
- **Preview for pieces other than `main-entity`** (e.g. "just the Organization piece"). If someone asks, trivial to add a piece-key filter, but nobody has asked.

## Acceptance

1. Opening the JSON-LD preview tab on any mapped content item defaults to showing only that doctype's mapped Thing — a single top-level JSON-LD object with its own `@context`. No `@graph` wrapper, no other pieces.
2. A toggle above the preview switches to the full `@graph` view. Selection persists across page reloads for that editor.
3. The tag helper output, Delivery API endpoint output, and Examine index field output are unchanged.
4. Unit tests cover: default mode is Mapping; explicit Mapping mode returns single-Thing JSON; explicit Graph mode returns full `@graph` JSON; switching modes doesn't leak state between requests.

## References

- `src/Umbraco.Community.SchemeWeaver/Services/SchemeWeaverService.cs` — `GeneratePreview` method to extend
- `src/Umbraco.Community.SchemeWeaver/Controllers/SchemeWeaverApiController.cs` — preview endpoint, `mode` query param binding
- `src/Umbraco.Community.SchemeWeaver/App_Plugins/SchemeWeaver/src/workspace-views/jsonld-content-view.element.ts` — preview UI, toggle + persistence
- `src/Umbraco.Community.SchemeWeaver/App_Plugins/SchemeWeaver/src/repository/schemeweaver.server-data-source.ts` — preview fetch call, `mode` parameter
