import { defineConfig } from "tsup";

export default defineConfig({
  entry: ["src/index.ts", "src/collections.ts"],
  format: ["esm"],
  target: "node22",
  clean: true,
  sourcemap: true,
  splitting: false,
  bundle: true,
  // Bundle every runtime dependency into the output so dist/index.js runs
  // standalone with no node_modules present — required because the Claude Code
  // plugin launches it as `node ${CLAUDE_PLUGIN_ROOT}/dist/index.js`. tsup still
  // externalises Node's own `node:` builtins.
  noExternal: [/.*/],
  // Some bundled CJS deps (e.g. cross-spawn) use dynamic require() of Node
  // builtins, which an ESM bundle can't resolve and otherwise throws
  // "Dynamic require of X is not supported". Inject a real createRequire so
  // those calls resolve at runtime.
  banner: {
    js: "import { createRequire as __cw_createRequire } from 'module'; const require = __cw_createRequire(import.meta.url);",
  },
  treeshake: true,
  minify: false,
  dts: false,
});
