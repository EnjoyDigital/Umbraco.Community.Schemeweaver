import { expect, fixture, html } from '@open-wc/testing';
import './property-combobox.element.js';
import type { PropertyComboboxElement } from './property-combobox.element.js';

describe('PropertyComboboxElement', () => {
  it('renders a uui-combobox', async () => {
    const el = await fixture<PropertyComboboxElement>(
      html`<schemeweaver-property-combobox></schemeweaver-property-combobox>`,
    );
    const combobox = el.shadowRoot!.querySelector('uui-combobox');
    expect(combobox).to.exist;
  });

  it('renders list options from properties array', async () => {
    const el = await fixture<PropertyComboboxElement>(
      html`<schemeweaver-property-combobox .properties=${['title', 'description', 'heroImage']}></schemeweaver-property-combobox>`,
    );
    const options = el.shadowRoot!.querySelectorAll('uui-combobox-list-option');
    expect(options.length).to.equal(3);
  });

  it('shows friendly names for built-in properties', async () => {
    const el = await fixture<PropertyComboboxElement>(
      html`<schemeweaver-property-combobox .properties=${['__url', '__name', 'title']}></schemeweaver-property-combobox>`,
    );
    const options = el.shadowRoot!.querySelectorAll('uui-combobox-list-option');
    expect(options[0].textContent!.trim()).to.contain('URL (Built-in)');
    expect(options[1].textContent!.trim()).to.contain('Name (Built-in)');
    expect(options[2].textContent!.trim()).to.equal('title');
  });

  it('sets value on the combobox', async () => {
    const el = await fixture<PropertyComboboxElement>(
      html`<schemeweaver-property-combobox .properties=${['title', 'desc']} .value=${'title'}></schemeweaver-property-combobox>`,
    );
    const combobox = el.shadowRoot!.querySelector('uui-combobox') as any;
    expect(combobox.value).to.equal('title');
  });

  it('renders empty list when no properties given', async () => {
    const el = await fixture<PropertyComboboxElement>(
      html`<schemeweaver-property-combobox></schemeweaver-property-combobox>`,
    );
    const options = el.shadowRoot!.querySelectorAll('uui-combobox-list-option');
    expect(options.length).to.equal(0);
  });

  it('filters properties on search', async () => {
    const el = await fixture<PropertyComboboxElement>(
      html`<schemeweaver-property-combobox .properties=${['title', 'description', 'heroImage']}></schemeweaver-property-combobox>`,
    );

    // Simulate search event
    const combobox = el.shadowRoot!.querySelector('uui-combobox') as any;
    if (combobox) {
      combobox.search = 'hero';
      combobox.dispatchEvent(new CustomEvent('search'));
      await el.updateComplete;
    }

    const options = el.shadowRoot!.querySelectorAll('uui-combobox-list-option');
    expect(options.length).to.equal(1);
    expect(options[0].textContent!.trim()).to.equal('heroImage');
  });

  it('filters by display name for built-in properties', async () => {
    const el = await fixture<PropertyComboboxElement>(
      html`<schemeweaver-property-combobox .properties=${['__url', '__name', 'title']}></schemeweaver-property-combobox>`,
    );

    const combobox = el.shadowRoot!.querySelector('uui-combobox') as any;
    if (combobox) {
      combobox.search = 'Built-in';
      combobox.dispatchEvent(new CustomEvent('search'));
      await el.updateComplete;
    }

    const options = el.shadowRoot!.querySelectorAll('uui-combobox-list-option');
    expect(options.length).to.equal(2);
  });

  it('resets filter when search is cleared', async () => {
    const el = await fixture<PropertyComboboxElement>(
      html`<schemeweaver-property-combobox .properties=${['title', 'description', 'heroImage']}></schemeweaver-property-combobox>`,
    );

    const combobox = el.shadowRoot!.querySelector('uui-combobox') as any;
    if (combobox) {
      // First filter
      combobox.search = 'hero';
      combobox.dispatchEvent(new CustomEvent('search'));
      await el.updateComplete;

      // Then clear
      combobox.search = '';
      combobox.dispatchEvent(new CustomEvent('search'));
      await el.updateComplete;
    }

    const options = el.shadowRoot!.querySelectorAll('uui-combobox-list-option');
    expect(options.length).to.equal(3);
  });

  it('dispatches change event with value', async () => {
    const el = await fixture<PropertyComboboxElement>(
      html`<schemeweaver-property-combobox .properties=${['title', 'desc']}></schemeweaver-property-combobox>`,
    );

    let eventDetail: any = null;
    el.addEventListener('change', (e: Event) => {
      eventDetail = (e as CustomEvent).detail;
    });

    const combobox = el.shadowRoot!.querySelector('uui-combobox') as any;
    if (combobox) {
      combobox.value = 'title';
      combobox.dispatchEvent(new CustomEvent('change'));
      await el.updateComplete;
    }

    expect(eventDetail).to.exist;
    expect(eventDetail.value).to.equal('title');
  });

  it('escapes special regex characters in search', async () => {
    const el = await fixture<PropertyComboboxElement>(
      html`<schemeweaver-property-combobox .properties=${['title', 'test.field', 'other']}></schemeweaver-property-combobox>`,
    );

    const combobox = el.shadowRoot!.querySelector('uui-combobox') as any;
    if (combobox) {
      combobox.search = 'test.field';
      combobox.dispatchEvent(new CustomEvent('search'));
      await el.updateComplete;
    }

    const options = el.shadowRoot!.querySelectorAll('uui-combobox-list-option');
    expect(options.length).to.equal(1);
  });
});
