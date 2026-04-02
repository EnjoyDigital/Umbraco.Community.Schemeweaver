import { css, html, customElement, state, nothing, repeat } from '@umbraco-cms/backoffice/external/lit';
import { UmbModalBaseElement } from '@umbraco-cms/backoffice/modal';
import type { SourceOriginPickerModalData, SourceOriginPickerModalValue } from './source-origin-picker-modal.token.js';

import { BLOCK_EDITOR_ALIASES } from '../constants.js';

interface OriginOption {
  sourceType: string;
  icon: string;
  labelKey: string;
  descriptionKey: string;
}

@customElement('schemeweaver-source-origin-picker-modal')
export class SourceOriginPickerModalElement extends UmbModalBaseElement<SourceOriginPickerModalData, SourceOriginPickerModalValue> {
  @state()
  private _view: 'main' | 'related' = 'main';

  private get _mainOptions(): OriginOption[] {
    const editorAlias = this.data?.editorAlias ?? '';
    const isComplexType = this.data?.isComplexType ?? false;
    const restrictToSimple = this.data?.restrictToSimpleSources ?? false;

    const options: OriginOption[] = [
      {
        sourceType: 'property',
        icon: 'icon-document',
        labelKey: 'schemeWeaver_sourceCurrentNode',
        descriptionKey: 'schemeWeaver_originCurrentNodeDescription',
      },
      {
        sourceType: 'static',
        icon: 'icon-edit',
        labelKey: 'schemeWeaver_sourceStaticValue',
        descriptionKey: 'schemeWeaver_originStaticValueDescription',
      },
    ];

    if (!restrictToSimple) {
      options.push({
        sourceType: '_related',
        icon: 'icon-link',
        labelKey: 'schemeWeaver_originRelatedContent',
        descriptionKey: 'schemeWeaver_originRelatedContentDescription',
      });
    }

    if (!restrictToSimple && (BLOCK_EDITOR_ALIASES.includes(editorAlias) || isComplexType)) {
      options.push({
        sourceType: 'blockContent',
        icon: 'icon-grid',
        labelKey: 'schemeWeaver_sourceBlockContent',
        descriptionKey: 'schemeWeaver_originBlockContentDescription',
      });
    }

    if (isComplexType) {
      options.push({
        sourceType: 'complexType',
        icon: 'icon-brackets',
        labelKey: 'schemeWeaver_sourceComplexType',
        descriptionKey: 'schemeWeaver_originComplexTypeDescription',
      });
    }

    return options;
  }

  private _relatedOptions: OriginOption[] = [
    {
      sourceType: 'parent',
      icon: 'icon-arrow-up',
      labelKey: 'schemeWeaver_sourceParentNode',
      descriptionKey: 'schemeWeaver_originParentDescription',
    },
    {
      sourceType: 'ancestor',
      icon: 'icon-hierarchy',
      labelKey: 'schemeWeaver_sourceAncestorNode',
      descriptionKey: 'schemeWeaver_originAncestorDescription',
    },
    {
      sourceType: 'sibling',
      icon: 'icon-split-alt',
      labelKey: 'schemeWeaver_sourceSiblingNode',
      descriptionKey: 'schemeWeaver_originSiblingDescription',
    },
  ];

  #choose(option: OriginOption) {
    if (option.sourceType === '_related') {
      this._view = 'related';
      return;
    }
    this.#submit(option.sourceType);
  }

  #submit(sourceType: string) {
    this.modalContext?.setValue({ sourceType });
    this.modalContext?.submit();
  }

  #close() {
    this.modalContext?.reject();
  }

  #goBack() {
    this._view = 'main';
  }

  override render() {
    return html`
      <umb-body-layout headline=${this.localize.term('schemeWeaver_pickSourceOrigin')}>
        <div id="main">
          ${this._view === 'main' ? this._renderMainView() : this._renderRelatedView()}
        </div>
        <div slot="actions">
          ${this._view === 'related'
            ? html`<uui-button look="default" label=${this.localize.term('schemeWeaver_back')} @click=${this.#goBack}>
                <uui-icon name="icon-arrow-left"></uui-icon>
                ${this.localize.term('schemeWeaver_back')}
              </uui-button>`
            : nothing}
          <uui-button look="default" label=${this.localize.term('general_close')} @click=${this.#close}></uui-button>
        </div>
      </umb-body-layout>
    `;
  }

  private _renderMainView() {
    return html`
      <uui-box>
        <uui-ref-list>
          ${repeat(
            this._mainOptions,
            (option) => option.sourceType,
            (option) => html`
              <umb-ref-item
                name=${this.localize.term(option.labelKey)}
                detail=${this.localize.term(option.descriptionKey)}
                icon=${option.icon}
                @open=${() => this.#choose(option)}
              ></umb-ref-item>
            `,
          )}
        </uui-ref-list>
      </uui-box>
    `;
  }

  private _renderRelatedView() {
    return html`
      <uui-box headline=${this.localize.term('schemeWeaver_originRelatedContent')}>
        <uui-ref-list>
          ${repeat(
            this._relatedOptions,
            (option) => option.sourceType,
            (option) => html`
              <umb-ref-item
                name=${this.localize.term(option.labelKey)}
                detail=${this.localize.term(option.descriptionKey)}
                icon=${option.icon}
                @open=${() => this.#choose(option)}
              ></umb-ref-item>
            `,
          )}
        </uui-ref-list>
      </uui-box>
    `;
  }

  static override styles = [
    css`
      :host {
        display: block;
      }
    `,
  ];
}

export default SourceOriginPickerModalElement;

declare global {
  interface HTMLElementTagNameMap {
    'schemeweaver-source-origin-picker-modal': SourceOriginPickerModalElement;
  }
}
