/**
 * Tool Collections Export
 *
 * Lightweight entry point for in-process chaining.
 * Import this from another MCP server to chain tools without spawning a process.
 *
 * @example
 * ```typescript
 * import { collections, allModes, allModeNames, allSliceNames } from "@umbraco-community/schemeweaver-mcp/collections";
 *
 * manager.registerServer({
 *   transport: "in-process",
 *   name: "schemeweaver",
 *   collections,
 *   modeRegistry: allModes,
 *   allModeNames,
 *   allSliceNames,
 * });
 * ```
 */

import schemeWeaverCollection from "./umbraco-api/tools/schemeweaver/index.js";
import umbracoServerCollection from "./umbraco-api/tools/umbraco-server/index.js";

export const collections = [
  schemeWeaverCollection,
  umbracoServerCollection,
];

export { allModes, allModeNames } from "./config/mode-registry.js";
export { allSliceNames } from "./config/slice-registry.js";
