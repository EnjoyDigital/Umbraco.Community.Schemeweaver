# SchemeWeaver — Improvement Spec v2

**Origin.** Hardening notes captured while migrating the LS Lettings site (SchemeWeaver **17.6.0** / MCP **1.1.0→1.1.1**) off a frontend JSON-LD hack and onto native nested-block emission. Unlike v1 (`schemeweaver-mcp-improvement-spec.md`), whose asks — `warnings`/warn-on-drop, `reachability`, `get-rendered-json-ld`, base-URL transparency, **nested-block composition** — are now **all shipped** in 17.6.0/1.1.0, this round is about the *next* layer of trust and correctness that real production use exposed.

The theme is unchanged from v1 and worth stating plainly: **the engine is good; the gap is closing the loop without leaving the tool.** Three times this session the MCP said "fine" and only a real `:9001` render (hand-built DB + `curl`) told the truth. Every P1 item below removes one of those out-of-band steps.

**Component key:** `[MCP]` = Node MCP server · `[ENGINE]` = `Umbraco.Community.SchemeWeaver` resolver/generator · `[USYNC]` = `Umbraco.Community.SchemeWeaver.uSync` addon.

---

## What works now (keep — do not regress)

- **Nested-block composition** (`BlockRoute` routes + `ResolveNestedBlockProperty` recursion + Block-Grid area flatten). Verified live: a page `content` grid → `faqBlock`→`faqItem`→`Question` and `numberedServicesBlock`→`numberedServiceItem`→`Service` emit with real values. This is the feature that retired the hack.
- **`warn-on-drop`** on `save-schema-mapping`. Concretely caught `ItemList` mapped under `hasPart` ("accepts CreativeWork… will be dropped. Map it to `About` instead") and let me re-home it to `mentions` before shipping. Best feature in the toolkit. Keep and extend (see P2.1/P2.3 — make more silent-drop classes visible).
- **`reachability`** classification and the honestly-labelled `preview-json-ld` (`context: backoffice-preview`, `resolvedBaseUrl`).
- **Introspection** (`list-content-types`, `get-content-type-properties`, `get-block-element-types` with `nestedBlockElementTypes`). I built the entire `routes` config straight off `get-block-element-types`.

---

## Priority 1 — Close the trust loop (highest leverage)

### P1.1 `save-schema-mapping` must persist to uSync (DB → disk) `[MCP]` `[USYNC]`
**Problem.** `save-schema-mapping` writes the DB only. Mappings don't reproduce to other environments or source control.
**Evidence.** After authoring/validating mappings via the MCP, I still **hand-wrote 12 `.config` files** to get them into the LS repo. The "programmatic bulk write" primitive doesn't reach the artifact that actually deploys.
**Proposed change.** On save, trigger uSync `ExportOnSave` (or expose an `export-mappings-to-usync` MCP tool). If save stays DB-only by design, the response MUST say so explicitly and the export tool MUST exist.
**Acceptance criteria.**
- After `save-schema-mapping`, the `.config` lands on disk in the standard `uSync/v17/SchemeWeaverMappings/` layout with **zero** manual authoring, and reproduces to another environment via normal uSync import.
- `get-all-schema-mappings` flags **disk/DB drift** per mapping (in DB not on disk, or vice-versa).

### P1.2 Real-content preview for block mappings `[MCP]` `[ENGINE]`
**Problem.** `preview-json-ld` against the configured host returns placeholder values for nested blocks — exactly the mappings most likely to be wrong.
**Evidence.** Previewing the `aboutPage` FAQ/Services mapping returned `"mainEntity": "[BlockList: content → ]"`. `isValid:true` proved structure only; I could not see a single real `Question` until I booted a real `:9001` with a populated DB and fetched `by-route`.
**Proposed change.** Let preview resolve a **real published node + block instance** — accept a `contentKey` whose node actually has the block content (and, for element-type previews, a `(pageNodeId, blockInstanceKey)` pair) and render real values. Pair with the sandbox-vs-real clarity in P3.2 so authors know which host the values came from.
**Acceptance criteria.**
- Given a published node containing a `faqBlock`, `preview-json-ld` returns the real `Question` names/answers, not `[BlockList…]` placeholders.
- Output states the node + host the values were resolved from.

### P1.3 uSync mappings must re-sync on every boot, not first-boot-only `[USYNC]`
**Problem.** The addon's import handler imports `SchemeWeaverMappings/*.config` **only when the DB has zero mappings** (`if (existing.Any()) return;`) and registers no uSync handler. On a populated DB, edited configs are silently ignored — config-as-code doesn't reproduce.
**Evidence.** The LS repo had to add a **local composer** (`SchemeWeaverMappingSyncComponent.cs`) that upserts every config on each `UmbracoApplicationStartedNotification`, reusing the addon's `SchemaMappingSerializer`, purely to work around this. Every consumer that wants config-as-code will reinvent that.
**Proposed change.** Ship an idempotent upsert-on-boot (find-or-create by alias + replace property mappings) inside the addon, or register a first-class uSync handler so `uSync/import` applies mapping changes. Remove the need for any consumer-side workaround.
**Acceptance criteria.**
- Editing a committed `.config` and restarting (DB already populated) applies the change with no DB clear and no custom composer.
- A documented one-time reseed path remains for full DB-clear scenarios.

