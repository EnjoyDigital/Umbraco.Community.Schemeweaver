/**
 * Unit tests for the shared base-URL helpers.
 *
 * Hostless and pure — these run in the worktree without a live TestHost.
 * (testMatch is **\/__tests__/**\/*.test.ts; this file ends in .test.ts.)
 */

import {
  resolveBaseUrl,
  resolveRenderHost,
  buildRenderedJsonLdUrl,
  DEFAULT_BASE_URL,
} from "../base-url.js";

describe("resolveBaseUrl", () => {
  const original = process.env.UMBRACO_BASE_URL;

  afterEach(() => {
    if (original === undefined) {
      delete process.env.UMBRACO_BASE_URL;
    } else {
      process.env.UMBRACO_BASE_URL = original;
    }
  });

  it("falls back to the default when UMBRACO_BASE_URL is unset", () => {
    delete process.env.UMBRACO_BASE_URL;
    expect(resolveBaseUrl()).toBe(DEFAULT_BASE_URL);
  });

  it("uses UMBRACO_BASE_URL when set", () => {
    process.env.UMBRACO_BASE_URL = "https://example.test";
    expect(resolveBaseUrl()).toBe("https://example.test");
  });

  it("strips a single trailing slash", () => {
    process.env.UMBRACO_BASE_URL = "https://example.test/";
    expect(resolveBaseUrl()).toBe("https://example.test");
  });

  it("strips multiple trailing slashes", () => {
    process.env.UMBRACO_BASE_URL = "https://example.test///";
    expect(resolveBaseUrl()).toBe("https://example.test");
  });
});

describe("resolveRenderHost", () => {
  const original = process.env.UMBRACO_BASE_URL;

  beforeEach(() => {
    process.env.UMBRACO_BASE_URL = "https://configured.test";
  });

  afterEach(() => {
    if (original === undefined) {
      delete process.env.UMBRACO_BASE_URL;
    } else {
      process.env.UMBRACO_BASE_URL = original;
    }
  });

  it("falls back to the configured base when host is undefined", () => {
    expect(resolveRenderHost(undefined)).toBe("https://configured.test");
  });

  it("falls back to the configured base when host is an empty string", () => {
    expect(resolveRenderHost("")).toBe("https://configured.test");
  });

  it("falls back to the configured base when host is whitespace only", () => {
    expect(resolveRenderHost("   ")).toBe("https://configured.test");
  });

  it("uses an explicit host when provided", () => {
    expect(resolveRenderHost("https://www.example.com")).toBe("https://www.example.com");
  });

  it("strips trailing slashes from an explicit host", () => {
    expect(resolveRenderHost("https://www.example.com///")).toBe("https://www.example.com");
  });

  it("trims surrounding whitespace from an explicit host", () => {
    expect(resolveRenderHost("  https://www.example.com/  ")).toBe("https://www.example.com");
  });
});

describe("buildRenderedJsonLdUrl", () => {
  const base = "https://localhost:44308";

  it("includes the route and the by-route path", () => {
    const url = buildRenderedJsonLdUrl({ base, route: "/" });
    expect(url).toContain(
      "https://localhost:44308/umbraco/delivery/api/v2/schemeweaver/json-ld/by-route?"
    );
    expect(url).toContain("route=%2F");
  });

  it("omits scope and culture when undefined", () => {
    const url = buildRenderedJsonLdUrl({ base, route: "/" });
    expect(url).not.toContain("scope=");
    expect(url).not.toContain("culture=");
  });

  it("appends scope when provided", () => {
    const url = buildRenderedJsonLdUrl({ base, route: "/", scope: "page" });
    expect(url).toContain("scope=page");
  });

  it("appends culture when provided", () => {
    const url = buildRenderedJsonLdUrl({ base, route: "/", culture: "en-US" });
    expect(url).toContain("culture=en-US");
  });

  it("encodes routes with spaces and ampersands", () => {
    const url = buildRenderedJsonLdUrl({ base, route: "/a b&c" });
    // URLSearchParams encodes space as '+' and '&' as %26
    expect(url).toContain("route=%2Fa+b%26c");
    expect(url).not.toContain("/a b&c");
  });

  it("builds all params together", () => {
    const url = buildRenderedJsonLdUrl({
      base,
      route: "/blog/post",
      scope: "all",
      culture: "de-DE",
    });
    expect(url).toContain("route=%2Fblog%2Fpost");
    expect(url).toContain("scope=all");
    expect(url).toContain("culture=de-DE");
  });
});
