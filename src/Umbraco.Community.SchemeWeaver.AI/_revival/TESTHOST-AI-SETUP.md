# TestHost AI Setup (Anthropic provider, Umbraco 17 build only)

Umbraco.AI manages providers, connections, and profiles via the backoffice (Settings →
Umbraco AI). There is currently no programmatic seeding API exposed publicly, so the
Anthropic connection and the three SchemeWeaver profiles must be created manually after
first boot. This file is the authoritative reference for doing that.

---

## 1. Supply the Anthropic API key via user-secrets

The API key must **never** be committed. Use .NET user-secrets:

```bash
cd src/Umbraco.Community.SchemeWeaver.TestHost

dotnet user-secrets set "Umbraco:AI:Secrets:AnthropicApiKey" "sk-ant-api03-..."
```

The secret is stored outside the repo under `%APPDATA%\Microsoft\UserSecrets\95d126b6-eee0-4838-8ec9-afa38a12a91f\secrets.json` (the `UserSecretsId` in the TestHost csproj).

---

## 2. Build and start the TestHost (17-only)

```bash
dotnet run --project src/Umbraco.Community.SchemeWeaver.TestHost \
           -p:UmbracoMajor=17
```

Navigate to `https://localhost:5001/umbraco` and log in as `admin@test.com` /
`SecurePass1234`.

---

## 3. Create the Anthropic connection

1. Go to **Settings → Umbraco AI → Providers → Add connection**.
2. Select provider: **Anthropic**.
3. Set **Name** (any label, e.g. `Anthropic – SchemeWeaver dev`).
4. Set **API Key** to the config-reference literal (not the actual key):

   ```
   $Umbraco:AI:Secrets:AnthropicApiKey
   ```

   Umbraco.AI resolves `$Prefix:Key` from `IConfiguration` at request time —
   the value comes from user-secrets, never from the database.

5. Leave **Endpoint** as `https://api.anthropic.com` (default).
6. Save. The connection will appear as active once the API key resolves.

---

## 4. Create the three SchemeWeaver profiles

Go to **Settings → Umbraco AI → Profiles → Add profile** for each entry below.

| Alias                                  | Model                  | Used for                                          |
|----------------------------------------|------------------------|---------------------------------------------------|
| `schemeweaver-schema-type-suggestion`  | `claude-sonnet-4-…`    | Schema.org type suggestion from content type name |
| `schemeweaver-bulk-schema-suggestion`  | `claude-sonnet-4-…`    | Bulk schema type suggestion for all content types |
| `schemeweaver-property-mapping`        | `claude-sonnet-4-…`    | Property-to-Schema.org property mapping           |

For each profile:

1. **Alias** — exact alias from the table above (the code looks up profiles by alias).
2. **Connection** — select the Anthropic connection created in step 3.
3. **Model** — pick any Claude Sonnet 4 model from the dropdown (requires a live API key
   to enumerate models; `claude-sonnet-4-20250514` is the latest as of June 2026).
4. Save.

> **Missing profile = heuristic fallback, not a crash.** If a profile alias is not
> found, `IAIChatService.GetChatResponseAsync` throws, `AISchemaMapper` catches it, and
> the response falls back to the synchronous heuristic mapper. Compile + wiring tests
> therefore pass without any key or profile.

---

## 5. Verify end-to-end

1. Open a document type in the backoffice.
2. Click **Actions → Map to Schema.org**.
3. Click **Auto-map** — the suggestion list should now be AI-generated (higher quality
   than the heuristic, and the log will show no `AISchemaMapper fallback` messages).

Alternatively, call the API directly:

```http
POST /umbraco/management/api/v1/schemeweaver/mappings/{alias}/auto-map?schemaTypeName=Article
Authorization: Bearer <backoffice-token>
```

---

## Notes

- The `Umbraco:AI:Secrets` prefix is the default allowed prefix in `AIOptions`
  (`AllowedConfigurationKeyPrefixes` + `SecretConfigurationKeyPrefixes`). No extra
  configuration is needed for the `$Umbraco:AI:Secrets:*` references to resolve.
- The `Umbraco:AI:Secrets:AnthropicApiKey` key name is a convention used in this
  project; any key under `Umbraco:AI:Secrets` is valid — just be consistent between
  the user-secret and the connection's ApiKey field.
- Connections and profiles are stored in the Umbraco database. Wiping the SQLite DB
  (e.g. `rm umbraco/Data/Umbraco.sqlite.db`) means recreating them on next boot.
