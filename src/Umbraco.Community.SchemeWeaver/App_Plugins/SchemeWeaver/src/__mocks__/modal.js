import { LitElement } from 'lit';
import { resolveLocalizationKey } from './lit-element.js';

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
    return {};
  }

  consumeContext(token, callback) {}

  provideContext(token, instance) {}
}

export const UMB_MODAL_MANAGER_CONTEXT = Symbol('UMB_MODAL_MANAGER_CONTEXT');

export class UmbModalToken {
  constructor(alias, config) {
    this.alias = alias;
    this.config = config;
  }
}

export const UMB_CONFIRM_MODAL = new UmbModalToken('umb-confirm-modal', {});
