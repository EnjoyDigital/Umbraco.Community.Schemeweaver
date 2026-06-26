import { css, html, customElement, state, nothing, repeat } from '@umbraco-cms/backoffice/external/lit';
import { UmbModalBaseElement } from '@umbraco-cms/backoffice/modal';
import { UMB_NOTIFICATION_CONTEXT } from '@umbraco-cms/backoffice/notification';
import { SchemeWeaverRepository } from '../repository/schemeweaver.repository.js';
import type {
  SchemaPropertyInfo,
  RankedSchemaPropertyInfo,
  BlockElementTypeInfo,
  BlockElementPropertyInfo,
  BlockMappingSuggestion,
  BlockRouteSuggestion,
  BlockRoutePropertyMapping,
  RoutedResolverConfig,
} from '../api/types.js';

import type {
  NestedMappingModalData,
  NestedMappingModalValue,
  NestedMappingModalTargetMapping,
} from './nested-mapping-modal.token.js';

/** A single nested-property mapping row inside one block's expandable table. */
interface RoutePropEntry {
  schemaProperty: string;
  schemaPropertyType: string;
  contentProperty: string;
  wrapInType: string;
  wrapInProperty: string;
  isComplexType: boolean;
}

/** One block element type as a flat-panel row. */
interface BlockRow {
  alias: string;
  name: string;
  /** Block element property aliases (drive the value dropdown). */
  properties: string[];
  propertyInfos: BlockElementPropertyInfo[];
  /** false === SKIP / not mapped (opt-in). */
  mapped: boolean;
  nestedSchemaType: string;
  targetProperty: string;
  propertyMappings: RoutePropEntry[];
  /** Total nested schema properties for the chosen type (denominator of the badge). */
  totalSchemaProps: number;
  expanded: boolean;
  confidence: number | null;
}

/** Target page properties offered for routing block content. */
const DEFAULT_TARGET_PROPERTIES = ['mainEntity', 'hasPart', 'about', 'mentions'];

@customElement('schemeweaver-nested-mapping-modal')
export class NestedMappingModalElement extends UmbModalBaseElement<NestedMappingModalData, NestedMappingModalValue> {
  // Own repository instance — sidesteps context-consumption timing (see the
  // long-standing empty-step-2 saga); the repository is stateless over HTTP.
  #repository = new SchemeWeaverRepository(this);
  #notificationContext?: typeof UMB_NOTIFICATION_CONTEXT.TYPE;

  @state()
  private _loading = true;

  @state()
  private _autoMapping = false;

  @state()
  private _blockRows: BlockRow[] = [];

  /** Cache of ranked schema-type properties keyed by type name. */
  private _typePropsCache: Record<string, SchemaPropertyInfo[]> = {};

  constructor() {
    super();
    this.consumeContext(UMB_NOTIFICATION_CONTEXT, (context) => {
      this.#notificationContext = context;
    });
  }

  async connectedCallback() {
    super.connectedCallback();
    await this._initialise();
  }

  // ── Initialisation ───────────────────────────────────────────────────────

