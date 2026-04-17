import { __mockContextRegistry } from './context-api.js';

export class UmbControllerBase {
  constructor(host) {
    this._host = host;
  }

  getHostElement() {
    return this._host;
  }

  async getContext(token) {
    return __mockContextRegistry.consume(token);
  }

  consumeContext(token, callback) {
    const instance = __mockContextRegistry.consume(token);
    if (instance) callback(instance);
    return { destroy() {} };
  }

  provideContext(token, instance) {
    __mockContextRegistry.provide(token, instance);
  }

  destroy() {}
}
