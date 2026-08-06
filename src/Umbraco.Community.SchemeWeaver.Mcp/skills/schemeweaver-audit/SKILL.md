---
name: schemeweaver-audit
description: Site-wide structured-data audit for an Umbraco site running SchemeWeaver, ending in a prioritised fix plan that gets executed. Use when the user asks to audit, review or health-check their site's structured data, schema.org or JSON-LD coverage, asks why pages are not getting Google rich results, wants to know which page types are missing schema markup, wants existing SchemeWeaver mappings validated or checked for uSync drift, or asks "how good is my SEO markup". Drives inventory sweep (all mappings + all content types) -> triage unmapped types by rich-result value -> validate every mapping -> live output spot checks -> drift check -> fixed-format audit report -> execute fixes one type at a time via the schemeweaver-map skill.
---

# SchemeWeaver — site-wide structured-data audit

Where `schemeweaver-map` perfects ONE content type, this skill surveys the WHOLE
site: what is mapped, what is missing, what is broken, and what would win the
most Google rich results next. It produces two things, in order: a fixed-format
**audit report**, then **executed fixes** agreed with the user.

Requires a working `schemeweaver` MCP connection — if the tools are missing or
failing auth, run the `schemeweaver-setup` skill first.

## Phase 1 — inventory sweep

1. **`get-all-schema-mappings`** — for every mapping record: content type alias,
   schema type, `isEnabled`, `isInherited`, `reachability`, `driftStatus`, and
   `warnings`. Treat every warning as serious: a warned property is mapped
   outside its Schema.org range and is **silently dropped at render** — the
   editor filled the content in, and nothing is emitted. Invisible failures,
   never cosmetic ones.
2. **`list-content-types`** — diff against the mappings to find UNMAPPED types.
3. Classify before judging. These are NOT gaps:
   - a child type whose ancestor mapping has `isInherited: true` (already covered);
   - element types on their own — they only emit inside a mapped page's
     `blockContent` rows;
   - settings/folder/utility types that never render on a URL.

## Phase 2 — triage unmapped types

Prioritise by what Google actually rewards, not by count.

**Priority 1 — routed page types matching rich-result-eligible schema types.**
Read the content-type names/aliases for these smells:

| Content type smell | Candidate schema type |
|---|---|
| `blog`, `news`, `article`, `post` | `BlogPosting` / `NewsArticle` / `Article` |
| `product`, `item`, `sku` | `Product` |
| `recipe` | `Recipe` |
| `event`, `webinar`, `gig` | `Event` |
| `faq`, `questions` | `FAQPage` |
| `job`, `vacancy`, `career` | `JobPosting` |
| `howTo`, `guide`, `tutorial` | `HowTo` |
| `course`, `training` | `Course` |
| `location`, `store`, `branch`, `contact` | `LocalBusiness` (or a subtype) |
| `video` | `VideoObject` |
| `review`, `testimonial` | `Review` / `AggregateRating` |

Also check **site identity**: the home/root type should carry `Organization`
(or `LocalBusiness`) and `WebSite` — publishers and authors elsewhere reference
them.

**Priority 2 — remaining routed pages.** Generic `WebPage` / `AboutPage` /
`ContactPage` mappings are worthwhile hygiene but carry no rich-result payoff;
never let them displace Priority 1 work.

With a long gap list, PRESENT THE PRIORITIES AND AGREE SCOPE with the user
before executing anything — an audit that silently starts mapping twenty types
has overstepped.

## Phase 3 — quality pass on existing mappings

For every existing mapping run **`validate-mapping`** and record `allClear` plus
the top items in severity order (`critical` > `warning` > `suggestion` > `info`).

- **Range warnings** (from Phase 1) rank highest: silently-dropped output on a
  mapped, published page beats every unmapped type in urgency. The fix
  (re-homing the property) belongs to `schemeweaver-map` — record it, don't fix
  it mid-audit.
- **`reachability: composed-from-block`** is information, not an error — but
  verify that some routed page's block mapping actually reaches the type. If
  nothing routes to it, it never emits anywhere: report it as critical (an
  orphaned mapping someone believes is live).
- **`isEnabled: false`** — surface and ask. Deliberate kill-switch or forgotten
  toggle? Never silently re-enable.

## Phase 4 — live truth

Pick 1–2 real published URLs per high-value mapped type and run
**`get-rendered-json-ld`** against them. This is the AUTHORITATIVE check — it
reads what the site actually serves (the same output Google fetches). Use the
`host` parameter to test the public domain when it differs from the configured
base URL.

**NEVER BASE THE AUDIT VERDICT ON `preview-json-ld` ALONE.** Preview renders in
backoffice context; its `isValid` does not prove the live page emits anything.
Preview is for iterating on a mapping, `get-rendered-json-ld` is for judging it.

Interpret infrastructure results correctly rather than blaming mappings:

- Delivery API 404/401 → it is off by default or API-key protected. Report as an
  **infrastructure finding** ("live output could not be verified"), not a
  mapping bug.
- HTTP 200 with ZERO JSON-LD blocks → the page renders but emits nothing (the
  tool surfaces this case explicitly). Report it; never call it a pass.

## Phase 5 — drift (uSync)

Run **`get-usync-drift`**. If uSync is unavailable, note "uSync addon not
installed" in the report and move on — it is optional.

- `in-sync` — nothing to do.
- `db-only` — saved in the database but never exported: not in source control,
  will not survive a rebuild or reach other environments.
- `content-differs` — database and disk disagree: the next deployment or import
  will surprise someone. Find out which side is right before exporting.
- `disk-only` — a committed `.config` with no matching database mapping: the
  import has likely not run in this environment.

If the site uses uSync, plan an **`export-mappings-to-usync`** AFTER fixes land
(never mid-audit), then re-check that drift reports `in-sync`.

## Phase 6 — the report

ALWAYS use this exact structure — audits get re-run and compared, and a fixed
shape keeps successive reports diffable:

```markdown
# Structured-data audit — <site> — <date>

## Coverage
| Content type | Routed? | Mapped to | Enabled | Reachability | Validation | Drift |
|---|---|---|---|---|---|---|
(one row per relevant content type; unmapped rows use "—" in Mapped to, with
the candidate type in brackets)

**Coverage: X of Y routed page types mapped (Z%).**

## Issues
### Critical
(broken/absent live output; silently-dropped range warnings; orphaned
composed-from-block mappings)
### Warnings
(validate-mapping warnings; disabled mappings; drift: content-differs / db-only)
### Suggestions
(missing recommended properties; a generic type where a more specific one fits)

## Action plan
1. <action> — <why: expected rich-result payoff, or risk removed>
```

Order the action plan by: live-output breakage → silently-dropped warnings →
high-value unmapped types → drift/exports → nice-to-haves.

## Phase 7 — execute

1. Present the report and AGREE THE SCOPE with the user.
2. For each agreed item, drive the **`schemeweaver-map`** skill
   (`/schemeweaver-mcp:schemeweaver-map`) for that ONE content type — do not
   restate its loop here. One type at a time, each to `allClear`, before
   starting the next.
3. Afterwards: re-run the `get-rendered-json-ld` spot checks on the affected
   pages; if the site uses uSync, `export-mappings-to-usync` and confirm
   `get-usync-drift` reports `in-sync`.

## Stopping condition

The audit is done when the report has been delivered AND every agreed action is
either completed (mapping at `allClear` + live spot check passing) or explicitly
deferred by the user, and drift is `in-sync` (or uSync unavailable / export
deferred). Never claim rich-results eligibility from `preview-json-ld` alone —
the live render is the only evidence that counts.
