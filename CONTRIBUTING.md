# Contributing

Contributions are very welcome — bug reports, fixes, docs, new property resolvers, extra auto-mapper synonyms, whole new features. Small PRs are fine.

## Getting set up

### C#

```bash
dotnet build
dotnet test
```

### Frontend

```bash
cd src/Umbraco.Community.SchemeWeaver/App_Plugins/SchemeWeaver
npm install
npm run build
npm test
npm run test:msw                 # component tests with MSW handlers
npm run test:mocked-backoffice   # Playwright drives the real backoffice UI with MSW — needs Umbraco-CMS clone
npm run test:e2e                 # Playwright against a running Umbraco + .env
npm run test:screenshots         # regenerate the docs screenshots (opt-in)
```

### Test host with 100+ sample content types

```bash
dotnet run --project src/Umbraco.Community.SchemeWeaver.TestHost
```

> The TestHost is purely for testing schema mappings and structured data generation. It is not intended as a base site or starter kit.

Read [`CLAUDE.md`](CLAUDE.md) for architecture, DI wiring, and naming conventions.

## Tests

Please add tests for behavioural changes, and a regression test for bug fixes. CI runs the full suite on every push.

| Layer | Framework | Location |
|---|---|---|
| C# Unit | xUnit + NSubstitute + FluentAssertions | `tests/Umbraco.Community.SchemeWeaver.Tests/Unit/` |
| C# Integration | xUnit + `WebApplicationFactory<Program>` against the SchemeWeaver TestHost, shared via an xUnit collection fixture so every test class reuses a single host (temp SQLite, one file per suite) | `tests/Umbraco.Community.SchemeWeaver.Tests/Integration/` |
| TS Unit / Component | `@open-wc/testing` + MSW | `App_Plugins/SchemeWeaver/src/**/*.test.ts` |
| Mocked Backoffice | Playwright drives the real Umbraco backoffice UI via `VITE_EXAMPLE_PATH`, with SchemeWeaver's MSW handlers serving all HTTP traffic — no .NET required. Requires a local `umbraco/Umbraco-CMS` clone plus a small `addMockHandlers` patch; see [`tests/mocked-backoffice/README.md`](src/Umbraco.Community.SchemeWeaver/App_Plugins/SchemeWeaver/tests/mocked-backoffice/README.md) | `App_Plugins/SchemeWeaver/tests/mocked-backoffice/` |
| E2E | Playwright + `@umbraco/playwright-testhelpers` against a running Umbraco | `App_Plugins/SchemeWeaver/tests/e2e/` |

For backoffice UI changes, `npm run test:mocked-backoffice` verifies manifest wiring, workspace-view conditions, and modal plumbing without a running Umbraco; `npm run test:e2e` against a real instance is still the only thing that catches issues across the full .NET + backoffice stack.

## Using an AI assistant

AI tools (Claude, Codex, Copilot, Cursor, etc.) are welcome to help. The rules are short:

- **You review it.** Read every line before committing. You're accountable for the PR, not the assistant.
- **MIT-compatible only.** Don't submit code copied from incompatible sources.
- **Add tests**, same as any other contribution.
- **For UI work**, install the [Umbraco Backoffice Skills](https://github.com/umbraco/umbraco-cms-backoffice-skills) and add `umbraco/Umbraco-CMS` (`src/Umbraco.Web.UI.Client`) and `umbraco/Umbraco.UI` (`packages/uui`) as working directories so the skills can grep the canonical backoffice source — see the skills repo for setup. Also run the `umbraco-extension-reviewer` agent on UI changes.
- **Tag the commit.** When an assistant materially helped, add a git trailer at the bottom of the commit message so we can see which model was used:

  ```
  Assisted-by: Claude:claude-opus-4-6
  ```

  Format is `Assisted-by: <agent>:<model> [optional tools]`, e.g. `Assisted-by: Copilot:gpt-5 playwright`. Just leave a blank line before it, or use `git commit --trailer "Assisted-by=..."`. Basic tools (git, dotnet, npm, editors) don't need listing. If your tool already adds a `Co-authored-by:` trailer for the assistant automatically, that's fine too — just don't bother adding both.
- **Don't add `Signed-off-by`** on a human's behalf. Only the human submitter can sign off their own contribution.
