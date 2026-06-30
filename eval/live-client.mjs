// Shared live management-API client for the eval harness (used by heuristic-live.mjs and
// tier3-mcp.mjs). Authenticates as the SchemeWeaver MCP API user via the backoffice
// client-credentials grant — the same path tier2-verify.mjs uses. A fresh token per call keeps
// slow agentic loops from expiring a short-lived backoffice token mid-run.
//
// Requires the TestHost on :44308 and the MCP API user creds in
// src/Umbraco.Community.SchemeWeaver.Mcp/.env (UMBRACO_CLIENT_ID / UMBRACO_CLIENT_SECRET).

import { readFileSync } from 'node:fs';
import { join } from 'node:path';

export const BASE = process.env.UMBRACO_URL || 'https://localhost:44308';
const MGMT = `${BASE}/umbraco/management/api/v1`;
process.env.NODE_TLS_REJECT_UNAUTHORIZED = '0';

function creds() {
  const env = readFileSync(join(process.cwd(), 'src/Umbraco.Community.SchemeWeaver.Mcp/.env'), 'utf8');
  const get = (k) => (env.match(new RegExp(`^${k}=(.*)$`, 'm')) || [])[1]?.trim();
  return { id: get('UMBRACO_CLIENT_ID'), secret: get('UMBRACO_CLIENT_SECRET') };
}

export async function token() {
  const { id, secret } = creds();
  const r = await fetch(`${MGMT}/security/back-office/token`, {
    method: 'POST',
    headers: { 'content-type': 'application/x-www-form-urlencoded' },
    body: new URLSearchParams({ grant_type: 'client_credentials', client_id: id, client_secret: secret }),
  });
  if (!r.ok) throw new Error(`token ${r.status}: ${await r.text()}`);
  return (await r.json()).access_token;
}

async function call(method, path, { query, body } = {}) {
  const tok = await token();
  const qs = query ? '?' + new URLSearchParams(query).toString() : '';
  const r = await fetch(`${MGMT}/schemeweaver${path}${qs}`, {
    method,
    headers: { authorization: `Bearer ${tok}`, 'content-type': 'application/json' },
    body: body === undefined ? undefined : JSON.stringify(body),
  });
  const text = await r.text();
  if (!r.ok) throw new Error(`${method} ${path} -> ${r.status}: ${text.slice(0, 200)}`);
  return text ? JSON.parse(text) : null;
}

// Thin verb wrappers, all rooted at /schemeweaver. `post` defaults to an empty JSON body
// because several SchemeWeaver POST endpoints (auto-map, preview) take their input via the
// query string and reject a missing body.
export const api = {
  get: (path, query) => call('GET', path, { query }),
  post: (path, query, body = {}) => call('POST', path, { query, body }),
  del: (path) => call('DELETE', path, {}),
};
