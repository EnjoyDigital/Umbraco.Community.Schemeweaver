import { defineConfig } from "orval";
import { orvalImportFixer } from "@umbraco-cms/mcp-server-sdk";

/**
 * Orval Configuration
 *
 * Generates the typed SchemeWeaver API client and Zod schemas from
 * src/umbraco-api/api/schemeweaver-openapi.json.
 *
 * That spec is extracted from a running Umbraco instance's management API
 * document by scripts/extract-openapi.mjs (run automatically as part of
 * `npm run generate`). The extracted spec is committed, so regeneration
 * without a running instance is possible via `orval --config orval.config.ts`.
 */
export default defineConfig({
  schemeWeaverApi: {
    input: {
      target: "./src/umbraco-api/api/schemeweaver-openapi.json",
      validation: false,
    },
    output: {
      target: "./src/umbraco-api/api/generated/schemeWeaverApi.ts",
      client: "axios",
      mode: "single",
      clean: false,
      override: {
        mutator: {
          path: "./src/umbraco-api/api/client.ts",
          name: "customInstance",
        },
      },
    },
    hooks: {
      afterAllFilesWrite: orvalImportFixer,
    },
  },

  // Zod schema generation for validation
  schemeWeaverApiZod: {
    input: {
      target: "./src/umbraco-api/api/schemeweaver-openapi.json",
      validation: false,
    },
    output: {
      target: "./src/umbraco-api/api/generated/schemeWeaverApi.zod.ts",
      client: "zod",
      mode: "single",
      clean: false,
    },
  },
});
