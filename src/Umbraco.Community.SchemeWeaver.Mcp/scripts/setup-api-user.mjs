/**
 * Creates the Umbraco API user this MCP server authenticates as.
 *
 * Logs in to the backoffice as an admin user, performs the OAuth
 * authorization-code (PKCE) flow to obtain a management API token, then
 * creates an API user in the Administrators group and attaches the
 * client credentials from .env (UMBRACO_CLIENT_ID / UMBRACO_CLIENT_SECRET).
 *
 * Usage:
 *   node scripts/setup-api-user.mjs <admin-email> <admin-password> [base-url]
 *
 * Idempotent: exits successfully if the client id already works.
 */

import crypto from "node:crypto";

process.env.NODE_TLS_REJECT_UNAUTHORIZED = "0";

const [, , adminUser, adminPassword, baseUrlArg] = process.argv;
const baseUrl = (baseUrlArg || process.env.UMBRACO_BASE_URL || "https://localhost:44308").replace(/\/$/, "");
const clientId = process.env.UMBRACO_CLIENT_ID || "umbraco-back-office-mcp";
const clientSecret = process.env.UMBRACO_CLIENT_SECRET;

if (!adminUser || !adminPassword || !clientSecret) {
  console.error("Usage: node scripts/setup-api-user.mjs <admin-email> <admin-password> [base-url]");
  console.error("UMBRACO_CLIENT_ID and UMBRACO_CLIENT_SECRET must be set (e.g. via node --env-file=.env).");
  process.exit(1);
}

const TOKEN_URL = `${baseUrl}/umbraco/management/api/v1/security/back-office/token`;

// 0. Idempotency check: do the credentials already work?
const probe = await fetch(TOKEN_URL, {
  method: "POST",
  headers: { "Content-Type": "application/x-www-form-urlencoded" },
  body: new URLSearchParams({ grant_type: "client_credentials", client_id: clientId, client_secret: clientSecret }),
});
if (probe.ok) {
  console.log(`Client '${clientId}' already authenticates successfully — nothing to do.`);
  process.exit(0);
}

// 1. Backoffice login (cookie-based)
const loginResponse = await fetch(`${baseUrl}/umbraco/management/api/v1/security/back-office/login`, {
  method: "POST",
  headers: { "Content-Type": "application/json" },
  body: JSON.stringify({ username: adminUser, password: adminPassword }),
});
if (!loginResponse.ok) {
  console.error(`Backoffice login failed: ${loginResponse.status} ${await loginResponse.text()}`);
  process.exit(1);
}
const cookies = loginResponse.headers
  .getSetCookie()
  .map((c) => c.split(";")[0])
  .join("; ");

// 2. OAuth authorize with PKCE (as the logged-in admin).
// Umbraco redacts the authorization code in the redirect ("code=[redacted]")
// and instead stores the real code in a __Host-umbPkceCode cookie; the token
// endpoint expects the literal string "[redacted]" plus that cookie.
const codeVerifier = crypto.randomBytes(32).toString("base64url");
const codeChallenge = crypto.createHash("sha256").update(codeVerifier).digest("base64url");
const redirectUri = `${baseUrl}/umbraco/oauth_complete`;
const authorizeUrl = new URL(`${baseUrl}/umbraco/management/api/v1/security/back-office/authorize`);
authorizeUrl.search = new URLSearchParams({
  client_id: "umbraco-back-office",
  response_type: "code",
  redirect_uri: redirectUri,
  code_challenge: codeChallenge,
  code_challenge_method: "S256",
}).toString();

const authorizeResponse = await fetch(authorizeUrl, { headers: { Cookie: cookies }, redirect: "manual" });
// The response deletes any previous PKCE cookie before setting the new one,
// so take the last non-empty occurrence.
const pkceCookie = authorizeResponse.headers
  .getSetCookie()
  .map((c) => c.split(";")[0])
  .filter((c) => /^(__Host-)?umbPkceCode=.+/.test(c))
  .pop();
if (authorizeResponse.status !== 302 || pkceCookie === undefined) {
  console.error(`Authorize step failed (${authorizeResponse.status}): no PKCE code cookie returned.`);
  process.exit(1);
}

