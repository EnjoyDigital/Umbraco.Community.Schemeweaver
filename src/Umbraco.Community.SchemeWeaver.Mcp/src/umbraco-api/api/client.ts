/**
 * API Client Configuration
 *
 * Custom Orval mutator that delegates to the SDK's UmbracoManagementClient,
 * which handles OAuth client-credentials authentication and token refresh.
 *
 * In unit tests with USE_MOCK_API=true, MSW intercepts the underlying HTTP
 * requests (see src/mocks/). Integration tests hit the real Umbraco instance.
 */

import {
  UmbracoManagementClient,
  type HttpResponse,
} from "@umbraco-cms/mcp-server-sdk";

interface RequestConfig {
  method?: string;
  url?: string;
  data?: unknown;
  params?: Record<string, unknown>;
  headers?: Record<string, string>;
  [key: string]: unknown;
}

/**
 * Custom fetch-based instance for API calls, used by Orval-generated code.
 */
export const customInstance = async <T>(
  config: RequestConfig,
  options?: RequestConfig
): Promise<HttpResponse<T> | T> => {
  const mergedConfig = { ...config, ...options };
  return UmbracoManagementClient<T>(mergedConfig as any, mergedConfig as any);
};

export default customInstance;
