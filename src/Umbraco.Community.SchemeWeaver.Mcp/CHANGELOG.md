# Changelog

All notable changes to the SchemeWeaver MCP server are documented here.

## 1.1.1

- Remove the generate-shim in the `save-schema-mapping` tool. The `reachability`
  and `warnings` output-only fields are now folded into the OpenAPI
  `SchemaMappingDto` and flow straight from the generated Orval client, so
  `outputSchema` is the generated `postSchemeweaverMappingsResponse` with no
  hand-written `.extend()`. No behavioural change for callers.

## 1.1.0

- Surface `reachability` and structural `warnings` on the save/read mapping
  responses.
