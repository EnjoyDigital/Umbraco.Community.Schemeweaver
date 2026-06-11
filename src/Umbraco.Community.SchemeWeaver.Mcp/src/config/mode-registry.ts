/**
 * Tool Mode Registry
 *
 * Defines tool modes that group tools by domain/functionality.
 * Modes map to collections, allowing users to enable groups of related tools.
 *
 * This is the SINGLE SOURCE OF TRUTH for mode definitions in this project.
 */

import type { ToolModeDefinition } from "@umbraco-cms/mcp-server-sdk";

export const toolModes: ToolModeDefinition[] = [
  {
    name: 'schemeweaver',
    displayName: 'SchemeWeaver',
    description: 'Schema.org mapping and JSON-LD generation tools',
    collections: ['schemeweaver']
  },
  {
    name: 'umbraco-server',
    displayName: 'Umbraco Server',
    description: 'Server information and status from the Umbraco Management API',
    collections: ['umbraco-server']
  },
];

/**
 * All mode definitions (alias for toolModes).
 */
export const allModes: ToolModeDefinition[] = [...toolModes];

/**
 * All valid mode names for configuration validation.
 */
export const allModeNames: readonly string[] = toolModes.map(m => m.name);

/**
 * Valid mode name type.
 */
export type ToolModeName = typeof allModeNames[number];
