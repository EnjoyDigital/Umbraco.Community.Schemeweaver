# SchemeWeaver.AI revival — verified contract (Umbraco.AI 1.14)

> Phase-0 spike output. **All of the recovered satellite code compiles clean against
> Umbraco.AI.Core 1.14.0 with 0 warnings / 0 errors** (built with `-p:UmbracoMajor=17`).
> The original was written against Core ~1.8; the API we use did **not** break.
> This file is the single source of truth for the parallel agents — read it first.

## Resolved versions (Umbraco 17 build)
- `Umbraco.AI.Core` **1.14.0** (csproj range `[1.14.0, 2.0.0)`).
- Transitively unifies `Umbraco.Cms.*` to **17.4.0** (the satellite + main package both
  compile against 17.4.0; Umbraco.AI.Core requires `>= 17.4.0 && < 18.0.0`).
- `Umbraco.AI.Anthropic` **1.3.6** — the provider. **NOT referenced by the satellite**
  (provider-agnostic). Installed by the **host** (TestHost + consumer docs). Depends on
  Core `>= 1.14.0` and `Anthropic >= 12.20.1`.
- **17-only.** No Umbraco 18 build of Umbraco.AI exists. The satellite csproj has a
  `GuardUmbracoMajor` target that hard-errors if built with `UmbracoMajor != 17`. Keep
  it out of the default (18) solution build; build with `-p:UmbracoMajor=17`.

## Chat API (`Umbraco.AI.Core.Chat.IAIChatService`) — VERIFIED COMPILING
```csharp
using Microsoft.Extensions.AI;        // ChatMessage, ChatRole
using Umbraco.AI.Core.Chat;           // IAIChatService

var response = await _chatService.GetChatResponseAsync(
    chat => chat.WithAlias("schemeweaver-property-mapping"),   // builder lambda + alias
    [
        new ChatMessage(ChatRole.System, SystemPrompts.PropertyMapping),
        new ChatMessage(ChatRole.User, userPrompt),
    ],
    ct);

string text = response.Text ?? "";    // response.Text works on 1.14 (NOT .Message.Text)
```
- `ChatMessage` / `ChatRole` come from `Microsoft.Extensions.AI` (Umbraco.AI builds on M.E.AI).
- The `WithAlias("...")` aliases used: `schemeweaver-schema-type-suggestion`,
  `schemeweaver-bulk-schema-suggestion`, `schemeweaver-property-mapping`. These map to
  **profiles** the host configures (see "Provider config" below) — but a missing profile
  just throws, which our code catches → heuristic fallback. So compile + wiring tests
  pass without any key.

## Tool / scope API — VERIFIED COMPILING
```csharp
using Umbraco.AI.Core.Tools;          // [AITool], AIToolBase<TArgs>
using Umbraco.AI.Core.Tools.Scopes;   // [AIToolScope], AIToolScopeBase

[AIToolScope("schemeweaver-mapping", Icon = "icon-brackets", Domain = "SchemeWeaver")]
public sealed class SchemeWeaverMappingScope : AIToolScopeBase
{ public const string ScopeId = "schemeweaver-mapping"; }

public record SuggestSchemaTypeArgs(
    [property: System.ComponentModel.Description("...")] string ContentTypeAlias);

[AITool("schemeweaver_suggest_schema_type", "Suggest Schema.org Type", ScopeId = SchemeWeaverMappingScope.ScopeId)]
public class SuggestSchemaTypeTool : AIToolBase<SuggestSchemaTypeArgs>
{
    public override string Description => "...";
    protected override async Task<object> ExecuteAsync(SuggestSchemaTypeArgs args, CancellationToken ct = default) { ... }
}
```
- Tools/scopes are **auto-discovered** by Umbraco.AI from the assembly — the composer only
  calls `AddControllers().AddApplicationPart(...)` and registers `IAISchemaMapper`. No
  explicit tool registration needed.
- Tools resolve scoped services via `IServiceScopeFactory.CreateScope()` (Umbraco.AI pattern;
  avoids singleton/scoped capture).

## The seam (landed in Phase 0 — already on this branch)
`ISchemaAutoMapper` now has BOTH:
```csharp
IEnumerable<PropertyMappingSuggestion> SuggestMappings(string alias, string schemaTypeName);             // sync (unchanged)
Task<IEnumerable<PropertyMappingSuggestion>> SuggestMappingsAsync(string alias, string schemaTypeName);  // NEW
```
- `SchemaAutoMapper.SuggestMappingsAsync` = `Task.FromResult(SuggestMappings(...))`.
- `ISchemeWeaverService.AutoMapAsync` + `SchemeWeaverService.AutoMapAsync` delegate to it.
- Controller `POST /mappings/{alias}/auto-map` is now `async` and awaits `AutoMapAsync`.
  **Route + response shape unchanged** → frontend + MCP unaffected.

### Agent A: the override
Create `AiSchemaAutoMapper : ISchemaAutoMapper` in the satellite. Implement
`SuggestMappingsAsync` with a real LLM call (reuse `AISchemaMapper.SuggestPropertyMappingsAsync`
logic + `ExtractJson` + `MergeSuggestions`); `SuggestMappings` (sync) and
`RankSchemaProperties` delegate to the injected heuristic `SchemaAutoMapper`.

Composer (`SchemeWeaverAIComposer`) — register so the override wins and the heuristic is
still resolvable as the fallback dependency:
```csharp
[ComposeAfter(typeof(Umbraco.Community.SchemeWeaver.Composing.SchemeWeaverComposer))]
public class SchemeWeaverAIComposer : IComposer {
  public void Compose(IUmbracoBuilder b) {
    b.Services.AddControllers().AddApplicationPart(typeof(SchemeWeaverAIComposer).Assembly);
    b.Services.AddScoped<IAISchemaMapper, AISchemaMapper>();
    b.Services.AddScoped<SchemaAutoMapper>();                                  // concrete = fallback
    b.Services.AddScoped<ISchemaAutoMapper>(sp => new AiSchemaAutoMapper(      // override (last wins)
        sp.GetRequiredService<SchemaAutoMapper>(), /* IAIChatService, registry, ILogger ... */));
  }
}
```
NOTE: registering `ISchemaAutoMapper` again replaces the main package's registration because
the AI composer runs after it (`[ComposeAfter]`). The controller/service resolve the single
`ISchemaAutoMapper`, so both the Auto-Map button and the MCP `suggest-property-mappings` tool
become AI-powered for free.

## Provider config (host-level — Agent D)
Umbraco.AI 1.14 configures providers via **connections + profiles** (managed in the backoffice;
also seedable). The Anthropic API key is NOT a bare `Anthropic:ApiKey` appsetting used directly
by our code — it is consumed by the Anthropic provider/connection. For the TestHost:
- `.AddUmbracoAI()` in the builder chain (after `.AddBackOffice().AddWebsite()`).
- Install `Umbraco.AI.Anthropic` in the TestHost csproj.
- Seed an Anthropic connection + a profile per alias above (or document manual backoffice setup).
- API key via `dotnet user-secrets` (never committed). Verify the exact appsettings/secret key
  the Anthropic provider 1.3.6 reads (check its package or backoffice connection UI).

## Gotchas confirmed
- `response.Text` (not `.Message.Text`) on 1.14 — already correct in recovered code.
- Build the satellite ONLY with `-p:UmbracoMajor=17`.
- AI failures must fall back to heuristic (already coded) — wiring tests run keyless.
