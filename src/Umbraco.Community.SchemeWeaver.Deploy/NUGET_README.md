# SchemeWeaver Deploy addon

Umbraco Deploy / Umbraco Cloud support for [SchemeWeaver](https://www.nuget.org/packages/Umbraco.Community.SchemeWeaver) — deploy your Schema.org mappings between environments alongside the document types they belong to.

Every schema mapping is serialised to a `.uda` artifact in `umbraco/Deploy/Revision` when you save it, travels through source control with your schema, and is upserted on the target environment when a deployment is triggered. Each artifact declares a dependency on its document type, so Deploy always processes them in the right order. No more recreating mappings per environment.

This is the Deploy equivalent of the [uSync addon](https://www.nuget.org/packages/Umbraco.Community.SchemeWeaver.uSync) — pick the one that matches your deployment tooling.

## Requirements

- **Umbraco Deploy OnPrem or Umbraco Cloud** on the host site (a paid, separately installed Umbraco product — this addon does not include it). Without it, the addon logs a single startup warning and stays inactive; it never breaks your site.
- `Umbraco.Community.SchemeWeaver` at the matching major-aligned version (installed automatically as a dependency).
- Versioning follows the CMS major: install `17.x` on Umbraco 17, `18.x` on Umbraco 18 — NuGet resolves the right build automatically.

## Getting started

```bash
dotnet add package Umbraco.Community.SchemeWeaver.Deploy
```

That's it — the addon self-registers. Save a mapping (Settings → your document type → **Schema.org** tab) and a `schemeweaver-mapping__{contentTypeKey}.uda` file appears in `umbraco/Deploy/Revision`. Commit it, deploy as usual (marker file, CI/CD trigger, or the Deploy dashboard), and the mapping arrives on the target.

## Behaviour worth knowing

- **Deletions do not propagate.** Deleting a mapping removes its `.uda` at source, but the target keeps its database row until you delete it there too — the same never-delete-schema policy Deploy applies to document types.
- **Source overwrites target** on every schema deployment, including the enabled/disabled flag. Make mapping changes at the source of your deployment flow.
- Deleting a document type automatically cleans up its mapping artifact, so stale artifacts can never fail later deployments.
- Resolver and dynamic-root configuration travel verbatim; content referenced inside them moves with Deploy's normal content workflows.

## Documentation

Full documentation — behaviour, field reference, troubleshooting and a pre-release test checklist — lives in the [Umbraco Deploy Integration docs](https://github.com/EnjoyDigital/Umbraco.Community.Schemeweaver/blob/main/docs/deploy.md).
