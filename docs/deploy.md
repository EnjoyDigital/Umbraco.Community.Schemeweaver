# Umbraco Deploy Integration

The optional **`Umbraco.Community.SchemeWeaver.Deploy`** addon makes schema mappings first-class citizens of [Umbraco Deploy](https://umbraco.com/products/add-ons/deploy/): every mapping is serialised to a `.uda` artifact in `umbraco/Deploy/Revision`, travels through source control with the document types it belongs to, and is upserted on the target environment when a deployment is triggered. It is the Deploy/Cloud equivalent of the [uSync addon](usync.md): pick the one that matches your deployment tooling.

## Requirements

| Requirement | Notes |
|---|---|
| `Umbraco.Community.SchemeWeaver` | Same major-aligned version (installed automatically as a dependency) |
| **Umbraco Deploy OnPrem or Cloud** | A **paid, separately installed** product (included with Umbraco Cloud). The addon references only the freely available `Umbraco.Deploy.Infrastructure` contracts; if Deploy itself is not installed, the addon logs one warning at startup and stays inactive, and it never breaks the site. |
| A Deploy licence | Required by Deploy itself for deployment operations. Any valid licence also covers `localhost`, `*.local` and `*.test`. |

## Installation

```bash
dotnet add package Umbraco.Community.SchemeWeaver.Deploy
```

That's it: the addon self-registers. On the next save of a mapping (Settings → your document type → **Schema.org** tab) you'll find a `schemeweaver-mapping__{contentTypeKey}.uda` file in `umbraco/Deploy/Revision`, alongside Deploy's own document-type artifacts.

## What gets deployed

The artifact carries exactly the same field set as the uSync serialiser:

- **Mapping**: content type alias + key, Schema.org type, enabled/inherited flags, [`@id` override](json-ld-output.md#id-precedence)
- **Property mappings** (in row order; order is meaningful): schema property, source type, property alias, source content type alias, transform, static value, nested schema type, resolver config, dynamic root config, target piece key

Environment-local values (`Id`, timestamps) are excluded so artifacts are deterministic: identical mappings on two environments produce identical checksums and compare as "up to date".

Each artifact declares a dependency on its **document type**, so Deploy always processes the doc type before the mapping that decorates it.

## How it works

- **Save/delete → disk**: saving a mapping in the backoffice writes/refreshes its `.uda` (and Deploy signature); deleting a mapping removes them. This is always on: writing artifacts on save is the core contract of a Deploy connector (Deploy's own entity refreshers behave the same way).
- **Commit → deploy**: commit the revision folder to source control as usual. On the target, trigger extraction with any of Deploy's mechanisms: the `deploy` marker file, the CI/CD trigger endpoint, or **Settings → Deploy** dashboard operations. The addon's connector upserts the mapping rows (matching by content type key, falling back to alias if the doc type was recreated with a new GUID).
- **Deleted document types**: deleting a doc type removes its mapping's `.uda` automatically. Without this, a stale mapping artifact with an unsatisfiable doc-type dependency would fail every subsequent schema deployment on the target.

### Deletions do not propagate

Deploy deliberately never deletes schema on a target because an artifact is missing ("it could lead to unrecoverable loss of data", Deploy's own policy, applied to doc types too). Deleting a mapping at source removes the `.uda` from the revision, but the target's database row stays until you delete the mapping there too. This intentionally differs from the uSync addon's `BootImportMode.Upsert` disk-wins model.

### Source overwrites target

On every schema deployment the source's mappings win, exactly like a uSync import. Note in particular that **`IsEnabled` travels in the artifact**: a mapping disabled directly on production will be re-enabled by the next deployment from an environment where it is enabled. Make mapping changes at the source of your deployment flow.

### Config blobs and content references

`ResolverConfig` and `DynamicRootConfig` are carried verbatim as opaque JSON. Content GUIDs inside a dynamic-root query are **not** declared as Deploy dependencies: content keys are stable across Deploy-managed environments, and a reference to content that doesn't exist yet simply resolves to nothing at render time (SchemeWeaver's standard never-break-the-page behaviour). Transfer the referenced content as usual with Deploy's content workflows.

## Local testing (TestHost)

The repo's TestHost keeps Deploy out of the default dev loop. Opt in per-build:

```bash
dotnet run -p:SchemeWeaverIncludeDeploy=true --project src/Umbraco.Community.SchemeWeaver.TestHost
```

Run `dotnet clean` when toggling the flag: MSBuild can otherwise leave the other variant's DLLs in `bin`. Unlicensed, OnPrem boots with a warning: save-time `.uda` writes and the Deploy dashboard work; extraction is disabled until a licence (or Umbraco Cloud) is present.

The full write→extract loop is covered licence-free in CI: `tests/Umbraco.Community.SchemeWeaver.Deploy.Tests` boots the TestHost with OnPrem and swaps in Deploy's own `NullLicensing` test hook (a public type Umbraco documents as being for testing) to drive the real disk pipeline end to end. This is a test-only device, not a way to run Deploy unlicensed.

## Pre-release checklist (real licensed E2E)

Run once before a release that touches the Deploy addon (an [Umbraco Cloud trial](https://try.umbraco.com/) includes Deploy and two environments):

1. Create a Cloud project; install `Umbraco.Community.SchemeWeaver` + `Umbraco.Community.SchemeWeaver.Deploy` (both environments).
2. In the left environment: create a doc type, map it to a Schema.org type, save.
3. Confirm `schemeweaver-mapping__*.uda` appears in the environment's git repository.
4. Deploy left → right from the portal.
5. In the right environment: confirm the mapping exists in the backoffice and the page emits the expected JSON-LD.
6. Delete the mapping on the left, deploy again, and confirm the right's mapping row intentionally remains (deletion policy above).

## Troubleshooting

| Symptom | Cause / fix |
|---|---|
| Startup warning "Umbraco Deploy (OnPrem/Cloud) is not installed" | The addon is present but Deploy itself isn't; install `Umbraco.Deploy.OnPrem` (or run on Cloud). The addon is inactive until then. |
| No `.uda` written on save | Check the warning above; also confirm the mapping's content type still exists and has a real key (mappings whose content type vanished are skipped deliberately). |
| Schema deployment fails with a missing `document-type` dependency | A mapping `.uda` references a doc type deleted outside this addon's cleanup (e.g. removed while the addon wasn't installed). Delete the stale `schemeweaver-mapping__*.uda` from the revision folder and redeploy. |
| Mapping differs between environments after deploy | By design: source overwrites target (see above). |
| A deleted mapping shows as a permanent difference in the Deploy schema comparison dashboard | Expected: deletions don't propagate, and the dashboard offers no delete button for mapping artifacts. Delete the mapping in the target's backoffice to clear it. |

## Roadmap

- Backoffice **queue for transfer / partial restore** (`RegisterTransferEntityType`): a deliberate v1 omission; mappings are doc-type-keyed settings and travel with the schema.
- Participation in Deploy's zip **Import/Export** (`SupportsImportExport`).
- `IDeletableServiceConnector` support, so the Deploy schema comparison dashboard can offer a delete affordance for orphaned mapping artifacts.

## Using Deploy and uSync together

The addons write to different folders (`umbraco/Deploy/Revision` vs `uSync/v*/SchemeWeaverMappings`) and don't conflict, but pick **one** tool as the source of truth for mappings on any given site; running both means two copies of the same state in source control.
