---
name: schemeweaver-setup
description: Connect Claude Code to an Umbraco site running SchemeWeaver and prove the connection works end to end. Use when the user wants to install or set up the SchemeWeaver plugin or MCP server, wire Claude up to their Umbraco site, create the Umbraco API user or client credentials it needs, or whenever any schemeweaver tool misbehaves - 401/unauthorised errors, an HTML login page instead of JSON, connection refused, certificate errors, or the schemeweaver tools simply not appearing at all. Drives create API user -> install plugin -> verification ladder (get-server-info -> list-content-types) -> targeted diagnosis, then hands off to the schemeweaver-map skill for the first mapping.
---

# SchemeWeaver — connect and verify the MCP server

Get Claude Code talking to an Umbraco site running SchemeWeaver, and **prove it**
before doing anything else. The goal is a verified connection: `get-server-info`
answering AND `list-content-types` returning the site's real content types.

Two things must both be true: the plugin is installed in Claude Code, and an
**API user** exists in the Umbraco backoffice for it to authenticate as. This
skill is also the diagnosis path — if the tools already exist but are failing,
jump straight to the [verification ladder](#step-3--the-verification-ladder) and
the troubleshooting table.

## Before you start

- A running Umbraco 17 or 18 site with the `Umbraco.Community.SchemeWeaver`
  NuGet package installed ([getting started](https://github.com/EnjoyDigital/Umbraco.Community.Schemeweaver/blob/main/docs/getting-started.md)).
- Backoffice **admin access** to that site (needed to create the API user).
- Node.js 22+ on this machine — the plugin runs the bundled MCP server locally.

## Step 1 — create the Umbraco API user

Do this FIRST: the plugin installer prompts for the credentials, so have them
ready before installing.

1. In the Umbraco backoffice: **Users → Create → API user**. Name it something
   recognisable, e.g. `SchemeWeaver MCP`.
2. Put it in a user group that has **backoffice access**. No specific section is
   required — every SchemeWeaver endpoint uses standard backoffice
   authentication, nothing more. Administrators works; so does a least-privilege
   custom group, provided it can access the backoffice at all. Make sure the
   user is **enabled**.
3. Open the user → **Client Credentials** → add a client ID and generate a
   secret. Copy both immediately — the secret is shown once.

**THE CLIENT ID INCLUDES UMBRACO'S PREFIX.** Umbraco prefixes API client IDs
with `umbraco-back-office-`; the effective ID is the whole thing (e.g. you type
`mcp`, the real client ID is `umbraco-back-office-mcp`). Supplying only the
suffix is the single most common cause of 401s — always use the full prefixed
value.

## Step 2 — install the plugin

Skip this if `schemeweaver` tools already appear in the session.

```text
/plugin marketplace add EnjoyDigital/Umbraco.Community.Schemeweaver
/plugin install schemeweaver-mcp@schemeweaver
/reload-plugins
```

The installer prompts for three values:

- **Umbraco Base URL** — the site root only (e.g. `https://localhost:44308` or
  `https://www.example.com`). No `/umbraco`, no path, no trailing slash.
- **API User Client ID** — the full prefixed ID from Step 1.
- **API User Client Secret** — stored securely by Claude Code, never in files.

Already installed but with wrong values? Update the plugin's configuration (or
reinstall it), then `/reload-plugins`.

## Step 3 — the verification ladder

Run the rungs IN ORDER — each one isolates a different failure, so do not skip
ahead to debugging credentials when the tools are not even loaded.

1. **Are the tools present?** If no `schemeweaver` tools are listed at all, the
   plugin is not loaded: check `/plugin`, run `/reload-plugins`, restart Claude
   Code. Nothing else can work until this passes.
2. **`get-server-info`** — the smoke test. Success proves the base URL is
   reachable, the OAuth client-credentials token was issued, and backoffice
   authentication was accepted. Sanity-check the returned Umbraco version and
   base URL against what the user expects.
3. **`list-content-types`** — proves SchemeWeaver itself is installed in that
   site and its management endpoints answer. A site WITHOUT the SchemeWeaver
   package passes rung 2 and fails here — that distinction is the whole point
   of running both rungs.

Declare the connection working only when rungs 2 AND 3 both pass.

## Troubleshooting

| Symptom | Cause | Fix |
|---|---|---|
| 401 / `invalid_client` / token request fails | Wrong client ID or secret; missing `umbraco-back-office-` prefix; API user disabled | Use the full prefixed client ID; re-check or regenerate the secret; confirm the user is enabled and its group has backoffice access |
| HTML login page or redirect where JSON was expected | Base URL wrong — points at a path, the wrong host, or includes `/umbraco` | Set the base URL to the bare site root |
| `ECONNREFUSED` / `ENOTFOUND` / timeout | Site not running, or wrong host/port/scheme | Confirm the site is up and the URL is exact (scheme and port included) |
| Self-signed certificate error (local dev) | The plugin forwards only `UMBRACO_*` variables to the server, so TLS overrides cannot be set via plugin config | Export `NODE_TLS_REJECT_UNAUTHORIZED=0` in the shell BEFORE launching Claude Code (local development only — never against production) |
| `get-server-info` passes but `list-content-types` 404s | The SchemeWeaver NuGet package is not installed in that Umbraco site | Install `Umbraco.Community.SchemeWeaver` in the site and restart it |
| Tools worked, then vanished after a config change | Plugin needs reloading | `/reload-plugins`, or restart Claude Code |
| Secret was pasted somewhere visible | Secrets shown in chat or files are compromised | Regenerate the secret in the backoffice and reconfigure the plugin |
| Everything passes but responses are empty | Fresh site with no content types yet | Not a connection problem — create content types, or generate them from Schema.org via the `generate-content-type` tool |
| `get-rendered-json-ld` later returns 404/401 | NOT a setup failure — Umbraco's Delivery API is off by default and may be API-key protected | See the `schemeweaver-audit` skill's live-output phase; it covers Delivery API access properly |

## Next steps

- First mapping: run the **`schemeweaver-map`** skill
  (`/schemeweaver-mcp:schemeweaver-map`) — it drives the full inspect → map →
  validate loop for one content type.
- Site already has mappings? Run **`schemeweaver-audit`** for a whole-site
  coverage and quality audit.

## Stopping condition

Setup is complete when `get-server-info` returns server information AND
`list-content-types` returns the site's content types. Report both results to
the user, then stop — mapping work belongs to `schemeweaver-map`, not here.
