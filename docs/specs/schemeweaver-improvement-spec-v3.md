# SchemeWeaver — Improvement Spec v3

**Origin.** v2 (`schemeweaver-improvement-spec-v2.md`) proposed ~8 fixes. Verifying it against the actual source proved most of v2 **already exists** — implemented on branch `feature/spec-v2-improvements` (commit `d913368`, unreleased; builds as **17.7.0 / 18.2.0**). The genuine lesson is not "build these features" — it's that a capable agent (me) **shipped sub-optimal JSON-LD with every needed feature already present**, because the features are off-by-default or only visible in the C#. v3 is therefore about **discoverability and defaults**, plus the small genuine remainders.

**Empirical verification (this round).** Ran the engine's own unit tests on `d913368` — **59 passed, 0 failed**, including `Apply_StripHtml_RemovesTagsAndTrims`, **`Resolve_NestedMapping_StripHtml_EmitsPlainText`**, `Resolve_RoutedConfig_WrapInListItem_EmitsSequentialListItems`, `Resolve_WrapInListItem_WithPositionProperty_UsesExplicitPositions`, `Resolve_RoutedConfig_RequiredPropertyMissing_DropsThing`, `Resolve_LegacyConfig_BlankBlock_IsDropped`. So `stripHtml` on nested block answers, ListItem/position wrapping, and empty-Thing dropping all **work today**.

**Component key:** `[MCP]` Node server · `[ENGINE]` `Umbraco.Community.SchemeWeaver` · `[USYNC]` uSync addon.

---

## 1. Erratum to v2 — these already exist (with proof)

| v2 said "missing/build it" | Reality on `d913368` | Proof (file:line) | How to use it |
|---|---|---|---|
| Nested `stripHtml`/transforms | **Exists** — `TransformType` on nested mappings, applied before the empty guard | `BlockContentResolver.cs:712` (field), `:364`/`:410` (apply) → `SchemaValueTransformer.cs:58` | Add `"transformType":"stripHtml"` to the nested `propertyMappings[]` entry |
| Skip empty nested Things (W3) | **Exists** — drops a Thing with no resolved props, and one missing a required prop | `BlockContentResolver.cs:281-289`, detection `:298-309` | Automatic; set `requiredProperties` on a route to enforce e.g. `acceptedAnswer` |
| `ListItem`/`position` for ordered lists (W2) | **Exists** — `WrapInListItem` + auto-increment + `positionProperty` | `BlockContentResolver.cs:130-156`, entry `:116` | Set `"wrapInListItem":true`, optional `"positionProperty":"number"` |
| Real-content preview | **Exists** — preview accepts a published node's `contentKey` | `SchemeWeaverApiController.cs:336-347`; service `SchemeWeaverService.cs:205-244` | Pass a real `contentKey` to `preview-json-ld` |
| uSync export-on-save + drift | **Exists** — gated, plus `persistedTo`/`driftStatus` on the response | `SchemaMappingExportNotificationHandler.cs:44-81` (gate `:81`), `SchemeWeaverService.cs:172,178-181` | Enable `SchemeWeaverOptions.ExportMappingsToUSyncOnSave` |
| Re-sync on boot / first-class uSync | **Exists** — boot-import modes + a uSync handler | `SchemaMappingImportComponent.cs:77-83`, `SchemaMappingHandler.cs:51-96` | Set `USyncBootImport` mode; use Import/Export All |
| Sandbox-vs-real clarity | **Exists** — `get-server-info` reports `hasPublishedContent`/`isTestHost` | `get-server-info.ts:70-72`, `server-context` endpoint `:60` | Read the flags before trusting preview |

**Consequence for the LS site:** our shipped FAQ answers carry raw `<p>…</p>` and our Services list has no positions **only because the author (me) didn't set `transformType`/`wrapInListItem`** — not because of any engine limitation. See the LS follow-up note at the end.

---

## 2. The principle this spec optimizes for

> **Litmus test:** *Could a competent agent produce the optimal mapping using ONLY the MCP tool schemas + responses — never reading the C#?*

Today the answer is **no**, and this session is the proof: every signal existed (`get-block-element-types` told me `answer` is `Umbraco.RichText`; `numberedServiceItem` has an integer `number`), but nothing connected signal → action, so the output was sub-optimal and 12 configs were hand-written. Every item below must move that answer toward **yes**. **Philosophy: inform + suggest, never auto-apply** — the tool teaches and pre-fills, the author always confirms; output never changes silently.

---

## 3. Generalize `warn-on-drop` across the three author-facing moments `[MCP]` `[ENGINE]`

`warn-on-drop` is the one pattern that already does this right (contextual, actionable, at save time). An agent only ever sees three surfaces — make all three teach.

### 3a. Input schema (before authoring)
The nested-route options (`transformType`, `wrapInListItem`, `positionProperty`, `requiredProperties`) live inside a stringified `resolverConfig` blob whose worked example omits them, and `transformType` is only documented on the *top-level* property-mapping schema. So an agent pattern-matches the example and omits them (I did).
**Change.** Promote the nested-route shape to typed, described fields in the MCP input schema (preferred), or at minimum **enumerate every option inside the nested example** in `save-schema-mapping.ts`'s description, each with a one-line "when to use".
**Acceptance.** An agent reading only `save-schema-mapping`'s schema sees `transformType`/`wrapInListItem`/`positionProperty`/`requiredProperties` as first-class, documented options on nested mappings.

