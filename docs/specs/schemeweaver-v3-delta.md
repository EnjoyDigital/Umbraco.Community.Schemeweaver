# SchemeWeaver v3 — delta addendum

Companion to `schemeweaver-improvement-spec-v3.md`. That spec was copied from the
LS-repo draft *before* the LS `stripHtml` retrofit was actually performed against
the released **17.7.0**. This file captures what that retrofit empirically found —
**one new work item, two corrections, and some now-stale framing.** Nothing here
contradicts the main spec's mechanism (§3) or other gaps (§4/§5); it adds/sharpens.

---

## 1. NEW work item — `wrapInListItem`/`positionProperty` don't propagate to nested routes `[ENGINE]`

**Problem.** `WrapInListItem` and `PositionProperty` are `ResolverConfigModel`-level
only (`Services/Resolvers/BlockContentResolver.cs:644,650`). When resolution recurses
into a nested block-list property, `ResolveNestedBlockProperty` builds the child config
as `new ResolverConfigModel { Routes = mapping.Routes }` (`:494-495`) — it copies **only
`Routes`**, and `NestedPropertyMapping` has no `WrapInListItem`/`PositionProperty`
fields. So `ListItem`/`position` wrapping works **only for a top-level block list**, not
for an `itemListElement` populated by a *nested* list.

**Evidence.** Retrofitting LS Lettings: the Services list is `numberedServiceItem`
nested inside an `ItemList` nested inside the page `content` grid. Setting
`wrapInListItem:true` on that nested route had no effect; the list stays bare
`Service[]`. (The passing tests `Resolve_RoutedConfig_WrapInListItem_EmitsSequentialListItems`
and `Resolve_WrapInListItem_WithPositionProperty_UsesExplicitPositions` only exercise a
**top-level** list — the nested case is uncovered.)

**Change.** Add `WrapInListItem` (bool) + `PositionProperty` (string?) to
`NestedPropertyMapping`, and in `ResolveNestedBlockProperty` propagate them onto the
child `ResolverConfigModel` alongside `Routes`.

**Acceptance.** A nested `itemListElement` route with `wrapInListItem:true` (and optional
`positionProperty`) emits `ItemList{ itemListElement:[ListItem{position,item}] }`. Add a
test mirroring the top-level ones but one level deeper (e.g. `…_Nested_…`).

---

## 2. Corrections to claims in `schemeweaver-improvement-spec-v3.md`

- **§1 Erratum table, "`ListItem`/`position` for ordered lists" row** — reads as
  unconditionally "Exists … set `wrapInListItem:true`". Qualify: **works for top-level
  block lists only**; nested lists need item #1 above first.
- **§ "LS Lettings follow-up", the Services bullet** ("add `wrapInListItem:true,
  positionProperty:number` to the `numberedServiceItem` route") — **blocked on #1**; it
  won't take effect at that nesting depth until propagation lands.

---

## 3. Now-stale framing (the world moved on)

- The spec says v2 is "unreleased on `feature/spec-v2-improvements` (d913368)". It's
  **released**: `main @ 63d870b`, tags `v17.7.0` / `v18.2.0`, MCP **1.2.0** on nuget/the
  plugin. Update the Origin/empirical-verification notes accordingly.
- **§5 "remove the two remaining `.extend()` shims"** — already done; folded into
  `63d870b` (the deferred P3.1 work). Mark as shipped / no-op.
- **§ LS follow-up, FAQ bullet** — **done and live.** LS bumped to 17.7.0 and added
  `transformType:"stripHtml"` to the `faqItem.answer`→`acceptedAnswer.text` mapping across
  all 12 page configs (LS PR #130, merged + deployed; verified live —
  `test.lslettings.co.uk/tenants` 10 FAQ answers all tag-free). Only the **Services**
  half of that follow-up remains, and it's the thing blocked on #1.

---

*Source of these findings: empirical retrofit + a `dotnet test` run of the 17.7.0 engine
(59 transform/resolver tests green) from a downstream session. No SchemeWeaver source was
modified by that session — only this `docs/specs/` note + the LS repo consume it.*
