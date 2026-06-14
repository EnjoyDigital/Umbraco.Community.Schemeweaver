# AI Integration

SchemeWeaver offers an optional companion package, **Umbraco.Community.SchemeWeaver.AI**, that uses [Umbraco.AI](https://marketplace.umbraco.com/package/umbraco.ai.core) to provide AI-powered schema mapping. When installed, it adds AI suggestion buttons to the existing SchemeWeaver UI and registers Copilot tools for conversational schema mapping.

If the AI package is not installed, SchemeWeaver works exactly as before -- the heuristic auto-mapper handles all suggestions.

---

## Requirements

| Requirement | Version |
|---|---|
| Umbraco.Community.SchemeWeaver | Same version as the AI package |
| Umbraco.AI.Core | 1.7.0 or later (up to, but not including, 2.0.0) |
| A configured AI chat provider | e.g. Azure OpenAI, Anthropic (via Umbraco.AI provider packages) |

The AI package depends on `IAIChatService` from Umbraco.AI.Core. You must have at least one chat provider configured in your Umbraco instance for AI features to work. Refer to the [Umbraco.AI documentation](https://docs.umbraco.com/umbraco-ai) for provider setup.

---

## Installation

Install the satellite package into the same project as SchemeWeaver:

```bash
dotnet add package Umbraco.Community.SchemeWeaver.AI --prerelease
```

No additional SchemeWeaver configuration is needed. The `SchemeWeaverAIComposer` registers the AI services and controller automatically. The frontend detects the AI package by calling `GET /ai/status` and shows AI buttons only when it returns successfully.

---

## Features

### AI Schema Type Suggestions

When you open the schema picker modal (**Map to Schema.org** on a document type), the AI package adds an **AI Analyse** entity action to the document type actions menu. This action:

1. Analyses the content type's name, property names, editor types, and descriptions
2. Returns up to 3 ranked Schema.org type suggestions with confidence scores and reasoning
3. Opens the schema picker with the top suggestion pre-highlighted

The AI validates its suggestions against SchemeWeaver's type registry, so only valid Schema.org types from the Schema.NET.Pending library are returned.

### AI Bulk Analysis

The **AI Analyse All** entity action appears on the Document Types root node. It opens a modal that analyses every non-element content type in a single batch:

- Results are displayed as a table with columns for content type name, suggested schema type, confidence score, and reasoning
- Rows with confidence of 70% or above are pre-selected
- Confidence is shown as a colour-coded tag: green (80%+), amber (50-79%), grey (below 50%)
- Click **Apply** to create mappings for all selected rows in one operation

For each selected row, the bulk apply process:

1. Calls the AI auto-map endpoint to get property mapping suggestions
2. Filters suggestions to those with confidence of 50% or above
3. Saves the mapping via the standard SchemeWeaver API

### AI Property Mapping

When mapping properties (either from the bulk flow or the individual mapping modal), the AI package enhances the auto-mapping process:

- The AI analyses content type properties semantically, understanding that `bodyText` maps to `articleBody` even without an explicit synonym entry
- AI suggestions are **merged** with heuristic suggestions: for each schema property, the suggestion with the higher confidence score wins
- If the AI call fails, the endpoint falls back entirely to heuristic suggestions

This merge strategy means AI enhances accuracy without sacrificing reliability.

### Umbraco Copilot Tools

The AI package registers four tools under the `schemeweaver-mapping` scope for use with Umbraco's AI Copilot:

| Tool | Description |
|---|---|
| `schemeweaver_suggest_schema_type` | Suggest Schema.org types for a content type |
| `schemeweaver_map_properties` | Suggest property mappings for a content type / schema type pair |
| `schemeweaver_save_mapping` | Save a schema mapping (marked as destructive) |
| `schemeweaver_list_mappings` | List all existing schema mappings |

These tools allow conversational workflows like "Map my Blog Post content type to Schema.org" through the Umbraco Copilot interface.

---

## API Endpoints

All AI endpoints are under `/umbraco/management/api/v1/schemeweaver/ai` and require backoffice authentication. For full request/response details, see the [API Reference](api-reference.md#ai-integration-optional).

| Method | Endpoint | Description |
|---|---|---|
| `GET` | `/ai/status` | Check if the AI package is installed (200 = yes, 404 = no) |
| `POST` | `/ai/suggest-schema-type/{contentTypeAlias}` | AI schema type suggestions for one content type |
| `POST` | `/ai/suggest-schema-types-bulk` | AI schema type suggestions for all content types |
| `POST` | `/ai/ai-auto-map/{contentTypeAlias}?schemaTypeName=X` | AI-enhanced property mapping suggestions |

---

## How It Works

The `AISchemaMapper` service orchestrates all AI operations:

1. **System prompts** guide the LLM with Schema.org expertise, Umbraco editor type awareness (TextBox, RichText, MediaPicker3, BlockList, etc.), and a calibrated confidence scale
2. **JSON extraction** handles markdown fences and extra text that LLMs sometimes wrap around JSON responses
3. **Registry validation** filters AI suggestions against the actual Schema.NET.Pending type list, discarding any hallucinated type names
4. **Merge strategy** for property mapping always retrieves heuristic suggestions as a baseline, then overlays AI suggestions where the AI's confidence is higher

The architecture ensures that AI is additive -- it can only improve on heuristic results, never degrade them.

---

## Troubleshooting

### AI buttons do not appear in the UI

The frontend checks `GET /ai/status` on load. If it returns 404, the AI package is not installed or registered. Verify:

1. The `Umbraco.Community.SchemeWeaver.AI` NuGet package is referenced in your project
2. The application has been restarted after installation
3. No DI registration errors appear in the Umbraco log at startup

### AI suggestions return errors

If the AI endpoints return 500 errors, check:

1. Umbraco.AI.Core is installed and configured with a chat provider
2. The chat provider's API key / connection string is valid
3. The Umbraco log for detailed error messages from `AISchemaMapper`

### AI suggests invalid Schema.org types

The AI validates suggestions against the Schema.NET.Pending type registry. If you see unexpected results, the LLM may be suggesting types that exist in Schema.org but are not yet in the Schema.NET.Pending library. The invalid suggestions are automatically filtered out before being returned to the UI.
