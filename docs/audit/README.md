# Rich Results Audit

This folder holds dated Rich Results audit reports produced by the validator pipeline in `src/Umbraco.Community.SchemeWeaver/Services/Validation/`.

## Running the audit

The audit is a scripted sweep that:
1. Enumerates every published content node on the target Umbraco site.
2. Generates its JSON-LD blocks via `IJsonLdBlocksProvider` (same path the Delivery API / tag helper / Examine index use).
3. Runs every node through `ISchemaValidator` — fifteen tier-1 Google Rich Results rule-sets plus a generic `@id` / `name` sanity check.
4. Writes a Markdown table summarising Critical / Warning counts per page + detailed findings for anything that failed.

### Current status

The automated harness at `tests/Umbraco.Community.SchemeWeaver.Tests/Integration/RichResultsAudit.cs` is scaffolded but **skipped by default**. It's based on `WebApplicationFactory<Program>` + uSync first-boot import, and the in-process boot isn't yet reliably populating the content cache by test-run time. Fix TBD — see the `Skip` reason on the test for the known limitation.

### Running manually against a live TestHost

Until the automation is fixed, audits are run against a hand-started TestHost:

1. Start the TestHost: `dotnet run --project src/Umbraco.Community.SchemeWeaver.TestHost`.
2. Wait for Umbraco to boot and uSync to finish its first-boot import (watch the logs for "uSync: Import complete").
3. Hit the backoffice JSON-LD preview tab on a content item, or inspect a rendered page's `<script type="application/ld+json">` output.
4. Paste the JSON-LD into the validator manually (or extend the harness to hit the running host via `HttpClient` pointed at its port).

The validator itself runs standalone — given a JSON-LD string, it tells you what's missing. The "run it against every page" step is the automation gap.

### Tier-1 rule coverage

Fifteen Schema.org type families get specific Google Rich Results rules (required → Critical, recommended → Warning):

- `Article` / `BlogPosting` / `NewsArticle` / `TechArticle`
- `Product` / `IndividualProduct` / `ProductModel`
- `Event` (+ all typed subevents)
- `Recipe`
- `HowTo`
- `Course` / `CourseInstance`
- `Movie`
- `Book`
- `VideoObject`
- `Organization` (+ non-LocalBusiness subtypes)
- `LocalBusiness` (+ ~50 vertical subtypes: Restaurant, Hotel, Store, Physician, etc.)
- `WebSite`
- `JobPosting`
- `FAQPage` (with per-Question validation)
- `BreadcrumbList` (with per-ListItem validation)

Every other Schema.org type falls back to the generic rule: `@id` must be a well-formed absolute URL.

Tier-2 coverage (adding e.g. Review/AggregateRating, Dataset, QAPage, SoftwareApplication rules) is a separate phase — add when the audit shows demand.

## Report format

Each run writes a file named `rich-results-audit-<yyyy-MM-dd>.md` containing:

- **Summary counts**: total pages, with-mapping, clean, warning-only, critical.
- **Per-page table**: route, content type, schema types, critical + warning counts, short "missing: x, y" note.
- **Detailed findings**: every issue on every failing page, with path, severity, schema type and a short Google-docs-linked explanation.

Commit the report to this folder after each run so we can diff audits across time and correlate fixes.
