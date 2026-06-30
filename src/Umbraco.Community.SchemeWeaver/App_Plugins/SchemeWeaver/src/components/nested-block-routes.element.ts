import { UmbLitElement } from '@umbraco-cms/backoffice/lit-element';
import { css, html, customElement, property, state, nothing, repeat } from '@umbraco-cms/backoffice/external/lit';
import { UMB_NOTIFICATION_CONTEXT } from '@umbraco-cms/backoffice/notification';
import { SchemeWeaverRepository } from '../repository/schemeweaver.repository.js';
import './schema-type-input.element.js';
import type { SchemaTypeInputElement } from './schema-type-input.element.js';
import type {
  RankedSchemaPropertyInfo,
  BlockElementTypeInfo,
  BlockRoute,
  BlockRouteSuggestion,
} from '../api/types.js';
import {
  type BlockMappingRow,
  type RoutePropEntry,
  makeBlockRow,
  seedEntriesFromRaw,
  threadNestedSuggestions,
  alignPropertyMappings,
  resolveNestedBlockTypes,
  allowedObjectSchemaTypes,
  schemaTypeSelectOptions,
  serialiseRoutes,
  mappedCount,
  recommendedCount,
  visibleEntries,
} from './block-route-model.js';

/**
 * Recursive editor for ONE block list's routes — one row per block element type, each mappable
 * to a nested Schema.org type with a property table. A property whose chosen value is itself a
 * Block List/Grid can be expanded into another instance of this same component, so the whole
 * routing model nests to whatever depth the (depth-capped) backend reports.
 *
 * Used at depth ≥ 1. The top-level modal (depth 0) is the only level that also picks a target
 * page property per row; everything else is identical and lives here so it is written once.
 *
 * @element schemeweaver-nested-block-routes
 * @fires change - whenever the routes change. Read the serialised value via the `.value` getter.
 */
@customElement('schemeweaver-nested-block-routes')
export class NestedBlockRoutesElement extends UmbLitElement {
  // Own repository instance — sidesteps context-consumption timing (mirrors the modal).
  #repository = new SchemeWeaverRepository(this);
  #notificationContext?: typeof UMB_NOTIFICATION_CONTEXT.TYPE;

  constructor() {
    super();
    this.consumeContext(UMB_NOTIFICATION_CONTEXT, (context) => {
      this.#notificationContext = context;
    });
  }

  /** The block element types available within this nested block list. */
  @property({ attribute: false })
  blockElementTypes: BlockElementTypeInfo[] = [];

  /** Seed routes (initial value). Changing the reference rebuilds the rows (init / auto-map). */
  @property({ attribute: false })
  routes: BlockRoute[] = [];

  /** Suggested routes for this level — drives the "Auto-map" action. */
  @property({ attribute: false })
  suggestedRoutes: BlockRouteSuggestion[] = [];

  /** Nesting depth (1 = first nested level). Drives the indentation styling. */
  @property({ type: Number })
  depth = 1;

  /**
   * Schema.org types each block here may map to. When non-empty the type field is a constrained
   * dropdown; when empty (broad/unknown — e.g. the parent property accepts `Thing`) it falls back
   * to the searchable browse picker.
   */
  @property({ attribute: false })
  allowedSchemaTypes: string[] = [];

  @state()
  private _rows: BlockMappingRow[] = [];

  /** True once the first async build has populated the rows — gates the loading state. */
  @state()
  private _built = false;

  /** Cache of ranked schema-type properties keyed by type name. */
  private _typePropsCache: Record<string, RankedSchemaPropertyInfo[]> = {};

  /** Latest serialised routes — what the parent should persist. */
  get value(): BlockRoute[] {
    return serialiseRoutes(this._rows);
  }

  override updated(changed: Map<string, unknown>) {
    super.updated(changed);
    // Rebuild only when the seed inputs change by reference (init / auto-map / a different
    // nested block list) — never from our own edits, so there is no feedback loop.
    if (changed.has('blockElementTypes') || changed.has('routes')) {
      void this._build();
    }
  }

