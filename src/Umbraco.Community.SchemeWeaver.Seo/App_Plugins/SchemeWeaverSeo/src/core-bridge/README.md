# core-bridge

The only folder allowed to import from the core plugin's `src/` (by relative path; the modules
are compiled into this bundle). Keep it to pure modules: types, the
authenticated fetch helper, the core context token. Never value-import a module that
defines a custom element or provides a context, or the two bundles would double-register.
A unit test (`core-bridge/bridge-guard.test.ts`) enforces the folder boundary.
