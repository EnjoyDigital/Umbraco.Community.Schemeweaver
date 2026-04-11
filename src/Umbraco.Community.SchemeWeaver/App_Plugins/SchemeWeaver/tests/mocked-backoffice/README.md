# Mocked Backoffice tests

Playwright drives the real Umbraco backoffice UI in Chromium, with **all**
HTTP traffic served by MSW from SchemeWeaver's in-memory mock DB
(`src/mocks/handlers.ts` + `src/mocks/data/schemeweaver.db.ts`). No .NET
backend is required.

This tier sits between the component tests (`npm test` / `npm run test:msw`)
and the full E2E suite (`npm run test:e2e`). It's the only layer that
exercises the real Umbraco extension registry, workspace-view condition
matching, and modal-manager plumbing, while staying fast enough to run
without booting a SQLite database or a .NET host.

## Prerequisites

| What | Why |
|---|---|
| A local clone of [`umbraco/Umbraco-CMS`](https://github.com/umbraco/Umbraco-CMS) (pinned to **v17.2.2**) | Hosts the real backoffice Vite dev server that loads SchemeWeaver via `VITE_EXAMPLE_PATH` |
| `npm install` run once inside `Umbraco-CMS/src/Umbraco.Web.UI.Client` | Dev server + MSW dependencies |
| `UMBRACO_CLIENT_PATH` env var pointing at `.../Umbraco-CMS/src/Umbraco.Web.UI.Client` | Consumed by `fixtures/env.ts` and `playwright.config.ts` |
| The local `addMockHandlers` patch applied (see below) | The Umbraco-CMS v17.2.2 MSW setup does not yet expose a runtime handler-registration API — the patch adds one |

## How the extension is loaded

Vite's `server.fs.allow` is scoped to `Umbraco.Web.UI.Client`, so Vite refuses
to serve files from SchemeWeaver's sibling repo directly (403 Forbidden). The
harness works around this by **mirroring `App_Plugins/SchemeWeaver/src` into
`Umbraco.Web.UI.Client/examples/schemeweaver-src`** on every test run, then
pointing `VITE_EXAMPLE_PATH` at that in-tree path. The mirror is handled
automatically by `fixtures/env.ts` — you don't need to run a pre-copy step
manually.

Windows junctions were the first thing we tried and are a dead end: Vite's
esbuild transform silently resolves junctions to their real out-of-tree path
and skips the TypeScript transform, so the browser ends up executing raw
`.ts` source. Plain-file copies avoid that.

The mirror directory is **dev-only** — it lives under the Umbraco-CMS
checkout, is refreshed on every run, and is not checked in anywhere. If you
pull a new Umbraco-CMS version, delete the mirror by hand or let the next run
recreate it.

## Applying the patch

The patch adds a single `??=` assignment to `Umbraco.Web.UI.Client/src/mocks/index.ts`
that exposes `window.MockServiceWorker.addMockHandlers`. It's idempotent
and upstream-safe — once Umbraco ships its own `addMockHandlers` the
`??=` makes the local override a silent no-op.

From the SchemeWeaver repo root:

```bash
git -C "$UMBRACO_CLIENT_PATH/../.." apply \
  src/Umbraco.Community.SchemeWeaver/App_Plugins/SchemeWeaver/tests/mocked-backoffice/patches/umbraco-cms-v17.2.2-addmockhandlers.patch
```

To unapply (e.g. before `git pull` in the Umbraco-CMS clone):

```bash
git -C "$UMBRACO_CLIENT_PATH/../.." apply --reverse \
  src/Umbraco.Community.SchemeWeaver/App_Plugins/SchemeWeaver/tests/mocked-backoffice/patches/umbraco-cms-v17.2.2-addmockhandlers.patch
```

`fixtures/env.ts` greps the patched file on startup and fails with the
exact `git apply` command if the hook is missing, so you'll know
immediately if you forget.

## Running

```bash
export UMBRACO_CLIENT_PATH=/c/projects/Umbraco-CMS/src/Umbraco.Web.UI.Client
cd src/Umbraco.Community.SchemeWeaver/App_Plugins/SchemeWeaver
npm run test:mocked-backoffice
```

First cold run: ~1–2 min (Vite compiles the Umbraco backoffice).
Warm reruns (with `reuseExistingServer`): ~20–40 s.

## What the specs cover

| Spec | Covers |
|---|---|
| `bootstrap.spec.ts` | MSW is active; the patch is applied; SchemeWeaver manifests are in the extension registry |
| `msw-handlers.spec.ts` | `/mappings`, `/content-types`, `/schema-types?search=...` are intercepted by SchemeWeaver's handlers and return mock DB data |
| `workspace-view.spec.ts` | The Schema.org workspace-view tab mounts on a doctype workspace URL via the real Umbraco shell |

## Follow-up specs to add

These are higher-value but depend on the Umbraco-CMS mock seed data and
the real entity-action / context-menu plumbing. Ship them once the
three above are green in CI:

- Entity action "Map to Schema.org" appears on the doctype context menu and opens the schema-picker modal.
- Schema picker modal: open programmatically, search, pick, assert the parent receives the picked type.
- Property mapping modal: auto-map, edit a row, save, reopen, assert persistence against the mock DB.
- JSON-LD preview tab mounts on a document workspace.

## Non-goals

- **Login-gated flows.** `VITE_UMBRACO_USE_MSW=on` sets `bypassAuth = true` in the Umbraco bootstrap. The tier cannot exercise the auth challenge or user-permission checks.
- **Rendering-only assertions.** Leave those in the component tests (`npm test`) — they're faster and don't need a browser.
- **Full CRUD persistence across restarts.** The mock DB is module-scoped inside the Vite dev server and resets on restart. Persistence across processes is E2E territory (`npm run test:e2e`).

## CI

Not wired up. The tier depends on a patched local Umbraco-CMS clone,
which is too much setup for every PR. Revisit once upstream ships the
real `addMockHandlers` API and the `??=` override becomes a dead no-op
— at that point the only remaining CI prerequisite is cloning
Umbraco-CMS, which fits cleanly into a GitHub Actions matrix job.