---

## Priority 2 — Emission correctness (found by adversarial review of real output)

### P2.1 Skip empty / degenerate nested Things `[ENGINE]`
**Problem.** A nested block that resolves no usable properties still emits an empty typed node (e.g. a `Question` with neither `name` nor `acceptedAnswer`). Google rejects empty `Question.name` / `Answer.text` — a latent validity regression.
**Evidence.** The retired frontend hack explicitly dropped blank Q/A pairs (`if (name && text)`). The resolver guards empty *string* values per-property but still instantiates the parent Thing, so a blank `faqItem` would emit an invalid empty `Question`. Current content is clean, so it's latent — but it depends on editors never leaving a blank row.
**Proposed change.** After mapping a nested Thing, drop it if it has **no** resolved non-`@type` properties (or if a configurable "required" property is empty). Apply to routed and legacy nested mappings.
**Acceptance criteria.**
- A block list with one fully-populated and one blank item emits exactly one nested Thing.
- A nested Thing with zero resolved properties never appears in the output.

### P2.2 Transforms on nested property mappings `[ENGINE]` `[MCP]`
**Problem.** `NestedPropertyMapping` has no `transformType`. Top-level `PropertyMapping` supports `stripHtml`/`toAbsoluteUrl`/`formatDate`; nested mappings can't.
**Evidence.** `acceptedAnswer.text` emits raw RichText markup (`<p>…</p>`) because there's no `stripHtml` at nest level. The hack stripped it. (Google permits a limited HTML subset, so it's minor — but `class`/heading/disallowed tags can trip Search Console.)
**Proposed change.** Add `transformType` to `NestedPropertyMapping` (and the route `propertyMappings` shape) and apply it in `ResolveBlockElementProperty`. Surface it in the MCP `resolverConfig` schema + docs.
**Acceptance criteria.**
- A nested RichText→`text` mapping with `transformType: "stripHtml"` emits plain text.
- `toAbsoluteUrl`/`formatDate` work the same way nested as at the top level.

### P2.3 Ordered-list (`ListItem` + `position`) support for `itemListElement` `[ENGINE]` `[MCP]`
**Problem.** Mapping a block list into `itemListElement` emits bare nested Things with no order/position. There's no clean way to produce a proper ordered `ItemList` of `ListItem{position, item}`.
**Evidence.** The hack emitted `itemListElement: [{ "@type":"ListItem", position:N, item:{ "@type":"Service" } }]`. Native emits bare `Service[]`. Order is preserved by array sequence, but explicit `position` and the `ListItem` envelope are gone — a semantic downgrade for a *numbered* services block.
**Proposed change.** Add a route/option that wraps each nested block as a `ListItem` with an auto-incremented `position` (and an optional `position`-from-property source), with the mapped Thing under `item`. Expose via `resolverConfig` + describe in `save-schema-mapping`.
**Acceptance criteria.**
- A `numberedServicesBlock` route can emit `ItemList{ itemListElement:[ListItem{position, item:Service}] }` with sequential positions.
- Bare-Thing emission remains the default (no breaking change).

---

## Priority 3 — MCP polish

### P3.1 Finish the generate-shim removal `[MCP]`
**Problem.** v1.1.1 (PR #17) regenerated the Orval client so `warnings`/`reachability` are real generated fields and dropped the `.extend()` shim in `save-schema-mapping`. The **same shim still exists** in `get-schema-mapping` and `get-all-schema-mappings`.
**Proposed change.** Remove both remaining `.extend()` shims now the generated `SchemaMappingDto` carries the fields. (Note: `generate`'s `extract-openapi.mjs` needs a live host at `:44308`; the committed `schemeweaver-openapi.json` was hand-aligned to the C# DTO in #17 — keep them in sync or wire a CI check.)
**Acceptance criteria.** No `.extend()` over generated response schemas remains in the schemeweaver tool collection; build + tests green.

### P3.2 Make the target host unambiguous; allow live-render against a real site `[MCP]`
**Problem.** The MCP points at the TestHost sandbox (`:44308`), which has the content **model** but not the published **tree**, so `get-rendered-json-ld`/`preview` can't reflect real pages. This is the root of P1.2 and a repeat of v1's "preview ≠ live" complaint, one level up.
**Evidence.** `get-server-info` reports `configuredBaseUrl` but not "this is a sandbox with no published content." `/tenants` 404s on the sandbox; only the real `:9001` had the tree.
**Proposed change.** Have `get-server-info` report whether the target has published content (or is a TestHost), and let `get-rendered-json-ld` optionally target a separately-configured **real** Delivery API host so ground-truth verification works from inside the MCP.
**Acceptance criteria.**
- `get-server-info` distinguishes sandbox vs populated site.
- `get-rendered-json-ld` can return the real public render for a route without leaving the MCP.

---

## Suggested order
P1.1 + P1.3 (reproducible config-as-code) → P1.2 (trustable block preview) → P2.1 (validity guard) → P2.2/P2.3 (fidelity) → P3.1/P3.2 (polish). P1.1–P1.3 together are what move SchemeWeaver from "great co-pilot for a Schema.org-literate operator" to "trustworthy on its own."
