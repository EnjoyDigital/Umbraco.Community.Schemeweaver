import { __mockContextRegistry } from './context-api.js';

/**
 * Mirrors the real UmbConditionBase: `config` from args, a `permitted` setter
 * that notifies `onChange` only when the value actually flips, and the
 * controller-host helpers conditions rely on.
 */
export class UmbConditionBase {
  #permitted = false;
  #onChange;

  constructor(host, args) {
    this.host = host;
    this.config = args?.config;
    this.#onChange = args?.onChange;
  }

  get permitted() {
    return this.#permitted;
  }

  set permitted(value) {
    if (value === this.#permitted) return;
    this.#permitted = value;
    this.#onChange?.(value);
  }

  async getContext(token) {
    return __mockContextRegistry.consume(token);
  }

  consumeContext(token, callback) {
    const instance = __mockContextRegistry.consume(token);
    if (instance) callback(instance);
    return { destroy() {} };
  }

  observe(observable, callback) {
    if (observable && typeof observable.getValue === 'function') {
      callback(observable.getValue());
    }
  }

  destroy() {}
}

export class UmbExtensionRegistry {
  constructor() {
    this._extensions = [];
  }

  register(extension) {
    this._extensions.push(extension);
  }

  getByAlias(alias) {
    return this._extensions.find(e => e.alias === alias);
  }
}
