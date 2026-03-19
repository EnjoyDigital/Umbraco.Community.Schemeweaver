import { LitElement } from 'lit';

/**
 * Minimal mock of UmbLitElement for unit tests.
 * Provides a stub localize.term() that returns the key suffix (after the section prefix).
 */
export class UmbLitElement extends LitElement {
  localize = {
    term(key) {
      // Return the key as-is so tests can assert on localisation keys or raw values.
      // In production, Umbraco resolves '#section_key' to the translated string.
      return key;
    },
  };

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