  private async _build() {
    const routeByAlias = new Map(this.routes.map((r) => [r.blockAlias, r]));
    const suggByAlias = new Map(this.suggestedRoutes.map((r) => [r.blockAlias, r]));

    // Build into a local array — assigning `this._rows` only after the async hydration
    // avoids synchronously scheduling another update from within `updated()`.
    const rows = this.blockElementTypes.map((bt) => {
      const existing = routeByAlias.get(bt.alias);
      const sugg = suggByAlias.get(bt.alias);
      const propertyInfos = makeBlockRow(bt).propertyInfos;
      if (!existing) return makeBlockRow(bt);
      const entries = seedEntriesFromRaw(existing.propertyMappings, propertyInfos, false);
      // Stored routes carry no suggestions; thread the heuristic's nested suggestions through
      // so grandchildren can still offer "Auto-map nested".
      threadNestedSuggestions(entries, sugg?.propertyMappings);
      return makeBlockRow(bt, {
        nestedSchemaType: existing.nestedSchemaType,
        propertyMappings: entries,
        confidence: sugg?.confidence ?? null,
      });
    });

    await Promise.all(rows.map((_, i) => this._hydrate(rows, i)));
    this._rows = rows;
    this._built = true;
    this._emitChange();
  }

  private async _getTypeProperties(typeName: string): Promise<RankedSchemaPropertyInfo[]> {
    if (!this._typePropsCache[typeName]) {
      const props = await this.#repository.requestSchemaTypeProperties(typeName, true);
      this._typePropsCache[typeName] = props || [];
    }
    return this._typePropsCache[typeName];
  }

  private async _hydrate(rows: BlockMappingRow[], index: number): Promise<void> {
    const row = rows[index];
    if (!row || !row.mapped || !row.nestedSchemaType) return;
    const props = await this._getTypeProperties(row.nestedSchemaType);
    if (props.length === 0) {
      rows[index] = { ...row, totalSchemaProps: row.propertyMappings.length };
      return;
    }
    const propertyMappings = alignPropertyMappings(props, row.propertyMappings, row.propertyInfos);
    rows[index] = { ...row, propertyMappings, totalSchemaProps: props.length };
  }

  private _emitChange() {
    this.dispatchEvent(new CustomEvent('change', { bubbles: false, composed: false }));
  }

  private _update(rows: BlockMappingRow[]) {
    this._rows = rows;
    this._emitChange();
  }

  // ── Row edits ─────────────────────────────────────────────────────────────

  private _enableRow(index: number) {
    const rows = [...this._rows];
    const sugg = this.suggestedRoutes.find((r) => r.blockAlias === rows[index].alias);
    rows[index] = {
      ...rows[index],
      mapped: true,
      nestedSchemaType: rows[index].nestedSchemaType || sugg?.nestedSchemaType || '',
      expanded: true,
    };
    this._update(rows);
    void this._rehydrate(index);
  }

  private _disableRow(index: number) {
    const rows = [...this._rows];
    rows[index] = { ...rows[index], mapped: false, expanded: false };
    this._update(rows);
  }

  private _toggleExpand(index: number) {
    const rows = [...this._rows];
    rows[index] = { ...rows[index], expanded: !rows[index].expanded };
    this._update(rows);
  }

  private async _handleSchemaTypeChange(index: number, value: string) {
    const rows = [...this._rows];
    rows[index] = { ...rows[index], nestedSchemaType: value };
    this._update(rows);
    await this._rehydrate(index);
  }

  private async _rehydrate(index: number) {
    const rows = [...this._rows];
    await this._hydrate(rows, index);
    this._rows = rows;
    this._emitChange();
  }

