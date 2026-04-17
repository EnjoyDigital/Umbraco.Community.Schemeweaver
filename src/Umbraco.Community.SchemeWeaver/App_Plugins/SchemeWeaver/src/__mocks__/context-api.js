export class UmbContextToken {
  constructor(alias) {
    this.alias = alias;
  }

  toString() {
    return this.alias;
  }
}

/**
 * Simple global context registry used by the test mocks. Production uses
 * Umbraco's real context tree — tests don't have one, so we fake it by
 * stashing every `provideContext(token, instance)` into a token-keyed map.
 * `consumeContext` / `getContext` in the mock `UmbLitElement` /
 * `UmbModalBaseElement` / `UmbControllerBase` / `UmbEntityActionBase` base
 * classes look up by the same key.
 *
 * Tokens may be `UmbContextToken` instances (production) or bare `Symbol`s
 * (some mocks). For tokens we key by `alias`; for symbols we key by
 * `Symbol.prototype.toString()` since symbols aren't value-comparable across
 * module boundaries when re-imported.
 */
const registry = new Map();

function keyFor(token) {
  if (!token) return undefined;
  if (typeof token === 'symbol') return token.toString();
  if (typeof token === 'object' && 'alias' in token) return token.alias;
  return String(token);
}

export const __mockContextRegistry = {
  provide(token, instance) {
    const key = keyFor(token);
    if (key) registry.set(key, instance);
  },
  consume(token) {
    const key = keyFor(token);
    return key ? registry.get(key) : undefined;
  },
  reset() {
    registry.clear();
  },
};
