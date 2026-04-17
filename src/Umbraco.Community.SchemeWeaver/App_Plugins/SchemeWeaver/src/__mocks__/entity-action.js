import { __mockContextRegistry } from './context-api.js';

export class UmbEntityActionBase {
  constructor(host, args) {
    this.host = host;
    this.args = args || {};
  }

  getHostElement() {
    return document.createElement('div');
  }

  addUmbController() {}

  removeUmbController() {}

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

  async execute() {}
}

export class UmbRequestReloadStructureForEntityEvent {
  static TYPE = 'request-reload-structure-for-entity';
  constructor() {}
}
