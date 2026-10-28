/**
 * Jest Global Setup for MSW
 *
 * This file is loaded by Jest's setupFilesAfterEnv configuration.
 * MSW is only started when USE_MOCK_API=true — this enables mock API
 * interception for unit tests without a real Umbraco instance.
 *
 * In this monorepo: set via `npm run test:template` (USE_MOCK_API=true)
 * On scaffolded sites: not set by default — tests hit the real Umbraco API
 *
 * The lifecycle is wired inline rather than through the SDK's
 * `setupMswServer` helper: that helper is typed against msw 2's
 * `listen({ onUnhandledRequest })`, which msw 3 renamed to `onUnhandledFrame`.
 */

import { server } from "./server.js";
import { resetStore } from "./store.js";

if (process.env.USE_MOCK_API === "true") {
  beforeAll(() => {
    server.listen({ onUnhandledFrame: "error" });
  });
  afterEach(() => {
    server.resetHandlers();
    resetStore();
  });
  afterAll(() => {
    server.close();
  });
}
