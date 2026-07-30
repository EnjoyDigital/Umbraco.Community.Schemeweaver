import { css, html, customElement, state, nothing } from '@umbraco-cms/backoffice/external/lit';
import { UmbTextStyles } from '@umbraco-cms/backoffice/style';
import { UmbModalBaseElement } from '@umbraco-cms/backoffice/modal';
import { UMB_NOTIFICATION_CONTEXT } from '@umbraco-cms/backoffice/notification';
import { UMB_MODAL_MANAGER_CONTEXT } from '@umbraco-cms/backoffice/modal';
import type { PropertyMappingRow } from '../components/property-mapping-table.element.js';
import '../components/property-mapping-table.element.js';
import { SchemeWeaverRepository } from '../repository/schemeweaver.repository.js';
import { SCHEMEWEAVER_NESTED_MAPPING_MODAL } from './nested-mapping-modal.token.js';
import type { NestedMappingModalValue, NestedMappingModalSiblingClaim } from './nested-mapping-modal.token.js';
import { parseResolverConfig, legacyConfigToRoutes } from '../components/block-route-model.js';
import { SCHEMEWEAVER_COMPLEX_TYPE_MAPPING_MODAL } from './complex-type-mapping-modal.token.js';
import { SCHEMEWEAVER_SOURCE_ORIGIN_PICKER_MODAL } from './source-origin-picker-modal.token.js';
import { mergeAutoMapSuggestions, applySourceTypeChange, rowsToPropertyMappingDtos } from '../utils/mapping-converters.js';

import type { SchemaPropertyInfo } from '../api/types.js';
import { SourceType } from '../constants/source-type.js';
import type { PropertyMappingModalData, PropertyMappingModalValue } from './property-mapping-modal.token.js';

@customElement('schemeweaver-property-mapping-modal')
export class PropertyMappingModalElement extends UmbModalBaseElement<PropertyMappingModalData, PropertyMappingModalValue> {
  // Own repository instance — no context-consumption timing dependency.
  #repository = new SchemeWeaverRepository(this);
  #notificationContext?: typeof UMB_NOTIFICATION_CONTEXT.TYPE;
  #modalManagerContext?: typeof UMB_MODAL_MANAGER_CONTEXT.TYPE;

  @state()
  private _loading = true;

  @state()
  private _saving = false;

  @state()
  private _mappings: PropertyMappingRow[] = [];

  @state()
  private _availableProperties: string[] = [];

  @state()
  private _allSchemaProperties: SchemaPropertyInfo[] = [];

  @state()
  private _aiAvailable = false;

  @state()
  private _aiChecking = true;

  @state()
  private _aiLoading = false;

  constructor() {
    super();
    this.consumeContext(UMB_NOTIFICATION_CONTEXT, (context) => {
      this.#notificationContext = context;
    });
    this.consumeContext(UMB_MODAL_MANAGER_CONTEXT, (context) => {
      this.#modalManagerContext = context;
    });
  }

  async connectedCallback() {
    super.connectedCallback();
    await this._initialise();
    this._checkAIStatus();
  }

  private async _checkAIStatus() {
    this._aiChecking = true;
    try {
      const status = await this.#repository.requestAIStatus();
      this._aiAvailable = status?.available === true;
    } catch {
      this._aiAvailable = false;
    } finally {
      this._aiChecking = false;
    }
  }

