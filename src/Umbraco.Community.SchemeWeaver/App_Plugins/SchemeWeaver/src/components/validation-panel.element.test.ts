import { expect, fixture, html } from '@open-wc/testing';
import './validation-panel.element.js';
import type { ValidationPanelElement } from './validation-panel.element.js';
import type { ValidationIssue } from '../api/types.js';

/**
 * Covers the validation-panel element in isolation. The preview component's
 * own test file covers the wiring from `JsonLdPreviewResponse.issues` into
 * the panel.
 */
describe('ValidationPanelElement', () => {
  it('renders the empty state when no issues are supplied', async () => {
    const el = await fixture<ValidationPanelElement>(
      html`<schemeweaver-validation-panel></schemeweaver-validation-panel>`,
    );

    const empty = el.shadowRoot!.querySelector('.panel.empty');
    expect(empty, 'empty-state panel should render when issues is undefined').to.exist;

    // Without a localisation provider mounted, `this.localize.term()` returns
    // the key verbatim. Match either the English string (when localised) or
    // the raw key — both are evidence the empty state rendered.
    expect(empty!.textContent).to.match(/No issues|schemeWeaver_validation_noIssues/i);

    // Empty state uses the check icon.
    const icon = empty!.querySelector('uui-icon');
    expect(icon!.getAttribute('name')).to.equal('icon-check');
  });

  it('renders the empty state when the issues array is empty', async () => {
    const el = await fixture<ValidationPanelElement>(
      html`<schemeweaver-validation-panel .issues=${[] as ValidationIssue[]}></schemeweaver-validation-panel>`,
    );

    const empty = el.shadowRoot!.querySelector('.panel.empty');
    expect(empty).to.exist;

    const issueRows = el.shadowRoot!.querySelectorAll('.issue');
    expect(issueRows.length).to.equal(0);
  });

  it('groups issues by severity in critical → warning → info order', async () => {
    const issues: ValidationIssue[] = [
      { severity: 'info', schemaType: 'Article', path: '$', message: 'nice-to-have' },
      { severity: 'warning', schemaType: 'Article', path: '@graph[0].dateModified', message: 'recommended' },
      { severity: 'critical', schemaType: 'Article', path: '@graph[0].headline', message: 'required' },
      { severity: 'warning', schemaType: 'Article', path: '@graph[0].author', message: 'recommended 2' },
    ];

    const el = await fixture<ValidationPanelElement>(
      html`<schemeweaver-validation-panel .issues=${issues}></schemeweaver-validation-panel>`,
    );

    const rows = Array.from(el.shadowRoot!.querySelectorAll<HTMLLIElement>('.issue'));
    expect(rows.length).to.equal(4);

    // First row should be critical, then the two warnings, then the info row.
    expect(rows[0].dataset.severity).to.equal('critical');
    expect(rows[1].dataset.severity).to.equal('warning');
    expect(rows[2].dataset.severity).to.equal('warning');
    expect(rows[3].dataset.severity).to.equal('info');
  });

  it('applies the correct uui-tag colour per severity', async () => {
    const issues: ValidationIssue[] = [
      { severity: 'critical', schemaType: 'Article', path: '$', message: 'c' },
      { severity: 'warning', schemaType: 'Article', path: '$', message: 'w' },
      { severity: 'info', schemaType: 'Article', path: '$', message: 'i' },
    ];

    const el = await fixture<ValidationPanelElement>(
      html`<schemeweaver-validation-panel .issues=${issues}></schemeweaver-validation-panel>`,
    );

    const rows = el.shadowRoot!.querySelectorAll('.issue');
    const critTag = rows[0].querySelector('uui-tag.severity-tag');
    const warnTag = rows[1].querySelector('uui-tag.severity-tag');
    const infoTag = rows[2].querySelector('uui-tag.severity-tag');

    expect(critTag!.getAttribute('color')).to.equal('danger');
    expect(warnTag!.getAttribute('color')).to.equal('warning');
    // Info maps to empty colour → default neutral tag.
    expect(infoTag!.getAttribute('color') ?? '').to.equal('');
  });

  it('renders the field path, schema-type chip and message verbatim', async () => {
    const issues: ValidationIssue[] = [
      {
        severity: 'critical',
        schemaType: 'Article',
        path: '@graph[2].headline',
        message: 'Missing `headline` — Google requires it for Article types.',
      },
    ];

    const el = await fixture<ValidationPanelElement>(
      html`<schemeweaver-validation-panel .issues=${issues}></schemeweaver-validation-panel>`,
    );

    const row = el.shadowRoot!.querySelector('.issue')!;
    const chip = row.querySelector('.schema-type-chip')!;
    const path = row.querySelector('.field-path')!;
    const message = row.querySelector('.message')!;

    expect(chip.textContent!.trim()).to.equal('Article');
    expect(path.textContent!.trim()).to.equal('@graph[2].headline');
    expect(message.textContent!.trim()).to.equal(
      'Missing `headline` — Google requires it for Article types.',
    );
    // The field path is rendered in a <code> element — this is load-bearing
    // styling because monospace makes JSON paths legible.
    expect(path.tagName.toLowerCase()).to.equal('code');
  });

  it('renders a summary chip count for each non-zero severity bucket', async () => {
    const issues: ValidationIssue[] = [
      { severity: 'critical', schemaType: 'Article', path: '$', message: 'a' },
      { severity: 'critical', schemaType: 'Article', path: '$', message: 'b' },
      { severity: 'warning', schemaType: 'Article', path: '$', message: 'c' },
    ];

    const el = await fixture<ValidationPanelElement>(
      html`<schemeweaver-validation-panel .issues=${issues}></schemeweaver-validation-panel>`,
    );

    const summaryTags = el.shadowRoot!.querySelectorAll('.summary uui-tag');
    // 2 critical + 1 warning = 2 summary tags (info bucket is empty).
    expect(summaryTags.length).to.equal(2);

    const critical = summaryTags[0];
    expect(critical.getAttribute('color')).to.equal('danger');
    expect(critical.textContent!.trim()).to.contain('2');

    const warning = summaryTags[1];
    expect(warning.getAttribute('color')).to.equal('warning');
    expect(warning.textContent!.trim()).to.contain('1');
  });

  it('falls back to the info icon for info-severity rows', async () => {
    const issues: ValidationIssue[] = [
      { severity: 'info', schemaType: '(no-mapping)', path: '$', message: 'FYI' },
    ];

    const el = await fixture<ValidationPanelElement>(
      html`<schemeweaver-validation-panel .issues=${issues}></schemeweaver-validation-panel>`,
    );

    const icon = el.shadowRoot!.querySelector('.issue uui-icon')!;
    expect(icon.getAttribute('name')).to.equal('icon-info');
  });
});