### 3b. Suggester (a correct starting point)
`suggest-property-mappings` (`post/suggest-property-mappings.ts`) matches by name only.
**Change.** Pre-fill obvious options from editor metadata: RichText source → text-range target ⇒ `transformType:"stripHtml"`; an ordered/numbered block (integer `number`/`order`/`position` property) → `itemListElement` ⇒ `wrapInListItem:true` + `positionProperty`. Mark them `isAutoMapped:true` so the author sees and can revert them.
**Acceptance.** Suggesting a `faqItem`→`Question` mapping returns `acceptedAnswer` pre-set with `transformType:"stripHtml"`; suggesting a numbered block returns `wrapInListItem:true`.

### 3c. Response (after a sub-optimal choice) — highest leverage
Extend the existing `warnings[]` with `severity:"info"`/`"suggestion"` items the engine derives from editor aliases + ranges + persistence state. Four concrete checks, each mapping to a real mistake this session:
- RichText source → text target **without** a transform → *"emits raw HTML; add `transformType:'stripHtml'`."*
- nested blocks → `itemListElement` **without** `wrapInListItem` → *"no positions; set `wrapInListItem:true`."*
- save returned `persistedTo:"database"` (and a `uSync/` folder exists) → *"won't reproduce to other environments; enable export-on-save or run export."*
- nested `Question` mapped `name` only → *"Google FAQ expects `acceptedAnswer`."*

Reuse the validator that already powers `warn-on-drop` (`SchemeWeaverService.cs` validation path, `:265-275`).
**Acceptance.** Saving the LS FAQ mapping *without* `transformType` returns an `info` suggestion naming `stripHtml`; saving the services mapping without `wrapInListItem` suggests it; a DB-only save in a uSync repo suggests export. None of them change the saved mapping.

---

## 4. Genuine remaining engine gaps `[ENGINE]`

- **Transforms not applied to `static`, `complexType`, or `blockContent` stringList sources.** Only `property` (top-level) and routed nested mappings honor `transformType`. `static` is intentional (`JsonLdGenerator.cs:386`); `complexType` (`ComplexTypeMappingEntry` has no `TransformType`) and stringList extraction (`BlockContentResolver.cs:53-57`) are real gaps — a RichText value pulled via `extractAs:stringList` can't be stripped. **Change:** add `transformType` to `ComplexTypeMappingEntry` and the stringList path. **Acceptance:** a stringList/complexType RichText source with `stripHtml` emits plain text.
- **Required-property enforcement is present but under-surfaced.** `requiredProperties` works (`Resolve_RoutedConfig_RequiredPropertyMissing_DropsThing`) but isn't in the MCP schema/example and there's no warning when a known rich-result type (FAQPage→Question→`acceptedAnswer`) lacks it. **Change:** document it (3a) + warn on it (3c).

---

## 5. MCP polish `[MCP]`

- **Remove the two remaining `.extend()` shims** in `get/get-schema-mapping.ts:30-45` and `get/get-all-schema-mappings.ts:26-31` — the generated `schemeWeaverApi.zod.ts` already carries `reachability`/`warnings`/`driftStatus` (`:151-159`). (v1.1.1 removed only the `save` shim.) **Acceptance:** no `.extend()` over generated response schemas remains; build + tests green.
- **`get-rendered-json-ld` can only hit the one configured host** (`get/get-rendered-json-ld.ts:72` → `base-url.ts:10`). Allow an optional explicit host/route so ground-truth can be checked against the *real* site, not just the sandbox. **Acceptance:** the tool can return the public render for a route without re-pointing `UMBRACO_BASE_URL`.
- **Make `persistedTo`/`driftStatus`/`hasPublishedContent`/`isTestHost` speak.** They're returned but silent. Fold them into the response guidance (3c) and the tool descriptions so an agent acts on them.

---

## 6. Defaults — conservative, per "inform + suggest, never auto-apply"

Do **not** silently change output. Instead:
- When a save is DB-only **and** a `uSync/` folder is present, the response loudly *suggests* enabling `ExportMappingsToUSyncOnSave` (3c) — rather than flipping it. (Optionally ship a documented recommended-default in templated installs.)
- Default `preview-json-ld` to a real node when `get-server-info.hasPublishedContent` is true and the caller didn't pass `contentKey`, so the riskiest mappings get real values by default. (Preview is read-only — no output is mutated.)

---

## 7. Optional: a `doctor` / `validate-mapping` surface `[MCP]`

One call that aggregates every advisory into a checklist for a content type's mapping: range drops, missing transforms, missing positions, DB/disk drift, missing required props, unreachable (`composed-from-block`) mappings. The proactive form of §3c.
**Acceptance.** `validate-mapping(contentTypeAlias)` returns a single ranked list an author can clear top-to-bottom; an all-clear means the mapping passes the §2 litmus test.

---

## Suggested order
§3c (response advisories — biggest leverage, reuses warn-on-drop) → §3a/§3b (schema + suggester) → §5 (MCP polish, mostly trivial) → §4 (engine gaps) → §7 (doctor) → §6 (defaults). Then **ship `d913368` as 17.7.0/18.2.0**.

---

## LS Lettings follow-up (NOT done this run — needs `d913368` released first)
Once the spec-v2 engine ships as **17.7.0**, bump `apps/umbraco/Directory.Packages.props` and retrofit the 12 shipped page configs:
- FAQ answers: add `"transformType":"stripHtml"` to the nested `faqItem.answer`→`acceptedAnswer` mapping → plain-text answers (fixes the raw `<p>` we shipped).
- Services: add `"wrapInListItem":true,"positionProperty":"number"` to the `numberedServiceItem` route → a properly ordered `ItemList` of `ListItem{position,item:Service}` (restores the ordering the old hack had).
Then re-validate live `by-route` (`/tenants` answers tag-free, `/landlords` positioned) and redeploy.