  private async _initialise() {
    this._loading = true;
    try {
      const contentTypeAlias = this.data?.contentTypeAlias || '';
      const propertyAlias = this.data?.contentTypePropertyAlias || '';

      const [blockTypes, suggestions] = await Promise.all([
        propertyAlias
          ? this.#repository.requestBlockElementTypes(contentTypeAlias, propertyAlias)
          : Promise.resolve(undefined),
        propertyAlias
          ? this.#repository.requestBlockSuggestions(contentTypeAlias, propertyAlias)
          : Promise.resolve(undefined),
      ]);

      const blocks = blockTypes ?? [];
      const routeByBlock = this._indexSuggestionRoutes(suggestions ?? []);
      const existingByBlock = this._indexExistingRoutes();

      // One row per block element type, seeded from existing config first
      // (re-editing wins), then the heuristic suggestion, else SKIP.
      this._blockRows = blocks.map((bt) => this._buildRow(bt, routeByBlock.get(bt.alias), existingByBlock.get(bt.alias)));

      // Hydrate full editable property tables for every mapped row.
      await Promise.all(this._blockRows.map((_, i) => this._hydrateRow(i)));
      this._blockRows = [...this._blockRows];
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

  /** blockAlias → { target, route } from the block-suggest response. */
  private _indexSuggestionRoutes(
    suggestions: BlockMappingSuggestion[],
  ): Map<string, { target: string; route: BlockRouteSuggestion; confidence: number }> {
    const map = new Map<string, { target: string; route: BlockRouteSuggestion; confidence: number }>();
    for (const s of suggestions) {
      for (const route of s.routes) {
        map.set(route.blockAlias, { target: s.schemaProperty, route, confidence: route.confidence });
      }
    }
    return map;
  }

  /** blockAlias → { target, nestedSchemaType, propertyMappings } parsed from existing saved config. */
  private _indexExistingRoutes(): Map<string, { target: string; nestedSchemaType: string; propertyMappings: BlockRoutePropertyMapping[] }> {
    const map = new Map<string, { target: string; nestedSchemaType: string; propertyMappings: BlockRoutePropertyMapping[] }>();
    for (const existing of this.data?.existingMappings ?? []) {
      const target = existing.schemaPropertyName;
      if (!existing.resolverConfig) continue;
      let config: Record<string, unknown>;
      try {
        config = JSON.parse(existing.resolverConfig);
      } catch {
        continue;
      }

      const routes = config.routes;
      if (Array.isArray(routes)) {
        // NEW routed shape
        for (const route of routes as Array<Record<string, unknown>>) {
          const blockAlias = (route.blockAlias as string) || '';
          map.set(blockAlias, {
            target,
            nestedSchemaType: (route.nestedSchemaType as string) || existing.nestedSchemaTypeName || '',
            propertyMappings: (route.propertyMappings as BlockRoutePropertyMapping[]) || [],
          });
        }
      } else if (Array.isArray(config.nestedMappings)) {
        // LEGACY flat shape → one implicit route keyed by its blockAlias (or wildcard).
        const flat = config.nestedMappings as Array<Record<string, unknown>>;
        const blockAlias = (flat[0]?.blockAlias as string) || '';
        map.set(blockAlias, {
          target,
          nestedSchemaType: existing.nestedSchemaTypeName || '',
          propertyMappings: flat.map((m) => ({
            schemaProperty: m.schemaProperty as string,
            contentProperty: m.contentProperty as string,
            wrapInType: (m.wrapInType as string) ?? null,
            wrapInProperty: (m.wrapInProperty as string) ?? null,
          })),
        });
      }
    }
    return map;
  }

  private _buildRow(
    bt: BlockElementTypeInfo,
    suggestion?: { target: string; route: BlockRouteSuggestion; confidence: number },
    existing?: { target: string; nestedSchemaType: string; propertyMappings: BlockRoutePropertyMapping[] },
  ): BlockRow {
    const properties = bt.propertyInfos?.length ? bt.propertyInfos.map((p) => p.alias) : bt.properties;
    const source = existing ?? (suggestion
      ? { target: suggestion.target, nestedSchemaType: suggestion.route.nestedSchemaType, propertyMappings: suggestion.route.propertyMappings }
      : undefined);

    return {
      alias: bt.alias,
      name: bt.name || bt.alias,
      properties,
      propertyInfos: bt.propertyInfos ?? properties.map((alias) => ({ alias, name: alias, editorAlias: '' })),
      mapped: !!source,
      nestedSchemaType: source?.nestedSchemaType ?? '',
      targetProperty: source?.target ?? '',
      // Seeded mappings; replaced with a full editable table during hydration.
      propertyMappings: (source?.propertyMappings ?? []).map((m) => ({
        schemaProperty: m.schemaProperty,
        schemaPropertyType: '',
        contentProperty: m.contentProperty || '',
        wrapInType: m.wrapInType || '',
        wrapInProperty: m.wrapInProperty || '',
        isComplexType: false,
      })),
      totalSchemaProps: source?.propertyMappings?.length ?? 0,
      expanded: false,
      confidence: suggestion?.confidence ?? null,
    };
  }

  /**
   * Fetch the chosen nested schema type's properties and align the row's
   * property table to them, preserving any already-chosen content properties.
   */
  private async _hydrateRow(index: number): Promise<void> {
    const row = this._blockRows[index];
    if (!row || !row.mapped || !row.nestedSchemaType) return;

    const seed = new Map(row.propertyMappings.map((m) => [m.schemaProperty.toLowerCase(), m]));
    const props = await this._getTypeProperties(row.nestedSchemaType);

    if (props.length === 0) {
      // Unknown nested type — keep whatever was seeded.
      this._blockRows[index] = { ...row, totalSchemaProps: row.propertyMappings.length };
      return;
    }

    const propertyMappings: RoutePropEntry[] = props.map((sp) => {
      const existing = seed.get(sp.name.toLowerCase());
      return {
        schemaProperty: sp.name,
        schemaPropertyType: sp.propertyType,
        contentProperty: existing?.contentProperty ?? '',
        wrapInType: existing?.wrapInType ?? '',
        wrapInProperty: existing?.wrapInProperty ?? '',
        isComplexType: sp.isComplexType,
      };
    });

    this._blockRows[index] = { ...row, propertyMappings, totalSchemaProps: props.length };
  }

  private async _getTypeProperties(typeName: string): Promise<RankedSchemaPropertyInfo[]> {
    if (!this._typePropsCache[typeName]) {
      const props = await this.#repository.requestSchemaTypeProperties(typeName, true);
      this._typePropsCache[typeName] = props || [];
    }
    return this._typePropsCache[typeName] as RankedSchemaPropertyInfo[];
  }

  // ── Auto-map ───────────────────────────────────────────────────────────────

  private async _handleAutoMapAll() {
    this._autoMapping = true;
    try {
      const suggestions = await this.#repository.requestBlockSuggestions(
        this.data?.contentTypeAlias || '',
        this.data?.contentTypePropertyAlias || '',
      );
      const routeByBlock = this._indexSuggestionRoutes(suggestions ?? []);

      this._blockRows = this._blockRows.map((row) => {
        const hit = routeByBlock.get(row.alias);
        if (!hit) return row; // no suggestion — leave as-is (e.g. SKIP blocks)
        return this._applyRouteToRow(row, hit.target, hit.route, hit.confidence);
      });

      await Promise.all(this._blockRows.map((_, i) => this._hydrateRow(i)));
      this._blockRows = [...this._blockRows];
    } finally {
      this._autoMapping = false;
    }
  }

  private async _handleAutoMapRow(index: number) {
    const row = this._blockRows[index];
    if (!row) return;
    const suggestions = await this.#repository.requestBlockSuggestions(
      this.data?.contentTypeAlias || '',
      this.data?.contentTypePropertyAlias || '',
    );
    const hit = this._indexSuggestionRoutes(suggestions ?? []).get(row.alias);
    if (!hit) {
      this.#notificationContext?.peek('warning', {
        data: { message: this.localize.term('schemeWeaver_blockNoSuggestion') },
      });
      return;
    }
    const updated = [...this._blockRows];
    updated[index] = this._applyRouteToRow(row, hit.target, hit.route, hit.confidence);
    this._blockRows = updated;
    await this._hydrateRow(index);
    this._blockRows = [...this._blockRows];
  }

  private _applyRouteToRow(row: BlockRow, target: string, route: BlockRouteSuggestion, confidence: number): BlockRow {
    return {
      ...row,
      mapped: true,
      nestedSchemaType: route.nestedSchemaType,
      targetProperty: target,
      confidence,
      propertyMappings: route.propertyMappings.map((m) => ({
        schemaProperty: m.schemaProperty,
        schemaPropertyType: '',
        contentProperty: m.contentProperty || '',
        wrapInType: m.wrapInType || '',
        wrapInProperty: m.wrapInProperty || '',
        isComplexType: false,
      })),
      totalSchemaProps: route.propertyMappings.length,
    };
  }

  // ── Row edits ──────────────────────────────────────────────────────────────

  private _mappedCount(row: BlockRow): number {
    return row.propertyMappings.filter((m) => m.contentProperty.trim() !== '').length;
  }

  private async _enableRow(index: number) {
    const updated = [...this._blockRows];
    updated[index] = { ...updated[index], mapped: true, targetProperty: updated[index].targetProperty || 'hasPart', expanded: true };
    this._blockRows = updated;
  }

  private _disableRow(index: number) {
    const updated = [...this._blockRows];
    updated[index] = { ...updated[index], mapped: false, expanded: false };
    this._blockRows = updated;
  }

  private _toggleExpand(index: number) {
    const updated = [...this._blockRows];
    updated[index] = { ...updated[index], expanded: !updated[index].expanded };
    this._blockRows = updated;
  }

  private async _handleSchemaTypeChange(index: number, value: string) {
    const updated = [...this._blockRows];
    updated[index] = { ...updated[index], nestedSchemaType: value };
    this._blockRows = updated;
    await this._hydrateRow(index);
    this._blockRows = [...this._blockRows];
  }

  private _handleTargetChange(index: number, value: string) {
    const updated = [...this._blockRows];
    updated[index] = { ...updated[index], targetProperty: value };
    this._blockRows = updated;
  }

  private _handleContentPropertyChange(rowIndex: number, propIndex: number, value: string) {
    const updated = [...this._blockRows];
    const row = { ...updated[rowIndex] };
    const mappings = [...row.propertyMappings];
    mappings[propIndex] = { ...mappings[propIndex], contentProperty: value };
    row.propertyMappings = mappings;
    updated[rowIndex] = row;
    this._blockRows = updated;
  }

  private _handleWrapInTypeChange(rowIndex: number, propIndex: number, value: string) {
    const updated = [...this._blockRows];
    const row = { ...updated[rowIndex] };
    const mappings = [...row.propertyMappings];
    mappings[propIndex] = { ...mappings[propIndex], wrapInType: value };
    row.propertyMappings = mappings;
    updated[rowIndex] = row;
    this._blockRows = updated;
  }

  // ── Save ─────────────────────────────────────────────────────────────────

  /** Serialise the mapped rows into one PropertyMappingDto-shaped target per group. */
  private _buildTargetMappings(): NestedMappingModalTargetMapping[] {
    const blockListProp = this.data?.contentTypePropertyAlias || '';
    const byTarget = new Map<string, RoutedResolverConfig>();

    for (const row of this._blockRows) {
      if (!row.mapped || !row.nestedSchemaType || !row.targetProperty) continue;
      const propertyMappings = row.propertyMappings
        .filter((m) => m.contentProperty.trim() !== '')
        .map((m) => ({
          schemaProperty: m.schemaProperty,
          contentProperty: m.contentProperty,
          wrapInType: m.wrapInType || null,
          wrapInProperty: m.wrapInProperty || null,
        }));

      const config = byTarget.get(row.targetProperty) ?? { routes: [] };
      config.routes.push({
        blockAlias: row.alias,
        nestedSchemaType: row.nestedSchemaType,
        propertyMappings,
      });
      byTarget.set(row.targetProperty, config);
    }

    return [...byTarget.entries()].map(([schemaPropertyName, config]) => ({
      schemaPropertyName,
      contentTypePropertyAlias: blockListProp,
      resolverConfig: JSON.stringify(config),
    }));
  }

  private _handleSave() {
    this.modalContext?.setValue({ mappings: this._buildTargetMappings() });
    this.modalContext?.submit();
  }

  private _handleClose() {
    this.modalContext?.reject();
  }

  // ── Render ─────────────────────────────────────────────────────────────────

  private get _targetOptions(): string[] {
    const fromRows = this._blockRows.map((r) => r.targetProperty).filter(Boolean);
    return Array.from(new Set([...DEFAULT_TARGET_PROPERTIES, ...fromRows]));
  }

  render() {
    return html`
      <umb-body-layout headline="${this.localize.term('schemeWeaver_blockMappings')}: ${this.data?.contentTypePropertyAlias ?? ''}">
        ${this._loading
          ? html`
              <div class="loading">
                <uui-loader-circle></uui-loader-circle>
                <p>${this.localize.term('schemeWeaver_loadingProperties')}</p>
              </div>
            `
          : this._renderPanel()}

        <div slot="actions">
          <uui-button look="secondary" @click=${this._handleClose} label=${this.localize.term('schemeWeaver_cancel')}>
            ${this.localize.term('schemeWeaver_cancel')}
          </uui-button>
          <uui-button look="primary" @click=${this._handleSave} label=${this.localize.term('schemeWeaver_save')}>
            ${this.localize.term('schemeWeaver_save')}
          </uui-button>
        </div>
      </umb-body-layout>
    `;
  }

  private _renderPanel() {
    if (this._blockRows.length === 0) {
      return html`
        <uui-box headline=${this.localize.term('schemeWeaver_blockMappings')}>
          <p class="no-block-types-hint">${this.localize.term('schemeWeaver_noBlockTypesHint')}</p>
          <p class="no-block-types-hint">${this.localize.term('schemeWeaver_noBlockTypesConfigureHint')}</p>
        </uui-box>
      `;
    }

    return html`
      <uui-box headline=${this.localize.term('schemeWeaver_blockMappings')}>
        <div class="panel-header">
          <p class="panel-description">${this.localize.term('schemeWeaver_blockMappingsDescription')}</p>
          <uui-button
            class="auto-map-all"
            look="secondary"
            ?disabled=${this._autoMapping}
            @click=${this._handleAutoMapAll}
            label=${this.localize.term('schemeWeaver_autoMapAll')}
          >
            <uui-icon name="icon-wand"></uui-icon>
            ${this._autoMapping ? this.localize.term('schemeWeaver_loadingEllipsis') : this.localize.term('schemeWeaver_autoMapAll')}
          </uui-button>
        </div>

        <div class="block-rows">
          ${repeat(this._blockRows, (r) => r.alias, (row, index) => this._renderBlockRow(row, index))}
        </div>
      </uui-box>
    `;
  }

  private _renderBlockRow(row: BlockRow, index: number) {
    if (!row.mapped) {
      return html`
        <div class="block-row unmapped">
          <div class="block-row-main">
            <div class="block-identity">
              <strong>${row.name}</strong>
              <small class="block-alias">${row.alias}</small>
            </div>
            <uui-tag look="secondary" class="not-mapped-badge">${this.localize.term('schemeWeaver_notMapped')}</uui-tag>
            <uui-button
              class="map-block-btn"
              look="secondary"
              compact
              @click=${() => this._enableRow(index)}
              label=${this.localize.term('schemeWeaver_mapThisBlock')}
            >
              ${this.localize.term('schemeWeaver_mapThisBlock')}
            </uui-button>
          </div>
        </div>
      `;
    }

    const mapped = this._mappedCount(row);
    return html`
      <div class="block-row mapped">
        <div class="block-row-main">
          <div class="block-identity">
            <strong>${row.name}</strong>
            <small class="block-alias">${row.alias}</small>
          </div>

          <uui-input
            class="schema-type-input"
            .value=${row.nestedSchemaType}
            placeholder=${this.localize.term('schemeWeaver_nestedSchemaType')}
            label=${this.localize.term('schemeWeaver_nestedSchemaType')}
            @change=${(e: Event) => this._handleSchemaTypeChange(index, (e.target as HTMLInputElement).value)}
          ></uui-input>

          <uui-select
            class="target-select"
            label=${this.localize.term('schemeWeaver_targetProperty')}
            .options=${this._targetOptions.map((t) => ({ name: t, value: t, selected: row.targetProperty === t }))}
            @change=${(e: Event) => this._handleTargetChange(index, (e.target as HTMLSelectElement).value)}
          ></uui-select>

          <uui-tag look="secondary" color="positive" class="mapped-badge">
            ${this.localize.term('schemeWeaver_mappedCount', mapped, row.totalSchemaProps)}
          </uui-tag>

          <uui-button
            compact
            look="secondary"
            class="row-auto-map"
            label=${this.localize.term('schemeWeaver_autoMapNested')}
            @click=${() => this._handleAutoMapRow(index)}
          >
            <uui-icon name="icon-wand"></uui-icon>
          </uui-button>

          <uui-button
            compact
            look="secondary"
            class="row-expand"
            label=${row.expanded ? this.localize.term('schemeWeaver_collapse') : this.localize.term('schemeWeaver_expand')}
            @click=${() => this._toggleExpand(index)}
          >
            <uui-icon name=${row.expanded ? 'icon-navigation-up' : 'icon-navigation-down'}></uui-icon>
          </uui-button>

          <uui-button
            compact
            look="secondary"
            class="row-unmap"
            label=${this.localize.term('schemeWeaver_unmapBlock')}
            @click=${() => this._disableRow(index)}
          >
            <uui-icon name="icon-trash"></uui-icon>
          </uui-button>
        </div>

        ${row.expanded ? this._renderRowTable(row, index) : nothing}
      </div>
    `;
  }

  private _renderRowTable(row: BlockRow, index: number) {
    if (row.propertyMappings.length === 0) {
      return html`<p class="row-empty-hint">${this.localize.term('schemeWeaver_blockTableEmptyHint')}</p>`;
    }

    return html`
      <uui-table class="nested-mapping-table" aria-label=${this.localize.term('schemeWeaver_nestedMappings')}>
        <uui-table-head>
          <uui-table-head-cell>${this.localize.term('schemeWeaver_schemaProperty')}</uui-table-head-cell>
          <uui-table-head-cell>${this.localize.term('schemeWeaver_value')}</uui-table-head-cell>
          <uui-table-head-cell>${this.localize.term('schemeWeaver_wrapInType')}</uui-table-head-cell>
        </uui-table-head>
        ${row.propertyMappings.map((m, propIndex) => html`
          <uui-table-row>
            <uui-table-cell>
              <div>
                <strong>${m.schemaProperty}</strong>
                <small class="type-label">${m.schemaPropertyType}</small>
              </div>
            </uui-table-cell>
            <uui-table-cell>
              <uui-select
                label=${this.localize.term('schemeWeaver_valueForProperty', m.schemaProperty)}
                .options=${[
                  { name: this.localize.term('schemeWeaver_none'), value: '', selected: !m.contentProperty },
                  ...row.properties.map((p) => ({ name: p, value: p, selected: m.contentProperty === p })),
                ]}
                @change=${(e: Event) => this._handleContentPropertyChange(index, propIndex, (e.target as HTMLSelectElement).value)}
              ></uui-select>
            </uui-table-cell>
            <uui-table-cell>
              ${m.isComplexType
                ? html`
                    <uui-input
                      .value=${m.wrapInType}
                      placeholder=${this.localize.term('schemeWeaver_wrapInType')}
                      label=${this.localize.term('schemeWeaver_wrapInTypeForProperty', m.schemaProperty)}
                      @change=${(e: Event) => this._handleWrapInTypeChange(index, propIndex, (e.target as HTMLInputElement).value)}
                    ></uui-input>
                  `
                : html`<span class="type-label">--</span>`}
            </uui-table-cell>
          </uui-table-row>
        `)}
      </uui-table>
    `;
  }

  static styles = [
    css`
      :host {
        display: block;
      }

      .loading {
        display: flex;
        flex-direction: column;
        align-items: center;
        gap: var(--uui-size-space-3);
        padding: var(--uui-size-space-6);
      }

      .panel-header {
        display: flex;
        align-items: center;
        gap: var(--uui-size-space-3);
        margin-bottom: var(--uui-size-space-4);
      }

      .panel-description {
        color: var(--uui-color-text-alt);
        margin: 0;
        flex: 1;
      }

      .auto-map-all {
        margin-left: auto;
      }

      .block-rows {
        display: flex;
        flex-direction: column;
        gap: var(--uui-size-space-3);
      }

      .block-row {
        border: 1px solid var(--uui-color-border);
        border-radius: var(--uui-border-radius);
        padding: var(--uui-size-space-3);
      }

      .block-row.unmapped {
        opacity: 0.7;
        background: var(--uui-color-surface-alt);
      }

      .block-row.mapped {
        border-left: 3px solid var(--uui-color-positive);
      }

      .block-row-main {
        display: flex;
        align-items: center;
        gap: var(--uui-size-space-3);
        flex-wrap: wrap;
      }

      .block-identity {
        display: flex;
        flex-direction: column;
        min-width: 140px;
      }

      .block-alias {
        color: var(--uui-color-text-alt);
        font-family: monospace;
        font-size: 0.8rem;
      }

      .not-mapped-badge {
        font-size: 0.75rem;
      }

      .map-block-btn {
        margin-left: auto;
      }

      .schema-type-input {
        min-width: 160px;
      }

      uui-select {
        min-width: 130px;
      }

      .mapped-badge {
        font-size: 0.75rem;
        --uui-tag-min-height: 22px;
      }

      .row-unmap {
        margin-left: auto;
      }

      .nested-mapping-table {
        margin-top: var(--uui-size-space-3);
      }

      .type-label {
        display: block;
        color: var(--uui-color-text-alt);
        font-family: monospace;
        font-size: 0.8rem;
        margin-top: 2px;
      }

      .row-empty-hint,
      .no-block-types-hint {
        color: var(--uui-color-text-alt);
        margin: var(--uui-size-space-2) 0 0 0;
      }
    `,
  ];
}

export default NestedMappingModalElement;

declare global {
  interface HTMLElementTagNameMap {
    'schemeweaver-nested-mapping-modal': NestedMappingModalElement;
  }
}