  private async _autoMapRow(index: number) {
    const row = this._rows[index];
    const sugg = this.suggestedRoutes.find((r) => r.blockAlias.toLowerCase() === row.alias.toLowerCase());
    if (!sugg) {
      // The button only renders when a suggestion exists, but guard + inform rather than
      // silently no-op if the suggestion set changed out from under us.
      this.#notificationContext?.peek('warning', {
        data: { message: this.localize.term('schemeWeaver_blockNoSuggestion') },
      });
      return;
    }
    const rows = [...this._rows];
    rows[index] = {
      ...row,
      mapped: true,
      nestedSchemaType: sugg.nestedSchemaType,
      expanded: true,
      propertyMappings: seedEntriesFromRaw(sugg.propertyMappings, row.propertyInfos, true),
    };
    this._update(rows);
    await this._rehydrate(index);
    // A hit with no resolvable property mappings would leave the table empty — always open
    // it (done above) and tell the user so the wand never looks dead.
    if (mappedCount(this._rows[index]) === 0) {
      this.#notificationContext?.peek('warning', {
        data: { message: this.localize.term('schemeWeaver_blockNoMappings') },
      });
    }
  }

  private _setEntry(rowIndex: number, propIndex: number, patch: Partial<RoutePropEntry>) {
    const rows = [...this._rows];
    const row = { ...rows[rowIndex] };
    const mappings = [...row.propertyMappings];
    mappings[propIndex] = { ...mappings[propIndex], ...patch };
    row.propertyMappings = mappings;
    rows[rowIndex] = row;
    this._update(rows);
  }

  private _handleContentPropertyChange(rowIndex: number, propIndex: number, value: string) {
    const row = this._rows[rowIndex];
    const nestedBlockElementTypes = resolveNestedBlockTypes(row.propertyInfos, value);
    // Manually picking a value always resets nested routing — any previously-seeded routes
    // belonged to a different content property (or no longer apply to this block set).
    this._setEntry(rowIndex, propIndex, {
      contentProperty: value,
      nestedBlockElementTypes,
      nestedSeed: [],
      nestedRoutes: [],
      nestedSuggestedRoutes: [],
      nestedExpanded: false,
    });
  }

  private _handleWrapInTypeChange(rowIndex: number, propIndex: number, value: string) {
    this._setEntry(rowIndex, propIndex, { wrapInType: value });
  }

  private _toggleNested(rowIndex: number, propIndex: number) {
    const row = this._rows[rowIndex];
    const entry = row.propertyMappings[propIndex];
    this._setEntry(rowIndex, propIndex, { nestedExpanded: !entry.nestedExpanded });
  }

  private _onNestedChange(rowIndex: number, propIndex: number, e: Event) {
    e.stopPropagation();
    const child = e.target as NestedBlockRoutesElement;
    this._setEntry(rowIndex, propIndex, { nestedRoutes: child.value });
  }

  // ── Render ────────────────────────────────────────────────────────────────

  override render() {
    if (this.blockElementTypes.length === 0) {
      return html`<p class="empty-hint">${this.localize.term('schemeWeaver_noNestedBlockTypesHint')}</p>`;
    }
    if (!this._built) {
      return html`<uui-loader-circle></uui-loader-circle>`;
    }
    return html`
      <div class="nested-routes">
        ${repeat(this._rows, (r) => r.alias, (row, index) => this._renderRow(row, index))}
      </div>
    `;
  }

  /**
   * The block's Schema.org type field: a dropdown constrained to {@link allowedSchemaTypes} when
   * known, otherwise the searchable browse picker. A current value outside the allowed set is
   * preserved as an option so existing saved mappings are never silently dropped.
   */
  private _renderSchemaTypeField(row: BlockMappingRow, index: number) {
    if (this.allowedSchemaTypes.length === 0) {
      return html`<schemeweaver-schema-type-input
        class="schema-type-input"
        .value=${row.nestedSchemaType}
        @change=${(e: Event) => { e.stopPropagation(); this._handleSchemaTypeChange(index, (e.target as SchemaTypeInputElement).value); }}></schemeweaver-schema-type-input>`;
    }

    const options = schemaTypeSelectOptions(
      this.allowedSchemaTypes,
      row.nestedSchemaType,
      this.localize.term('schemeWeaver_selectNestedType'),
    );
    return html`<uui-select
      class="schema-type-input"
      label=${this.localize.term('schemeWeaver_nestedSchemaType')}
      .options=${options}
      @change=${(e: Event) => { e.stopPropagation(); this._handleSchemaTypeChange(index, (e.target as HTMLSelectElement).value); }}></uui-select>`;
  }

  /**
   * The "Wrap in Type" cell: for a complex scalar property, wrap the value in a Schema.org object
   * of the chosen type. Constrained to the property's object accepted types when known, otherwise
   * a free-text input (broad/unknown).
   */
  private _renderWrapInTypeCell(row: BlockMappingRow, rowIndex: number, m: RoutePropEntry, propIndex: number) {
    const isNestedBlock = m.nestedBlockElementTypes.length > 0;
    if (!m.isComplexType || isNestedBlock) {
      return html`<span class="type-label">--</span>`;
    }
    const allowed = allowedObjectSchemaTypes(m);
    if (allowed.length === 0) {
      return html`<uui-input
        .value=${m.wrapInType}
        placeholder=${this.localize.term('schemeWeaver_wrapInType')}
        label=${this.localize.term('schemeWeaver_wrapInTypeForProperty', m.schemaProperty)}
        @change=${(e: Event) => { e.stopPropagation(); this._handleWrapInTypeChange(rowIndex, propIndex, (e.target as HTMLInputElement).value); }}></uui-input>`;
    }
    return html`<uui-select
      label=${this.localize.term('schemeWeaver_wrapInTypeForProperty', m.schemaProperty)}
      .options=${schemaTypeSelectOptions(allowed, m.wrapInType, this.localize.term('schemeWeaver_none'))}
      @change=${(e: Event) => { e.stopPropagation(); this._handleWrapInTypeChange(rowIndex, propIndex, (e.target as HTMLSelectElement).value); }}></uui-select>`;
  }

  private _renderRow(row: BlockMappingRow, index: number) {
    if (!row.mapped) {
      return html`
        <div class="block-row unmapped">
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
            label=${this.localize.term('schemeWeaver_mapThisBlock')}>
            ${this.localize.term('schemeWeaver_mapThisBlock')}
          </uui-button>
        </div>
      `;
    }

    const hasSuggestion = this.suggestedRoutes.some((r) => r.blockAlias.toLowerCase() === row.alias.toLowerCase());
    return html`
      <div class="block-row mapped">
        <div class="block-row-main">
          <div class="block-identity">
            <strong>${row.name}</strong>
            <small class="block-alias">${row.alias}</small>
          </div>

          ${this._renderSchemaTypeField(row, index)}

          <uui-tag look="secondary" color="positive" class="mapped-badge">
            ${this.localize.term('schemeWeaver_recommendedMappedCount', mappedCount(row), recommendedCount(row))}
          </uui-tag>

          ${hasSuggestion
            ? html`<uui-button
                compact
                look="secondary"
                label=${this.localize.term('schemeWeaver_autoMapNested')}
                @click=${() => this._autoMapRow(index)}>
                <uui-icon name="icon-wand"></uui-icon>
              </uui-button>`
            : nothing}

          <uui-button
            compact
            look="secondary"
            label=${row.expanded ? this.localize.term('schemeWeaver_collapse') : this.localize.term('schemeWeaver_expand')}
            @click=${() => this._toggleExpand(index)}>
            <uui-icon name=${row.expanded ? 'icon-navigation-up' : 'icon-navigation-down'}></uui-icon>
          </uui-button>

          <uui-button
            compact
            look="secondary"
            class="row-unmap"
            label=${this.localize.term('schemeWeaver_unmapBlock')}
            @click=${() => this._disableRow(index)}>
            <uui-icon name="icon-trash"></uui-icon>
          </uui-button>
        </div>

        ${row.expanded ? this._renderTable(row, index) : nothing}
      </div>
    `;
  }

  /** Render-only toggle for the long-tail disclosure — must NOT emit `change` (see _update). */
  private _toggleShowAll(index: number) {
    const rows = [...this._rows];
    rows[index] = { ...rows[index], showAll: !rows[index].showAll };
    this._rows = rows;
  }

  private _renderTable(row: BlockMappingRow, rowIndex: number) {
    if (row.propertyMappings.length === 0) {
      return html`<p class="empty-hint">${this.localize.term('schemeWeaver_blockTableEmptyHint')}</p>`;
    }
    const visible = visibleEntries(row);
    const hidden = row.showAll ? 0 : row.propertyMappings.length - visible.length;
    return html`
      <uui-table class="nested-mapping-table" aria-label=${this.localize.term('schemeWeaver_nestedMappings')}>
        <uui-table-head>
          <uui-table-head-cell>${this.localize.term('schemeWeaver_schemaProperty')}</uui-table-head-cell>
          <uui-table-head-cell>${this.localize.term('schemeWeaver_value')}</uui-table-head-cell>
          <uui-table-head-cell>${this.localize.term('schemeWeaver_wrapInType')}</uui-table-head-cell>
        </uui-table-head>
        ${visible.map(({ entry, index }) => this._renderTableRow(row, rowIndex, entry, index))}
      </uui-table>
      ${hidden > 0 || row.showAll
        ? html`<uui-button
            class="show-all-toggle"
            look="default"
            compact
            label=${row.showAll ? this.localize.term('schemeWeaver_showFewerProperties') : this.localize.term('schemeWeaver_showAllProperties', row.propertyMappings.length)}
            @click=${() => this._toggleShowAll(rowIndex)}>
            ${row.showAll
              ? this.localize.term('schemeWeaver_showFewerProperties')
              : this.localize.term('schemeWeaver_showAllProperties', row.propertyMappings.length)}
          </uui-button>`
        : nothing}
    `;
  }

  private _renderTableRow(row: BlockMappingRow, rowIndex: number, m: RoutePropEntry, propIndex: number) {
    const isNestedBlock = m.nestedBlockElementTypes.length > 0;
    return html`
      <uui-table-row>
        <uui-table-cell>
          <div>
            <strong>${m.schemaProperty}</strong>
            <small class="type-label">${m.schemaPropertyType}</small>
          </div>
        </uui-table-cell>
        <uui-table-cell>
          <div class="value-cell">
            <uui-select
              label=${this.localize.term('schemeWeaver_valueForProperty', m.schemaProperty)}
              .options=${[
                { name: this.localize.term('schemeWeaver_none'), value: '', selected: !m.contentProperty },
                ...row.properties.map((p) => ({ name: p, value: p, selected: m.contentProperty === p })),
              ]}
              @change=${(e: Event) => { e.stopPropagation(); this._handleContentPropertyChange(rowIndex, propIndex, (e.target as HTMLSelectElement).value); }}></uui-select>
            ${isNestedBlock
              ? html`<uui-button
                  compact
                  look="secondary"
                  class="nested-toggle"
                  label=${m.nestedExpanded ? this.localize.term('schemeWeaver_collapseNestedBlock') : this.localize.term('schemeWeaver_routeNestedBlock')}
                  @click=${() => this._toggleNested(rowIndex, propIndex)}>
                  <uui-icon name="icon-box"></uui-icon>
                  ${m.nestedExpanded ? this.localize.term('schemeWeaver_collapse') : this.localize.term('schemeWeaver_routeNestedBlock')}
                </uui-button>`
              : nothing}
          </div>
        </uui-table-cell>
        <uui-table-cell>
          ${this._renderWrapInTypeCell(row, rowIndex, m, propIndex)}
        </uui-table-cell>
      </uui-table-row>
      ${isNestedBlock && m.nestedExpanded
        ? html`<uui-table-row class="nested-editor-row">
            <uui-table-cell colspan="3">
              <schemeweaver-nested-block-routes
                .blockElementTypes=${m.nestedBlockElementTypes}
                .routes=${m.nestedSeed}
                .suggestedRoutes=${m.nestedSuggestedRoutes}
                .allowedSchemaTypes=${allowedObjectSchemaTypes(m)}
                .depth=${this.depth + 1}
                @change=${(e: Event) => this._onNestedChange(rowIndex, propIndex, e)}></schemeweaver-nested-block-routes>
            </uui-table-cell>
          </uui-table-row>`
        : nothing}
    `;
  }

  static override styles = [
    css`
      :host {
        display: block;
      }

      .nested-routes {
        display: flex;
        flex-direction: column;
        gap: var(--uui-size-space-2);
        border-left: 2px solid var(--uui-color-divider-emphasis);
        padding-left: var(--uui-size-space-3);
        margin-top: var(--uui-size-space-2);
      }

      .block-row {
        border: 1px solid var(--uui-color-border);
        border-radius: var(--uui-border-radius);
        padding: var(--uui-size-space-2) var(--uui-size-space-3);
        background: var(--uui-color-surface);
      }

      .block-row.unmapped {
        display: flex;
        align-items: center;
        gap: var(--uui-size-space-3);
        opacity: 0.75;
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
        min-width: 120px;
      }

      .block-alias {
        color: var(--uui-color-text-alt);
        font-family: monospace;
        font-size: 0.8rem;
      }

      .map-block-btn {
        margin-left: auto;
      }

      .schema-type-input {
        min-width: 150px;
      }

      uui-select {
        min-width: 130px;
      }

      .value-cell {
        display: flex;
        align-items: center;
        gap: var(--uui-size-space-2);
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

      .show-all-toggle {
        margin-top: var(--uui-size-space-2);
        width: 100%;
        --uui-button-font-weight: normal;
      }

      .nested-editor-row uui-table-cell {
        background: var(--uui-color-surface-alt);
      }

      .type-label {
        display: block;
        color: var(--uui-color-text-alt);
        font-family: monospace;
        font-size: 0.8rem;
        margin-top: 2px;
      }

      .empty-hint {
        color: var(--uui-color-text-alt);
        margin: var(--uui-size-space-2) 0 0 0;
      }
    `,
  ];
}

export default NestedBlockRoutesElement;

declare global {
  interface HTMLElementTagNameMap {
    'schemeweaver-nested-block-routes': NestedBlockRoutesElement;
  }
}
