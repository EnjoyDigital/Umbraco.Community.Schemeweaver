import { expect, fixture, html, oneEvent } from '@open-wc/testing';
import './slot-editor.element.js';
import type { SeoSlotEditorElement } from './slot-editor.element.js';
import { CATALOGUE } from '../mocks/data/seo.db.js';
import { seoDb } from '../mocks/data/seo.db.js';

const article = CATALOGUE.templates[0];
const properties = seoDb.getProperties('blogArticle');

async function mount(schemaProperty: string, value: SeoSlotEditorElement['value'] = { kind: 'none' }, showAll = false) {
  const slot = article.slots.find((s) => s.schemaProperty === schemaProperty)!;
  return fixture<SeoSlotEditorElement>(
    html`<schemeweaver-seo-slot-editor .definition=${slot} .value=${value} .properties=${properties} .showAllFields=${showAll} .siteIdentityConfigured=${true}></schemeweaver-seo-slot-editor>`,
  );
}

function options(el: SeoSlotEditorElement) {
  const select = el.shadowRoot!.querySelector('uui-select') as HTMLElement & { options: Array<{ value: string; group?: string; selected?: boolean }> };
  return select.options;
}

describe('schemeweaver-seo-slot-editor', () => {
  it('offers only compatible fields plus the slot built-ins by default', async () => {
    const el = await mount('headline');
    const values = options(el).map((o) => o.value);
    expect(values).to.include('property:title');
    expect(values).to.include('property:body', 'rich text is accepted for text slots (stripped server-side)');
    expect(values).to.include('property:__name');
    expect(values).to.not.include('property:heroImage', 'a media picker is not a text source');
    expect(values).to.not.include('property:price');
    expect(values).to.include('fixed');
    expect(values).to.not.include('siteIdentity', 'headline has no identity piece');
  });

  it('shows every field when "show all fields" is on', async () => {
    const el = await mount('headline', { kind: 'none' }, true);
    expect(options(el).map((o) => o.value)).to.include('property:price');
  });

  it('offers the site identity and nested parts for entity slots', async () => {
    const el = await mount('publisher', { kind: 'siteIdentity', targetPieceKey: 'organization' });
    const values = options(el).map((o) => o.value);
    expect(values).to.include('siteIdentity');
    expect(values).to.include('nested');
    expect(options(el).find((o) => o.selected)!.value).to.equal('siteIdentity');
  });

  it('emits slot-change with a property value and its editor alias', async () => {
    const el = await mount('headline');
    const select = el.shadowRoot!.querySelector('uui-select') as unknown as HTMLSelectElement;
    setTimeout(() => {
      select.value = 'property:body';
      select.dispatchEvent(new Event('change'));
    });
    const event = (await oneEvent(el, 'slot-change')) as CustomEvent;
    expect(event.detail.schemaProperty).to.equal('headline');
    expect(event.detail.value).to.deep.equal({ kind: 'property', propertyAlias: 'body', editorAlias: 'Umbraco.RichText' });
  });

  it('renders sub-slots when "fill in parts" is chosen and nests their values', async () => {
    const el = await mount('author', { kind: 'nested', nested: {} });
    await el.updateComplete;
    const subs = el.shadowRoot!.querySelectorAll('schemeweaver-seo-slot-editor');
    expect(subs.length).to.equal(2);
    const subSelect = subs[0].shadowRoot!.querySelector('uui-select') as unknown as HTMLSelectElement;
    setTimeout(() => {
      subSelect.value = 'property:authorName';
      subSelect.dispatchEvent(new Event('change'));
    });
    const event = (await oneEvent(el, 'slot-change')) as CustomEvent;
    expect(event.detail.value.kind).to.equal('nested');
    expect(event.detail.value.nested.name.propertyAlias).to.equal('authorName');
  });

  it('shows read-only tags for block and advanced rows', async () => {
    const el = await mount('description', { kind: 'advanced', summary: 'ancestor: sectionIntro' });
    expect(el.shadowRoot!.querySelector('uui-select')).to.equal(null);
    expect(el.shadowRoot!.textContent).to.contain('ancestor: sectionIntro');
  });
});
