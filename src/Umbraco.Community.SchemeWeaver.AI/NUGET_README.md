# SchemeWeaver AI

AI-powered Schema.org mapping for [Umbraco.Community.SchemeWeaver](https://www.nuget.org/packages/Umbraco.Community.SchemeWeaver), built on [Umbraco.AI](https://www.nuget.org/packages/Umbraco.AI).

Where SchemeWeaver's built-in auto-mapper matches property names heuristically, this satellite asks an LLM to reason about your content types semantically:

- **AI Analyse** on any document type suggests the most specific fitting Schema.org type, with confidence scores and reasoning.
- **AI Analyse All** bulk-analyses every unmapped document type from the Document Types root.
- Property auto-mapping becomes AI-authoritative: well-formed AI suggestions win outright, the heuristic fills anything the AI leaves unmapped, and the endpoint falls back entirely to heuristic suggestions if the AI call fails — AI enhances accuracy without sacrificing reliability.
- Ships Umbraco Copilot tools (suggest schema type, map properties, save/list mappings) so the mapping workflow is available to agents too.

## Requirements

- **Umbraco 17 or 18** — install the package version matching your Umbraco major (17.x for Umbraco 17, 18.x for Umbraco 18). The matching major-aligned `Umbraco.AI` line is pulled automatically.
- **Umbraco.Community.SchemeWeaver** at the same version as this package.
- An **Umbraco.AI chat provider** of your choice (e.g. `Umbraco.AI.Anthropic` or `Umbraco.AI.OpenAI`) with a configured connection, a chat profile (set Max Tokens), and a **Default Chat Profile** selected in the AI section's settings.

```bash
dotnet add package Umbraco.Community.SchemeWeaver.AI
```

Full setup, configuration, and troubleshooting: [AI Integration documentation](https://github.com/EnjoyDigital/Umbraco.Community.Schemeweaver/blob/main/docs/ai-integration.md).
