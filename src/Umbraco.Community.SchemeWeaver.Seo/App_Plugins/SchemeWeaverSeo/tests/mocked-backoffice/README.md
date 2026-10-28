# Mocked Backoffice tests (SEO bundle)

The SEO counterpart of the core harness in
`src/Umbraco.Community.SchemeWeaver/App_Plugins/SchemeWeaver/tests/mocked-backoffice/`: the
real Umbraco backoffice runs in Chromium from the Umbraco-CMS Vite dev server with
`VITE_UMBRACO_USE_MSW=on`, and every HTTP call is answered by MSW from the two bundles'
in-memory mock DBs. No .NET host is needed.

Prerequisites are identical to the core harness (a local Umbraco-CMS clone pinned to
**v17.2.2** with `npm install` run in `src/Umbraco.Web.UI.Client`, the `addMockHandlers`
patch applied, and `UMBRACO_CLIENT_PATH` pointing at that client folder); read the core
README first.

## How the two bundles load together

The SEO bundle imports a few core modules through `src/core-bridge/` by relative path. Vite
only serves files inside the Umbraco client checkout, so `fixtures/global-setup.ts` builds a
composite mirror under `examples/schemeweaver-seo/`: a copy of the core `src/` as `core/`, a
copy of this plugin's `src/` as `seo/` with the bridge imports rewritten to `../../core/`, and
an `index.ts` that exports both manifest lists. `VITE_EXAMPLE_PATH` points at that folder, so
the core's workspace views and MSW handlers register alongside the SEO section.

The SEO handlers also override `GET /umbraco/management/api/v1/user/current` to add the
`SchemeWeaver.Seo.Section` alias to the mock user's allowed sections, otherwise the section
and tab conditions would hide everything.

## Running

```bash
export UMBRACO_CLIENT_PATH=/path/to/Umbraco-CMS/src/Umbraco.Web.UI.Client
cd src/Umbraco.Community.SchemeWeaver.Seo/App_Plugins/SchemeWeaverSeo
npm run test:mocked-backoffice
```

The dev server listens on port 5175 (the core harness uses 5174) so both can stay up.

## Specs

- `section.spec.ts`: the section and its four views render; the Content types table shows statuses.
- `wizard.spec.ts`: the wizard sets a page type up and the mock DB holds the saved slots.
- `page-schema-tab.spec.ts`: the Schema tab mounts on a document and the core JSON-LD tab is excluded.
