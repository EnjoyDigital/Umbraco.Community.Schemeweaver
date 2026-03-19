import { LitElement } from 'lit';

export class UmbModalBaseElement extends LitElement {
  localize = {
    term(key) {
      return key;
    },
  };

  constructor() {
    super();
    this._data = {};
    this.modalContext = {
      setValue: () => {},
      submit: () => {},
      reject: () => {},
    };
  }

  consumeContext(token, callback) {}

  get data() {
    return this._data;
  }

  set data(v) {
    this._data = v;
  }
}

export const UMB_MODAL_MANAGER_CONTEXT = Symbol('UMB_MODAL_MANAGER_CONTEXT');

export class UmbModalToken {
  constructor(alias, config) {
    this.alias = alias;
    this.config = config;
  }
}
