import { expect, fixture, html, oneEvent } from '@open-wc/testing';
import './schema-type-input.element.js';
import type { SchemaTypeInputElement } from './schema-type-input.element.js';

describe('SchemaTypeInputElement', () => {
  it('renders a typeable input and a browse button', async () => {
    const el = await fixture<SchemaTypeInputElement>(
      html`<schemeweaver-schema-type-input .value=${'Question'}></schemeweaver-schema-type-input>`,
    );
    const input = el.shadowRoot!.querySelector('uui-input');
    const browse = el.shadowRoot!.querySelector('.browse-btn');
    expect(input, 'input present').to.not.equal(null);
    expect(browse, 'browse button present').to.not.equal(null);
    expect((input as any).value).to.equal('Question');
  });

  it('emits change with the typed value', async () => {
    const el = await fixture<SchemaTypeInputElement>(
      html`<schemeweaver-schema-type-input></schemeweaver-schema-type-input>`,
    );
    const input = el.shadowRoot!.querySelector('uui-input') as HTMLInputElement;
    input.value = 'Answer';
    setTimeout(() => input.dispatchEvent(new Event('change', { bubbles: true, composed: true })));
    await oneEvent(el, 'change');
    expect(el.value).to.equal('Answer');
  });
});
