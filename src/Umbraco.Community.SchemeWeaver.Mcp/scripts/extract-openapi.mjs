/**
 * Extracts the SchemeWeaver subset of the Umbraco management OpenAPI document.
 *
 * The full management document (~1.4 MB) describes every management API in the
 * instance. Feeding it to Orval would generate a client for all of Umbraco, so
 * this script pulls out only the paths tagged "SchemeWeaver" plus the component
 * schemas they (transitively) reference, and writes the result to
 * src/umbraco-api/api/schemeweaver-openapi.json — the input for `npm run generate`.
 *
 * Requires a running Umbraco instance with SchemeWeaver installed.
 * Usage: node scripts/extract-openapi.mjs [base-url]
 */

import { writeFileSync } from "node:fs";
import { fileURLToPath } from "node:url";
import path from "node:path";

const TAG = "SchemeWeaver";
const baseUrl = process.argv[2] || process.env.UMBRACO_BASE_URL || "https://localhost:44308";
const specUrl = `${baseUrl.replace(/\/$/, "")}/umbraco/openapi/management.json`;

// Local instances use self-signed certificates
process.env.NODE_TLS_REJECT_UNAUTHORIZED = "0";

const response = await fetch(specUrl);
if (!response.ok) {
  console.error(`Failed to fetch ${specUrl}: ${response.status} ${response.statusText}`);
  console.error("Is the Umbraco instance running?");
  process.exit(1);
}
const doc = await response.json();

// Keep only paths where every operation is tagged SchemeWeaver
const paths = {};
for (const [route, pathItem] of Object.entries(doc.paths ?? {})) {
  const operations = Object.values(pathItem).filter((op) => op && typeof op === "object" && op.tags);
  if (operations.length > 0 && operations.every((op) => op.tags.includes(TAG))) {
    paths[route] = pathItem;
  }
}

if (Object.keys(paths).length === 0) {
  console.error(`No paths tagged "${TAG}" found in ${specUrl}`);
  process.exit(1);
}

// Walk $refs transitively to collect the component schemas the paths need
const neededSchemas = new Set();
const collectRefs = (node) => {
  if (Array.isArray(node)) {
    node.forEach(collectRefs);
  } else if (node && typeof node === "object") {
    for (const [key, value] of Object.entries(node)) {
      if (key === "$ref" && typeof value === "string") {
        const match = value.match(/^#\/components\/schemas\/(.+)$/);
        if (match && !neededSchemas.has(match[1])) {
          neededSchemas.add(match[1]);
          collectRefs(doc.components?.schemas?.[match[1]]);
        }
      } else {
        collectRefs(value);
      }
    }
  }
};
collectRefs(paths);

const schemas = {};
for (const name of [...neededSchemas].sort()) {
  schemas[name] = doc.components.schemas[name];
}

const extracted = {
  openapi: doc.openapi,
  info: {
    title: "SchemeWeaver Management API",
    description: `SchemeWeaver endpoints extracted from the Umbraco management API document (${specUrl})`,
    version: doc.info?.version ?? "1.0",
  },
  paths,
  components: {
    schemas,
    securitySchemes: doc.components?.securitySchemes ?? {},
  },
};

const outPath = path.resolve(
  path.dirname(fileURLToPath(import.meta.url)),
  "../src/umbraco-api/api/schemeweaver-openapi.json"
);
writeFileSync(outPath, JSON.stringify(extracted, null, 2) + "\n");
console.log(
  `Wrote ${Object.keys(paths).length} paths and ${neededSchemas.size} schemas to ${outPath}`
);
