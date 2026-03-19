# Umbraco.Community.SchemeWeaver

Schema.org mapping for Umbraco Content Types. Maps document types to Schema.org types and generates JSON-LD structured data.

## Features

- **Schema.org Registry** — discovers all Schema.org types from Schema.NET
- **Auto-mapping** — suggests property mappings between content types and schema types
- **JSON-LD generation** — produces valid JSON-LD from published content
- **Backoffice UI** — dashboard, modals, and workspace view for managing mappings
- **Content Type Generation** — create Umbraco document types from Schema.org definitions
- **Delivery API integration** — adds JSON-LD to the content delivery API

## Requirements

- Umbraco 17+
- .NET 10

## Installation

```bash
dotnet add package Umbraco.Community.SchemeWeaver
```

## Development

```bash
# Build
dotnet build

# Test
dotnet test

# Frontend (in src/Umbraco.Community.SchemeWeaver/App_Plugins/SchemeWeaver)
npm install
npm run build
npm test
```

## Licence

MIT — see [LICENSE](LICENSE).
