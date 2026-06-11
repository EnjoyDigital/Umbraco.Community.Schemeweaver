/**
 * Server Configuration
 *
 * Loads the base Umbraco MCP configuration (connection credentials, tool
 * filtering, readonly mode) from CLI arguments, environment variables and
 * .env files. Custom fields can be added to `customFields` if this server
 * ever needs configuration beyond the SDK's built-ins.
 */

import {
  getServerConfig,
  type ConfigFieldDefinition,
  type UmbracoServerConfig,
  type GetServerConfigResult,
} from "@umbraco-cms/mcp-server-sdk";

/**
 * Custom configuration specific to this MCP server. None needed yet.
 */
export interface MyServerCustomConfig {}

const customFields: ConfigFieldDefinition[] = [];

// ============================================================================
// Config Loading
// ============================================================================

export interface ServerConfig {
  /** Base Umbraco MCP configuration */
  umbraco: UmbracoServerConfig;
  /** Custom configuration for this server */
  custom: MyServerCustomConfig;
  /** CLI introspection flags */
  cliFlags: GetServerConfigResult["cliFlags"];
}

let cachedConfig: ServerConfig | null = null;

/**
 * Load server configuration from CLI arguments and environment variables.
 *
 * @param isStdioMode - Whether the server is running in stdio mode (suppresses logging)
 * @returns Combined base and custom configuration
 */
export async function loadServerConfig(isStdioMode: boolean): Promise<ServerConfig> {
  if (cachedConfig) {
    return cachedConfig;
  }

  const { config, custom, cliFlags } = await getServerConfig(isStdioMode, {
    additionalFields: customFields,
  });

  cachedConfig = {
    umbraco: config,
    custom: custom as MyServerCustomConfig,
    cliFlags,
  };

  return cachedConfig;
}

/**
 * Clear cached config (useful for testing)
 */
export function clearConfigCache(): void {
  cachedConfig = null;
}

/**
 * Get the custom field definitions (useful for testing/documentation)
 */
export function getCustomFieldDefinitions(): ConfigFieldDefinition[] {
  return [...customFields];
}
