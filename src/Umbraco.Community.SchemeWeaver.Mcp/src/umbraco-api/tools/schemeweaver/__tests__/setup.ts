/**
 * Shared test setup for SchemeWeaver tools
 *
 * Integration tests run against a real Umbraco instance (the SchemeWeaver
 * TestHost) using the credentials from .env. See src/mocks/jest-setup.ts and
 * jest.setup.ts for the global environment wiring.
 */

import {
  setupTestEnvironment,
  createMockRequestHandlerExtra,
} from "@umbraco-cms/mcp-server-sdk/testing";
import { configureApiClient } from "@umbraco-cms/mcp-server-sdk";
import { getSchemeWeaverManagementAPI } from "../../../api/generated/schemeWeaverApi.js";

configureApiClient(() => getSchemeWeaverManagementAPI());

export { setupTestEnvironment, createMockRequestHandlerExtra };
