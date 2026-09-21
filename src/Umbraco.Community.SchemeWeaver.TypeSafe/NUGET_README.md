# SchemeWeaver TypeSafe

Calibrated, typed AI judgments for [Umbraco.Community.SchemeWeaver](https://www.nuget.org/packages/Umbraco.Community.SchemeWeaver)'s Schema.org auto-mapper, powered by [TypeSafe](https://typesafe.ai) System One (Jev).

Where SchemeWeaver's built-in auto-mapper matches property names heuristically, this satellite asks a System One model small typed questions over closed option sets, and code owns every rule:

- **Better auto-map suggestions, no UI change.** The satellite decorates the `ISchemaAutoMapper` seam, so the existing auto-map step in the property mapping modal and the MCP server's `suggest-property-mappings` tool return TypeSafe results with the same thresholds (80 pre-ticked, 60 shown).
- **Calibrated confidence.** Every suggestion carries a real probability from the model rather than a fixed tier score, so the auto-apply and show thresholds mean what they say.
- **Nothing to parse, nothing invented.** The model can only select from the properties and types the code offers it; nested types are chosen by walking down the Schema.org tree from the property's declared range, so an out-of-range nested type is unrepresentable.
- **Always falls back.** No key, `Enabled: false`, or an unreachable API and every call goes to the prior mapper (the heuristic, or the AI satellite when both are installed). A backoffice Health Check ("SchemeWeaver TypeSafe") and one startup log line tell you which is active.
- **No AI framework.** Only `HttpClient`; around 100 ms per request and $42 per billion input tokens (about $0.0018 per content type on the project's eval harness).

## Requirements

- **Umbraco 17 or 18**: install the package version matching your Umbraco major (17.x for Umbraco 17, 18.x for Umbraco 18).
- **Umbraco.Community.SchemeWeaver** at the same version as this package.
- A **TypeSafe API key**, supplied via `dotnet user-secrets set "SchemeWeaver:TypeSafe:ApiKey" "..."` or the `SchemeWeaver__TypeSafe__ApiKey` environment variable (never appsettings).

```bash
dotnet add package Umbraco.Community.SchemeWeaver.TypeSafe
```

Full setup, configuration, the four-round mapping model, and troubleshooting: [TypeSafe Integration documentation](https://github.com/EnjoyDigital/Umbraco.Community.Schemeweaver/blob/main/docs/typesafe-integration.md).