  private async _handleAIAutoMap() {
    this._aiLoading = true;
    try {
      const suggestions = await this.#repository.requestAIAutoMap(
        this.data?.contentTypeAlias || '',
        this.data?.schemaType || '',
      );
      if (suggestions && Array.isArray(suggestions)) {
        this._mappings = mergeAutoMapSuggestions(this._mappings, suggestions);
      }
    } catch {
      this.#notificationContext?.peek('danger', {
        data: { message: this.localize.term('schemeWeaver_aiAutoMapFailed') },
      });
    } finally {
      this._aiLoading = false;
    }
  }

  private async _initialise() {
    this._loading = true;
    try {
      // Auto-map returns a flat array of PropertyMappingSuggestion
      const suggestions = await this.#repository.requestAutoMap(
        this.data?.contentTypeAlias || '',
        this.data?.schemaType || ''
      );

      if (suggestions && Array.isArray(suggestions)) {
        this._mappings = mergeAutoMapSuggestions(this._mappings, suggestions);
      }

      const [props, schemaProps] = await Promise.all([
        this.#repository.requestContentTypeProperties(this.data?.contentTypeAlias || ''),
        this.#repository.requestSchemaTypeProperties(this.data?.schemaType || ''),
      ]);
      if (props) {
        this._availableProperties = props.map((p) => p.alias);
      }
      if (schemaProps) {
        this._allSchemaProperties = schemaProps;
        // Enrich rows with schema property metadata
        this._mappings = this._mappings.map(row => {
          const sp = schemaProps.find(
            (s: SchemaPropertyInfo) => s.name.toLowerCase() === row.schemaPropertyName.toLowerCase()
          );
          if (sp) {
            return {
              ...row,
              schemaPropertyType: sp.propertyType || row.schemaPropertyType,
              acceptedTypes: sp.acceptedTypes || row.acceptedTypes,
              isComplexType: sp.isComplexType || row.isComplexType,
            };
          }
          return row;
        });
      }
    } catch (error) {
      this.#notificationContext?.peek('danger', {
        data: {
          message: error instanceof Error ? error.message : this.localize.term('schemeWeaver_failedToLoadMappingData'),
        },
      });
    } finally {
      this._loading = false;
    }
  }

  private _handleMappingsChanged(e: CustomEvent) {
    this._mappings = e.detail.mappings;
  }

  private async _handleConfigureComplexTypeMapping(e: CustomEvent) {
    const { index, schemaPropertyName, acceptedTypes, selectedSubType, resolverConfig } = e.detail;
    if (!this.#modalManagerContext) return;

    const modalHandler = this.#modalManagerContext.open(this, SCHEMEWEAVER_COMPLEX_TYPE_MAPPING_MODAL, {
      data: {
        schemaPropertyName,
        acceptedTypes: acceptedTypes || [],
        selectedSubType: selectedSubType || '',
        contentTypeAlias: this.data?.contentTypeAlias || '',
        availableProperties: this._availableProperties,
        existingConfig: resolverConfig,
      },
    });

    try {
      const result = await modalHandler.onSubmit();
      if (result?.resolverConfig) {
        const updated = [...this._mappings];
        updated[index] = {
          ...updated[index],
          resolverConfig: result.resolverConfig,
          selectedSubType: result.selectedSubType,
          nestedSchemaTypeName: result.selectedSubType,
        };
        this._mappings = updated;
      }
    } catch {
      // Modal was rejected / closed
    }
  }

  private async _handlePickSourceOrigin(e: CustomEvent) {
    const { index, editorAlias, isComplexType, currentSourceType } = e.detail;
    if (!this.#modalManagerContext) return;

    const result = await this.#modalManagerContext
      .open(this, SCHEMEWEAVER_SOURCE_ORIGIN_PICKER_MODAL, {
        data: { editorAlias, isComplexType, currentSourceType },
      })
      .onSubmit()
      .catch(() => null);

    if (!result?.sourceType) return;

    const updated = [...this._mappings];
    updated[index] = applySourceTypeChange(updated[index], result.sourceType);
    this._mappings = updated;
  }

  private async _handleResolveDocumentType(e: CustomEvent) {
    const { index, documentTypeUnique } = e.detail;
    if (!documentTypeUnique) return;

    const contentTypes = await this.#repository.requestContentTypes();
    const match = contentTypes?.find((ct) => ct.key === documentTypeUnique);
    if (!match) return;

    const props = await this.#repository.requestContentTypeProperties(match.alias);
    const propertyAliases = props?.map((p) => p.alias) || [];

    const updated = [...this._mappings];
    updated[index] = {
      ...updated[index],
      sourceContentTypeAlias: match.alias,
      sourceContentTypeProperties: propertyAliases,
      contentTypePropertyAlias: '',
    };
    this._mappings = updated;
  }

  /** Picker drill-down: browse a document type to list the picked item's properties. */
  private async _handleResolvePickedDocumentType(e: CustomEvent) {
    const { index, documentTypeUnique } = e.detail;
    if (!documentTypeUnique) return;

    const contentTypes = await this.#repository.requestContentTypes();
    const match = contentTypes?.find((ct) => ct.key === documentTypeUnique);
    if (!match) return;

    const props = await this.#repository.requestContentTypeProperties(match.alias);
    const propertyAliases = props?.map((p) => p.alias) || [];

    const updated = [...this._mappings];
    updated[index] = {
      ...updated[index],
      pickedContentTypeAlias: match.alias,
      pickedContentTypeUnique: match.key,
      pickedContentTypeProperties: propertyAliases,
      pickedPropertyAlias: undefined,
      // The old drilled alias belongs to the previous type — clearing only the
      // row field would leave the stale drill config to be saved verbatim.
      resolverConfig: null,
    };
    this._mappings = updated;
  }

  private async _handleConfigureNestedMapping(e: CustomEvent) {
    const detail = e.detail;
    const index = detail.index as number;
    const mapping = this._mappings[index];

    // The panel is scoped to THIS row: it maps the block-list property's element
    // types INTO this row's schema property. It needs a chosen property alias.
    if (!mapping || !mapping.contentTypePropertyAlias) {
      this.#notificationContext?.peek('warning', {
        data: {
          message: this.localize.term('schemeWeaver_pleaseSelectBlockContentProperty'),
        },
      });
      return;
    }

    const blockListProp = mapping.contentTypePropertyAlias;

    const modalHandler = this.#modalManagerContext?.open(this, SCHEMEWEAVER_NESTED_MAPPING_MODAL, {
      data: {
        contentTypeAlias: this.data?.contentTypeAlias || '',
        contentTypePropertyAlias: blockListProp,
        schemaPropertyName: mapping.schemaPropertyName,
        schemaPropertyType: mapping.schemaPropertyType || undefined,
        acceptedTypes: mapping.acceptedTypes,
        existingConfig: mapping.resolverConfig ?? null,
        nestedSchemaTypeName: mapping.nestedSchemaTypeName || null,
        siblingClaims: this._computeSiblingClaims(index, blockListProp),
      },
    });

    if (!modalHandler) return;

    try {
      const result = await modalHandler.onSubmit();
      if (result) {
        this._applyNestedMappingResult(index, mapping.editorAlias, result);
      }
    } catch {
      // Modal was rejected / closed — do nothing
    }
  }

  /**
   * Blocks already routed by OTHER blockContent rows on the same block-list
   * property — read-only context for the panel. Legacy-wildcard siblings (flat
   * `nestedMappings` with no routes) are skipped: they apply to every block, so
   * per-block attribution would be misleading.
   */
  private _computeSiblingClaims(openedIndex: number, blockListProp: string): NestedMappingModalSiblingClaim[] {
    const claims: NestedMappingModalSiblingClaim[] = [];
    this._mappings.forEach((row, i) => {
      if (i === openedIndex) return;
      if (row.sourceType !== SourceType.BlockContent || row.contentTypePropertyAlias !== blockListProp) return;
      const config = parseResolverConfig(row.resolverConfig);
      const blockAliases = (config?.routes ?? [])
        .map((r) => r.blockAlias)
        .filter((alias): alias is string => !!alias);
      if (blockAliases.length === 0) return;
      claims.push({ schemaPropertyName: row.schemaPropertyName, blockAliases });
    });
    return claims;
  }

  /**
   * Merge the panel's row-scoped result back into the table. Patches ONLY the
   * opened row's `resolverConfig`; a verbatim-unchanged config touches NOTHING.
   * Explicit fan-out entries merge into an existing sibling row or append a new
   * one. No row is ever deleted or re-keyed.
   */
  private _applyNestedMappingResult(index: number, editorAlias: string, value: NestedMappingModalValue) {
    const opened = this._mappings[index];
    const blockListProp = opened.contentTypePropertyAlias;
    const rows = [...this._mappings];

    const returned = value.resolverConfig ?? null;
    if (returned !== (opened.resolverConfig ?? null)) {
      const patched: PropertyMappingRow = { ...opened, resolverConfig: returned };
      // A config that now carries routes has upgraded past the legacy
      // mapping-level nested type — clear it so the routes alone drive placement.
      if (parseResolverConfig(returned)?.routes) patched.nestedSchemaTypeName = '';
      rows[index] = patched;
    }

    for (const target of value.additionalTargets ?? []) {
      // Never re-route the opened row via fan-out — its config is `value.resolverConfig`.
      if (target.schemaPropertyName.toLowerCase() === opened.schemaPropertyName.toLowerCase()) continue;
      const siblingIndex = rows.findIndex(
        (row, i) =>
          i !== index &&
          row.sourceType === SourceType.BlockContent &&
          row.contentTypePropertyAlias === blockListProp &&
          row.schemaPropertyName.toLowerCase() === target.schemaPropertyName.toLowerCase(),
      );

      if (siblingIndex >= 0) {
        // Merge routes: block aliases present in the new set replace, others kept.
        // A sibling still on the LEGACY flat shape is expanded to equivalent
        // explicit routes first — merging routes on top of untouched
        // nestedMappings would silently shadow the whole flat list (the
        // renderer prefers routes).
        const sibling = rows[siblingIndex];
        const siblingConfig = parseResolverConfig(sibling.resolverConfig) ?? {};
        const baseRoutes =
          siblingConfig.routes ??
          (siblingConfig.nestedMappings?.length
            ? legacyConfigToRoutes(siblingConfig.nestedMappings, sibling.nestedSchemaTypeName)
            : sibling.nestedSchemaTypeName
              ? [{ blockAlias: '', nestedSchemaType: sibling.nestedSchemaTypeName, propertyMappings: [] }]
              : []);
        const newRoutes = parseResolverConfig(target.resolverConfig)?.routes ?? [];
        const newAliases = new Set(newRoutes.map((r) => (r.blockAlias ?? '').toLowerCase()));
        const keptRoutes = baseRoutes.filter((r) => !newAliases.has((r.blockAlias ?? '').toLowerCase()));
        const { nestedMappings: _legacy, ...rootExtras } = siblingConfig;
        rows[siblingIndex] = {
          ...sibling,
          resolverConfig: JSON.stringify({ ...rootExtras, routes: [...keptRoutes, ...newRoutes] }),
          // Routes now drive placement — the legacy mapping-level type is upgraded away.
          nestedSchemaTypeName: '',
        };
      } else {
        const sp = this._allSchemaProperties.find(
          (s) => s.name.toLowerCase() === target.schemaPropertyName.toLowerCase(),
        );
        rows.push({
          schemaPropertyName: target.schemaPropertyName,
          schemaPropertyType: sp?.propertyType || '',
          sourceType: SourceType.BlockContent,
          contentTypePropertyAlias: blockListProp,
          sourceContentTypeAlias: '',
          staticValue: '',
          confidence: null,
          editorAlias,
          nestedSchemaTypeName: '',
          resolverConfig: target.resolverConfig,
          acceptedTypes: sp?.acceptedTypes || [],
          isComplexType: sp?.isComplexType || false,
          expanded: false,
          subMappings: [],
          selectedSubType: '',
          sourceContentTypeProperties: [],
        });
      }
    }

    this._mappings = rows;
  }

  private async _handleSave() {
    this._saving = true;

    try {
      await this.#repository.saveMapping({
        contentTypeAlias: this.data?.contentTypeAlias || '',
        contentTypeKey: this.data?.contentTypeKey ?? '',
        schemaTypeName: this.data?.schemaType || '',
        isEnabled: true,
        isInherited: false,
        propertyMappings: rowsToPropertyMappingDtos(this._mappings),
      });

      this.modalContext?.setValue({ saved: true });
      this.modalContext?.submit();
    } catch (error) {
      this.#notificationContext?.peek('danger', {
        data: {
          message: error instanceof Error ? error.message : this.localize.term('schemeWeaver_failedToSave'),
        },
      });
    } finally {
      this._saving = false;
    }
  }

  private _handleClose() {
    this.modalContext?.reject();
  }

  render() {
    return html`
      <umb-body-layout headline="${this.localize.term('schemeWeaver_mapProperties')}: ${this.data?.schemaType ?? ''}">
        ${this._loading ? this._renderLoading() : this._renderContent()}

        <div slot="actions">
          <uui-button
            look="secondary"
            data-mark="schemeweaver:mapping-cancel"
            @click=${this._handleClose}
            label=${this.localize.term('schemeWeaver_cancel')}
          >
            ${this.localize.term('schemeWeaver_cancel')}
          </uui-button>
          <uui-button
            look="primary"
            data-mark="schemeweaver:mapping-save"
            @click=${this._handleSave}
            ?disabled=${this._saving || this._loading}
            .state=${this._saving ? 'waiting' : undefined}
            label=${this.localize.term('schemeWeaver_save')}
          >
            ${this._saving ? this.localize.term('schemeWeaver_saving') : this.localize.term('schemeWeaver_save')}
          </uui-button>
        </div>
      </umb-body-layout>
    `;
  }

  private _renderLoading() {
    return html`
      <div id="loading">
        <uui-loader></uui-loader>
      </div>
    `;
  }

  private _renderContent() {
    return html`
      <uui-box headline=${this.localize.term('schemeWeaver_propertyMappings')}>
        ${this._aiAvailable && !this._aiChecking
          ? html`
              <uui-button
                slot="header-actions"
                look="outline"
                color="positive"
                compact
                @click=${this._handleAIAutoMap}
                ?disabled=${this._aiLoading}
                .state=${this._aiLoading ? 'waiting' : undefined}
                label=${this.localize.term('schemeWeaver_aiAutoMap')}
              >
                <uui-icon name="icon-wand"></uui-icon>
                ${this._aiLoading ? this.localize.term('schemeWeaver_aiAnalysing') : this.localize.term('schemeWeaver_aiAutoMap')}
              </uui-button>
            `
          : nothing}

        <p id="mapping-context" class="uui-text">
          ${this.data?.schemaType} ${this.localize.term('schemeWeaver_mappedTo')} ${this.data?.contentTypeAlias}
        </p>

        <schemeweaver-property-mapping-table
          .mappings=${this._mappings}
          .availableProperties=${this._availableProperties}
          .allSchemaProperties=${this._allSchemaProperties}
          @mappings-changed=${this._handleMappingsChanged}
          @configure-nested-mapping=${this._handleConfigureNestedMapping}
          @configure-complex-type-mapping=${this._handleConfigureComplexTypeMapping}
          @pick-source-origin=${this._handlePickSourceOrigin}
          @resolve-document-type=${this._handleResolveDocumentType}
          @resolve-picked-document-type=${this._handleResolvePickedDocumentType}
        ></schemeweaver-property-mapping-table>
      </uui-box>
    `;
  }

  static styles = [
    UmbTextStyles,
    css`
      :host {
        display: block;
        height: 100%;
      }

      #loading {
        display: flex;
        justify-content: center;
        align-items: center;
        height: 100%;
        opacity: 0;
        animation: fadeIn 240ms 240ms forwards;
      }

      @keyframes fadeIn {
        100% {
          opacity: 1;
        }
      }

      #mapping-context {
        margin: 0 0 var(--uui-size-space-4);
        color: var(--uui-color-text-alt);
      }
    `,
  ];
}

export default PropertyMappingModalElement;

declare global {
  interface HTMLElementTagNameMap {
    'schemeweaver-property-mapping-modal': PropertyMappingModalElement;
  }
}
