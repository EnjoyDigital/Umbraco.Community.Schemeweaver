import { LitElement } from 'lit';

export class UmbLitElement extends LitElement {
  observe(observable, callback, alias) {
    if (observable && typeof observable.getValue === 'function') {
      callback(observable.getValue());
    }
  }

  async getContext(token) {
    return {};
  }

  consumeContext(token, callback) {}

  provideContext(token, instance) {}
}