// 3. Exchange code for an access token
const tokenResponse = await fetch(TOKEN_URL, {
  method: "POST",
  headers: {
    "Content-Type": "application/x-www-form-urlencoded",
    Cookie: `${pkceCookie}; ${cookies}`,
    Origin: baseUrl,
  },
  body: new URLSearchParams({
    grant_type: "authorization_code",
    code: "[redacted]",
    client_id: "umbraco-back-office",
    redirect_uri: redirectUri,
    code_verifier: codeVerifier,
  }),
});
if (!tokenResponse.ok) {
  console.error(`Token exchange failed: ${tokenResponse.status} ${await tokenResponse.text()}`);
  process.exit(1);
}
// The access token is also redacted from the response body and delivered as an
// encrypted umbAccessToken cookie. Sending "Bearer [redacted]" together with
// that cookie makes the server swap the real token back in.
const accessTokenCookie = tokenResponse.headers
  .getSetCookie()
  .map((c) => c.split(";")[0])
  .filter((c) => /^(__Host-)?umbAccessToken=.+/.test(c))
  .pop();
if (!accessTokenCookie) {
  console.error("Token exchange succeeded but no access token cookie was returned.");
  process.exit(1);
}
const authHeaders = {
  Authorization: "Bearer [redacted]",
  Cookie: `${accessTokenCookie}; ${cookies}`,
  "Content-Type": "application/json",
};

// 4. Find the Administrators user group
const groupsResponse = await fetch(`${baseUrl}/umbraco/management/api/v1/user-group?skip=0&take=100`, {
  headers: authHeaders,
});
const groups = await groupsResponse.json();
const adminGroup = groups.items?.find((g) => g.alias === "admin" || g.name === "Administrators");
if (!adminGroup) {
  console.error("Could not find the Administrators user group.");
  process.exit(1);
}

// 5. Create the API user (idempotent-ish: reuse if the email already exists)
const apiUserEmail = `${clientId}@mcp.local`;
let userId = null;
const createUserResponse = await fetch(`${baseUrl}/umbraco/management/api/v1/user`, {
  method: "POST",
  headers: authHeaders,
  body: JSON.stringify({
    kind: "Api",
    email: apiUserEmail,
    userName: apiUserEmail,
    name: "SchemeWeaver MCP",
    userGroupIds: [{ id: adminGroup.id }],
  }),
});
if (createUserResponse.ok) {
  userId = createUserResponse.headers.get("location")?.split("/").pop()
    ?? (await createUserResponse.json().catch(() => null))?.id;
} else {
  // Possibly already exists — look it up by email
  const filterResponse = await fetch(
    `${baseUrl}/umbraco/management/api/v1/filter/user?skip=0&take=10&filter=${encodeURIComponent(apiUserEmail)}`,
    { headers: authHeaders }
  );
  const existing = (await filterResponse.json()).items?.find((u) => u.email === apiUserEmail);
  if (!existing) {
    console.error(`Failed to create API user: ${createUserResponse.status} ${await createUserResponse.text()}`);
    process.exit(1);
  }
  userId = existing.id;
  console.log(`API user already exists (${userId}); attaching client credentials.`);
}

// 6. Attach the client credentials
const credentialsResponse = await fetch(`${baseUrl}/umbraco/management/api/v1/user/${userId}/client-credentials`, {
  method: "POST",
  headers: authHeaders,
  body: JSON.stringify({ clientId, clientSecret }),
});
if (!credentialsResponse.ok) {
  console.error(`Failed to attach client credentials: ${credentialsResponse.status} ${await credentialsResponse.text()}`);
  process.exit(1);
}

// 7. Verify
const verify = await fetch(TOKEN_URL, {
  method: "POST",
  headers: { "Content-Type": "application/x-www-form-urlencoded" },
  body: new URLSearchParams({ grant_type: "client_credentials", client_id: clientId, client_secret: clientSecret }),
});
if (!verify.ok) {
  console.error(`Verification token request failed: ${verify.status} ${await verify.text()}`);
  process.exit(1);
}
console.log(`API user ready — client '${clientId}' can now obtain management API tokens.`);
