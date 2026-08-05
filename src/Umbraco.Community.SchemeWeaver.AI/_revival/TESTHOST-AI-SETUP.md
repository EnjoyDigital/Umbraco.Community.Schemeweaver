# TestHost AI Setup (Anthropic provider, Umbraco 17 build only)

> **SUPERSEDED (2026-08-05):** the satellite now builds for Umbraco 17 AND 18 with
> major-aligned Umbraco.AI lines (17.x / 18.x), and the TestHost includes it for both
> majors. The "17 build only" framing below is historical.

Umbraco.AI manages providers, connections, and profiles via the backoffice (the **AI**
section). There is currently no programmatic seeding API exposed publicly, so the
Anthropic connection and the SchemeWeaver profile(s) must be created manually after
first boot. This file is the authoritative reference for doing that.

## ✅ Verified working end-to-end (2026-06-15)

A real-key run confirmed the satellite produces AI-driven property mappings in the live
backoffice (NewsArticle → articlePage: `bodyText`→ArticleBody, `title`→Headline,
`heroImage`→Image, `authorName`→Author, built-in URL→Url — all semantic, no heuristic
fallback in the log). Three gotchas were found along the way — **read these first**:

1. **Reference the umbrella `Umbraco.AI` package, NOT `Umbraco.AI.Startup` alone.**
   Startup is backend-only; the **AI backoffice section** (connections/profiles UI) comes
   from `Umbraco.AI.Web.StaticAssets`, which only the umbrella `Umbraco.AI` package pulls
   in. With Startup alone there is no AI section and you cannot configure anything. (The
   TestHost csproj now references `Umbraco.AI`.)
2. **Set a Default Chat Profile.** In Umbraco.AI 1.14, `chat.WithAlias("…")` did **not**
   resolve our named profiles by alias at runtime — the chat service fell through to the
   *default* chat profile and threw "Default Chat profile is not configured". Fix: AI
   section → **Settings → Default Chat Profile → Add → pick the SchemeWeaver profile →
   Save**. (Follow-up worth verifying: whether `WithAlias` should select a profile by
   alias in 1.14, or whether `AISchemaMapper` should use `WithProfile`/the `profileAlias`
   parameter instead. Until then, a Default Chat Profile is required.)
3. **Dedicated 17 DB.** The TestHost shares one SQLite DB across both majors
   (`umbraco/Data/Umbraco.sqlite.db`), so a DB last upgraded by the default-18 build
   cannot be migrated by the 17 build ("Premigrations does not support migrating from
   state …"). For an Umbraco-17 AI session, point the 17 host at its own DB, e.g. run
   with `ConnectionStrings__umbracoDbDSN` set to `…/Umbraco17.sqlite.db` (or temporarily
   edit `appsettings.Development.json`). It installs fresh and re-seeds via uSync
   `ImportOnFirstBoot`. **Let the uSync first-boot import finish before hitting the
   backoffice** — concurrent OAuth/login traffic during the import deadlocks SQLite.

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
