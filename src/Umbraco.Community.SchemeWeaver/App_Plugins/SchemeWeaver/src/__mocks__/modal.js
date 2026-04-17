import { LitElement } from 'lit';
import { resolveLocalizationKey } from './lit-element.js';
import { __mockContextRegistry } from './context-api.js';

const localize = {
  term: (key) => resolveLocalizationKey(key),
};

export class UmbModalBaseElement extends LitElement {
  constructor() {
    super();
    this._data = {};
    this.localize = localize;
    this.modalContext = {
      setValue: () => {},
      submit: () => {},
      reject: () => {},
    };
  }

  get data() {
    return this._data;
  }

  set data(v) {
    this._data = v;
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
}

export const UMB_MODAL_MANAGER_CONTEXT = Symbol('UMB_MODAL_MANAGER_CONTEXT');

export class UmbModalToken {
  constructor(alias, config) {
    this.alias = alias;
    this.config = config;
  }
}

export const UMB_CONFIRM_MODAL = new UmbModalToken('umb-confirm-modal', {});
