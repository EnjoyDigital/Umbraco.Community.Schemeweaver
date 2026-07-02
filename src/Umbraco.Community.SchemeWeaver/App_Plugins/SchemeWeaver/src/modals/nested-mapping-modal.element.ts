import { css, html, customElement, state, nothing, repeat } from '@umbraco-cms/backoffice/external/lit';
import { UmbModalBaseElement } from '@umbraco-cms/backoffice/modal';
import { UMB_NOTIFICATION_CONTEXT } from '@umbraco-cms/backoffice/notification';
import { SchemeWeaverRepository } from '../repository/schemeweaver.repository.js';
import '../components/nested-block-routes.element.js';
import type { NestedBlockRoutesElement } from '../components/nested-block-routes.element.js';
import '../components/schema-type-input.element.js';
import type { SchemaTypeInputElement } from '../components/schema-type-input.element.js';
import {
  type BlockMappingRow as BlockRow,
  type RoutePropEntry,
  type RowSeed,
  makeBlockRow,
  rowPropertyInfos,
  seedEntriesFromRaw,
  seedRowsFromLegacyConfig,
  parseResolverConfig,
  threadNestedSuggestions,
  alignPropertyMappings,
  resolveNestedBlockTypes,
  allowedObjectSchemaTypes,
  schemaTypeSelectOptions,
  serialiseRoutes,
  convertSuggestedRoutes,
  mappedCount,
  recommendedMapped,
  recommendedTotal,
  visibleEntries,
} from '../components/block-route-model.js';
import { filterOutPrimitiveAcceptedTypes } from '../utils/schema-primitives.js';
import type {
  RankedSchemaPropertyInfo,
  BlockElementTypeInfo,
  BlockMappingSuggestion,
  BlockRouteSuggestion,
  RoutedResolverConfig,
} from '../api/types.js';

import type {
  NestedMappingModalData,
  NestedMappingModalValue,
  NestedMappingModalAdditionalTarget,
} from './nested-mapping-modal.token.js';

/** A block-suggest hit for one block element type. */
interface SuggestionHit {
  /** The suggester's preferred TARGET page property (may differ from this panel's). */
  target: string;
  route: BlockRouteSuggestion;
  confidence: number;
}

/** An auto-map suggestion that fits a DIFFERENT target than this panel's row. */
interface OffTargetRoute {
  blockAlias: string;
  blockName: string;
  target: string;
  route: BlockRouteSuggestion;
}

/** Sentinel option value for the constrained type picker's "Other type…" escape hatch. */
const OTHER_TYPE_OPTION = '__schemeweaver-other-type__';

/**
 * Route editor scoped to exactly ONE parent property-mapping row: maps the block
 * element types of a Block List/Grid property INTO the parent row's Schema.org
 * property. The target is immutable context — placement is decided by the parent
 * row's `schemaPropertyName` on the main table, never in here. Multi-target is
 * expressed as separate rows; off-target auto-map suggestions are offered as an
 * explicit fan-out (`value.additionalTargets`) instead of silently re-targeting.
 */
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

  /** True when the stored config is a string-list extraction — read-only notice, no route editor. */
  @state()
  private _stringListMode = false;

  /** The string-list source block property, for the notice text. */
  @state()
  private _stringListSource = '';

  /** Suggested routes that fit OTHER targets — offered as an explicit fan-out, never applied here. */
  @state()
  private _offTargetRoutes: OffTargetRoute[] = [];

  /** True once the user opted into the fan-out — `additionalTargets` are emitted on save. */
  @state()
  private _fanOutQueued = false;

  /** Rows whose constrained type picker was toggled to the free searchable input. */
  @state()
  private _freeTypeRows: ReadonlySet<string> = new Set<string>();

  /**
   * Whether the user changed anything. When false, save returns
   * `data.existingConfig` VERBATIM — a no-change open+save is a persistence no-op
   * (byte fidelity is a hard invariant: legacy configs only upgrade on real edits).
   */
  private _dirty = false;

  /** Fan-out targets queued by the explicit "create rows" affordance. */
  private _queuedAdditionalTargets: NestedMappingModalAdditionalTarget[] = [];

  /** Latest block-suggest hits keyed by LOWERCASED block alias. */
  private _suggestionByBlock = new Map<string, SuggestionHit>();

  /**
   * Nested child editors that have delivered their initial (mount) `change` emit.
   * The child emits once after its first build — that sync must not mark the
   * panel dirty; only subsequent (user-driven) emits do.
   */
  #nestedSyncedChildren = new WeakSet<EventTarget>();

  /** Cache of ranked schema-type properties keyed by type name. */
  private _typePropsCache: Record<string, RankedSchemaPropertyInfo[]> = {};

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
      const config = parseResolverConfig(this.data?.existingConfig);
      if (config?.extractAs === 'stringList') {
        // String-list extraction has no block routes to edit — render a read-only
        // notice and round-trip the stored config VERBATIM on save.
        this._stringListMode = true;
        this._stringListSource = config.contentProperty ?? '';
        return;
      }

      const contentTypeAlias = this.data?.contentTypeAlias || '';
      const propertyAlias = this.data?.contentTypePropertyAlias || '';

      const [blockTypes, suggestions] = await Promise.all([
        propertyAlias
          ? this.#repository.requestBlockElementTypes(contentTypeAlias, propertyAlias)
          : Promise.resolve(undefined),
        propertyAlias
          ? this.#repository.requestBlockSuggestions(contentTypeAlias, propertyAlias, this.data?.schemaPropertyName)
          : Promise.resolve(undefined),
      ]);

      const blocks = blockTypes ?? [];
      this._suggestionByBlock = this._indexSuggestionRoutes(suggestions ?? []);
      const claimsByBlock = this._indexSiblingClaims();
      const seeds = this._buildSeeds(blocks, config);

      this._blockRows = blocks.map((bt) => {
        const key = bt.alias.toLowerCase();
        const row = makeBlockRow(bt, seeds.get(key));
        const claims = claimsByBlock.get(key);
        if (claims?.length) row.claimedBy = claims;
        if (!row.mapped) {
          // Display-only hint: the suggester routes this block to a DIFFERENT target.
          const hit = this._suggestionByBlock.get(key);
          if (hit && !this._routeFits(hit.route)) row.suggestedTarget = hit.target;
        }
        return row;
      });

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

  /**
   * Build per-block RowSeeds for THIS row's stored config (all legacy shapes
   * must round-trip):
   * - routed (`routes[]`) → one seed per route; wildcard routes apply to every
   *   block without an explicit route.
   * - legacy flat (`nestedMappings[]`) → {@link seedRowsFromLegacyConfig}, so
   *   wildcard entries seed EVERY block row (the old modal keyed them by `''`
   *   and matched nothing — the invisible-legacy bug).
   * - bare `nestedSchemaTypeName` → every block seeded with that type and an
   *   empty table (the renderer auto-maps by name today; shown honestly).
   * - fresh (no config, no hint) → seed from the suggester, but ONLY routes
   *   whose nested type fits this row's target.
   */
  private _buildSeeds(blocks: BlockElementTypeInfo[], config: RoutedResolverConfig | null): Map<string, RowSeed> {
    const target = this.data?.schemaPropertyName ?? '';
    const seeds = new Map<string, RowSeed>();

    if (config && Array.isArray(config.routes)) {
      const explicit = config.routes.filter((r) => !!r.blockAlias);
      const wildcard = config.routes.filter((r) => !r.blockAlias);
      for (const bt of blocks) {
        const route =
          explicit.find((r) => r.blockAlias.toLowerCase() === bt.alias.toLowerCase()) ?? wildcard[0];
        if (!route) continue;
        const entries = seedEntriesFromRaw(route.propertyMappings ?? [], rowPropertyInfos(bt), false);
        // Stored routes carry no suggestions; thread the heuristic's nested suggestions
        // through so the nested editor can still offer "Auto-map nested" when re-editing.
        threadNestedSuggestions(entries, this._suggestionByBlock.get(bt.alias.toLowerCase())?.route.propertyMappings);
        seeds.set(bt.alias.toLowerCase(), {
          nestedSchemaType: route.nestedSchemaType ?? '',
          propertyMappings: entries,
          targetProperty: target,
          requiredProperties: route.requiredProperties ?? undefined,
        });
      }
      return seeds;
    }

    if (config && Array.isArray(config.nestedMappings)) {
      const legacy = seedRowsFromLegacyConfig(blocks, config.nestedMappings, this.data?.nestedSchemaTypeName);
      for (const [alias, seed] of legacy) {
        threadNestedSuggestions(seed.propertyMappings, this._suggestionByBlock.get(alias)?.route.propertyMappings);
        seeds.set(alias, { ...seed, targetProperty: target });
      }
      return seeds;
    }

    if (this.data?.nestedSchemaTypeName) {
      for (const bt of blocks) {
        seeds.set(bt.alias.toLowerCase(), {
          nestedSchemaType: this.data.nestedSchemaTypeName,
          propertyMappings: [],
          targetProperty: target,
        });
      }
      return seeds;
    }

    if (!this.data?.existingConfig) {
      // Fresh row: auto-seed only the suggested routes that FIT this target.
      // Emitting them is a real change from `null`, so seeding marks the panel dirty.
      for (const bt of blocks) {
        const hit = this._suggestionByBlock.get(bt.alias.toLowerCase());
        if (!hit || !this._routeFits(hit.route)) continue;
        seeds.set(bt.alias.toLowerCase(), {
          nestedSchemaType: hit.route.nestedSchemaType,
          propertyMappings: seedEntriesFromRaw(hit.route.propertyMappings, rowPropertyInfos(bt), true),
          confidence: hit.confidence,
          targetProperty: target,
        });
      }
      if (seeds.size > 0) this._dirty = true;
    }

    return seeds;
  }

  /** blockAlias → suggestion hit from the block-suggest response. */
  private _indexSuggestionRoutes(suggestions: BlockMappingSuggestion[]): Map<string, SuggestionHit> {
    const map = new Map<string, SuggestionHit>();
    for (const s of suggestions) {
      for (const route of s.routes) {
        // Key case-insensitively so a block alias whose casing differs between the
        // element-type list and the suggester still resolves.
        map.set(route.blockAlias.toLowerCase(), { target: s.schemaProperty, route, confidence: route.confidence });
      }
    }
    return map;
  }

  /** LOWERCASED block alias → sibling target properties that already route it. */
  private _indexSiblingClaims(): Map<string, string[]> {
    const map = new Map<string, string[]>();
    for (const claim of this.data?.siblingClaims ?? []) {
      for (const alias of claim.blockAliases) {
        const key = alias.toLowerCase();
        const list = map.get(key) ?? [];
        list.push(claim.schemaPropertyName);
        map.set(key, list);
      }
    }
    return map;
  }

  /**
   * Whether a suggested route's nested type fits THIS row's target property.
   * Server-authoritative when `fitsTarget` is present; else a client-side range
   * check (empty/`Thing` ranges accept anything).
   */
  private _routeFits(route: BlockRouteSuggestion): boolean {
    if (typeof route.fitsTarget === 'boolean') return route.fitsTarget;
    const accepted = this.data?.acceptedTypes ?? [];
    if (accepted.length === 0) return true;
    if (accepted.includes('Thing')) return true;
    return accepted.some((t) => t.toLowerCase() === route.nestedSchemaType.toLowerCase());
  }

  /**
   * Fetch the chosen nested schema type's properties and align the row's
   * property table to them, preserving any already-chosen content properties
   * and nested-block routes.
   */
  private async _hydrateRow(index: number): Promise<void> {
    const row = this._blockRows[index];
    if (!row || !row.mapped || !row.nestedSchemaType) return;

    const props = await this._getTypeProperties(row.nestedSchemaType);
    if (props.length === 0) {
      // Unknown nested type — keep whatever was seeded.
      this._blockRows[index] = { ...row, totalSchemaProps: row.propertyMappings.length };
      return;
    }

    const propertyMappings = alignPropertyMappings(props, row.propertyMappings, row.propertyInfos);
    this._blockRows[index] = { ...row, propertyMappings, totalSchemaProps: props.length };
  }

  private async _getTypeProperties(typeName: string): Promise<RankedSchemaPropertyInfo[]> {
    if (!this._typePropsCache[typeName]) {
      const props = await this.#repository.requestSchemaTypeProperties(typeName, true);
      this._typePropsCache[typeName] = props || [];
    }
    return this._typePropsCache[typeName];
  }

  // ── Auto-map ───────────────────────────────────────────────────────────────

  private async _handleAutoMapAll() {
    this._autoMapping = true;
    try {
      const suggestions = await this.#repository.requestBlockSuggestions(
        this.data?.contentTypeAlias || '',
        this.data?.contentTypePropertyAlias || '',
        this.data?.schemaPropertyName,
      );
      this._suggestionByBlock = this._indexSuggestionRoutes(suggestions ?? []);

      // Recomputing the off-target list invalidates any previously queued fan-out —
      // otherwise the banner would show the new list while save emits the old queue.
      this._queuedAdditionalTargets = [];
      this._fanOutQueued = false;

      let appliedAny = false;
      const offTarget: OffTargetRoute[] = [];
      this._blockRows = this._blockRows.map((row) => {
        const hit = this._suggestionByBlock.get(row.alias.toLowerCase());
        if (!hit) return row; // no suggestion — leave as-is (e.g. SKIP blocks)
        if (!this._routeFits(hit.route)) {
          // Fits a DIFFERENT target — never applied to this row; offered as fan-out.
          offTarget.push({ blockAlias: row.alias, blockName: row.name, target: hit.target, route: hit.route });
          return row.mapped ? row : { ...row, suggestedTarget: hit.target };
        }
        appliedAny = true;
        return this._applyRouteToRow(row, hit.route, hit.confidence);
      });
      if (appliedAny) this._dirty = true;
      this._offTargetRoutes = offTarget;

      await Promise.all(this._blockRows.map((_, i) => this._hydrateRow(i)));
      this._blockRows = [...this._blockRows];

      if (offTarget.length > 0) {
        this.#notificationContext?.peek('warning', {
          data: {
            message: this.localize.term(
              'schemeWeaver_blockAutoMapSkipped',
              offTarget.length,
              offTarget.map((o) => `${o.blockName} → ${o.target}`).join(', '),
            ),
          },
        });
      }

      // Feedback so the wand is never a silent no-op: nothing matched, or matches produced
      // no resolvable property mappings.
      const mappedAnything = this._blockRows.some((row) => row.mapped && this._mappedCount(row) > 0);
      if (!appliedAny && offTarget.length === 0) {
        this.#notificationContext?.peek('warning', {
          data: { message: this.localize.term('schemeWeaver_blockNoSuggestion') },
        });
      } else if (appliedAny && !mappedAnything) {
        this.#notificationContext?.peek('warning', {
          data: { message: this.localize.term('schemeWeaver_blockNoMappings') },
        });
      }
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
      this.data?.schemaPropertyName,
    );
    const hit = this._indexSuggestionRoutes(suggestions ?? []).get(row.alias.toLowerCase());
    if (!hit) {
      this.#notificationContext?.peek('warning', {
        data: { message: this.localize.term('schemeWeaver_blockNoSuggestion') },
      });
      return;
    }
    if (!this._routeFits(hit.route)) {
      // The only suggestion fits a different target — say so instead of mis-applying it.
      this.#notificationContext?.peek('warning', {
        data: {
          message: this.localize.term('schemeWeaver_blockAutoMapSkipped', 1, `${row.name} → ${hit.target}`),
        },
      });
      return;
    }
    const updated = [...this._blockRows];
    updated[index] = this._applyRouteToRow(row, hit.route, hit.confidence);
    this._blockRows = updated;
    this._dirty = true;
    await this._hydrateRow(index);
    this._blockRows = [...this._blockRows];
    // A hit that produced no resolvable property mappings still opens the table; tell the
    // user so the wand never looks dead.
    if (this._mappedCount(this._blockRows[index]) === 0) {
      this.#notificationContext?.peek('warning', {
        data: { message: this.localize.term('schemeWeaver_blockNoMappings') },
      });
    }
  }

  private _applyRouteToRow(row: BlockRow, route: BlockRouteSuggestion, confidence: number): BlockRow {
    return {
      ...row,
      mapped: true,
      nestedSchemaType: route.nestedSchemaType,
      // Scoped panel: blocks always map into the parent row's property.
      targetProperty: this.data?.schemaPropertyName ?? '',
      suggestedTarget: undefined,
      confidence,
      propertyMappings: seedEntriesFromRaw(route.propertyMappings, row.propertyInfos, true),
      totalSchemaProps: route.propertyMappings.length,
    };
  }

  /**
   * Queue the off-target suggestions as explicit fan-out rows: grouped by their
   * suggested target and emitted via `value.additionalTargets` on save. The
   * caller merges/creates SIBLING rows — this row is never touched.
   */
  private _handleFanOutCreate() {
    if (this._offTargetRoutes.length === 0) return;
    const panelTarget = (this.data?.schemaPropertyName ?? '').toLowerCase();
    const byTarget = new Map<string, BlockRouteSuggestion[]>();
    for (const o of this._offTargetRoutes) {
      // Never fan out to THIS row's own property — that would create a duplicate
      // sibling row for the same target (possible only when the server-side fit
      // annotation is unavailable and the client fallback rejected a subtype).
      if (o.target.toLowerCase() === panelTarget) continue;
      const list = byTarget.get(o.target) ?? [];
      list.push(o.route);
      byTarget.set(o.target, list);
    }
    if (byTarget.size === 0) return;
    this._queuedAdditionalTargets = [...byTarget.entries()].map(([schemaPropertyName, routes]) => ({
      schemaPropertyName,
      resolverConfig: JSON.stringify({ routes: convertSuggestedRoutes(routes) }),
    }));
    this._fanOutQueued = true;
    this.#notificationContext?.peek('positive', {
      data: {
        message: this.localize.term('schemeWeaver_blockFanOutCreated', [...byTarget.keys()].join(', ')),
      },
    });
  }

  // ── Row edits ──────────────────────────────────────────────────────────────

  private _mappedCount(row: BlockRow): number {
    return mappedCount(row);
  }

  /**
   * Opt a block in. Never defaults a target — this panel's target is fixed.
   * The nested type seeds from the block's suggestion when it FITS, else the
   * single accepted object type, else stays empty for the user to choose.
   */
  private async _enableRow(index: number) {
    const row = this._blockRows[index];
    let nestedSchemaType = '';
    const hit = this._suggestionByBlock.get(row.alias.toLowerCase());
    if (hit && this._routeFits(hit.route)) {
      nestedSchemaType = hit.route.nestedSchemaType;
    } else {
      const allowed = filterOutPrimitiveAcceptedTypes(this.data?.acceptedTypes ?? []);
      if (allowed.length === 1 && allowed[0] !== 'Thing') nestedSchemaType = allowed[0];
    }
    const updated = [...this._blockRows];
    updated[index] = {
      ...row,
      mapped: true,
      nestedSchemaType,
      targetProperty: this.data?.schemaPropertyName ?? '',
      expanded: true,
    };
    this._blockRows = updated;
    this._dirty = true;
    if (nestedSchemaType) {
      await this._hydrateRow(index);
      this._blockRows = [...this._blockRows];
    }
  }

  private _disableRow(index: number) {
    const updated = [...this._blockRows];
    updated[index] = { ...updated[index], mapped: false, expanded: false };
    this._blockRows = updated;
    this._dirty = true;
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
    this._dirty = true;
    await this._hydrateRow(index);
    this._blockRows = [...this._blockRows];
  }

  private async _handleTypeSelectChange(index: number, value: string) {
    if (value === OTHER_TYPE_OPTION) {
      // Escape hatch: switch this row to the free searchable type input.
      this._freeTypeRows = new Set([...this._freeTypeRows, this._blockRows[index].alias]);
      return;
    }
    await this._handleSchemaTypeChange(index, value);
  }

  private _setEntry(rowIndex: number, propIndex: number, patch: Partial<RoutePropEntry>) {
    const updated = [...this._blockRows];
    const row = { ...updated[rowIndex] };
    const mappings = [...row.propertyMappings];
    mappings[propIndex] = { ...mappings[propIndex], ...patch };
    row.propertyMappings = mappings;
    updated[rowIndex] = row;
    this._blockRows = updated;
  }

  private _handleContentPropertyChange(rowIndex: number, propIndex: number, value: string) {
    const row = this._blockRows[rowIndex];
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
    this._dirty = true;
  }

  private _handleWrapInTypeChange(rowIndex: number, propIndex: number, value: string) {
    this._setEntry(rowIndex, propIndex, { wrapInType: value });
    this._dirty = true;
  }

  private _toggleNested(rowIndex: number, propIndex: number) {
    const entry = this._blockRows[rowIndex].propertyMappings[propIndex];
    this._setEntry(rowIndex, propIndex, { nestedExpanded: !entry.nestedExpanded });
  }

  private _onNestedChange(rowIndex: number, propIndex: number, e: Event) {
    e.stopPropagation();
    const child = e.target as NestedBlockRoutesElement;
    this._setEntry(rowIndex, propIndex, { nestedRoutes: child.value });
    // The child emits once after its initial build — that sync is not a user edit.
    if (this.#nestedSyncedChildren.has(child)) {
      this._dirty = true;
    } else {
      this.#nestedSyncedChildren.add(child);
    }
  }

  // ── Save ─────────────────────────────────────────────────────────────────

  /**
   * Serialise THIS row's ResolverConfig. Byte fidelity: when nothing changed
   * (or the config is a string-list extraction) the stored JSON is returned
   * VERBATIM. On a real edit the config upgrades to the routed shape,
   * preserving every root-level field except `routes`/`nestedMappings`
   * (`wrapInListItem`, `positionProperty`, `requiredProperties`, unknown keys).
   */
  private _buildResolverConfig(): string | null {
    const existing = this.data?.existingConfig ?? null;
    if (this._stringListMode || !this._dirty) return existing;

    const parsed = parseResolverConfig(existing);
    const extras: Record<string, unknown> = {};
    for (const [key, value] of Object.entries(parsed ?? {})) {
      if (key === 'routes' || key === 'nestedMappings') continue;
      extras[key] = value;
    }
    const routes = serialiseRoutes(this._blockRows);
    const hasMeaningfulExtras = Object.values(extras).some((v) => v !== null && v !== undefined);
    if (routes.length === 0 && !hasMeaningfulExtras) return null;
    return JSON.stringify({ ...extras, routes });
  }

  private _handleSave() {
    this.modalContext?.setValue({
      resolverConfig: this._buildResolverConfig(),
      additionalTargets: this._fanOutQueued ? this._queuedAdditionalTargets : [],
    });
    this.modalContext?.submit();
  }

  private _handleClose() {
    this.modalContext?.reject();
  }

  // ── Render ─────────────────────────────────────────────────────────────────

  render() {
    return html`
      <umb-body-layout headline="${this.localize.term('schemeWeaver_blockMappingsFor', this.data?.schemaPropertyName ?? '')}">
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
          <uui-button
            look="primary"
            data-mark="schemeweaver:block-modal-save"
            @click=${this._handleSave}
            label=${this.localize.term('schemeWeaver_save')}
          >
            ${this.localize.term('schemeWeaver_save')}
          </uui-button>
        </div>
      </umb-body-layout>
    `;
  }

  /** The immutable-target context strip rendered under the headline. */
  private _renderContextStrip() {
    const target = this.data?.schemaPropertyName ?? '';
    const targetType = this.data?.schemaPropertyType ?? '';
    const accepted = this.data?.acceptedTypes ?? [];
    return html`
      <div class="context-strip">
        <p class="context-route">
          <code>${this.data?.contentTypePropertyAlias ?? ''}</code>
          <span class="context-kind">(${this.localize.term('schemeWeaver_blockModalEditorKind')})</span>
          <span class="context-arrow" aria-hidden="true">→</span>
          <strong>${target}</strong>
          ${targetType ? html`<span class="context-type">(${targetType})</span>` : nothing}
        </p>
        <p class="context-sentence">
          ${this.localize.term('schemeWeaver_blockModalContext', target, this.data?.contentTypeAlias ?? '')}
        </p>
        ${accepted.length > 0
          ? html`<p class="context-accepts">${this.localize.term('schemeWeaver_blockModalAccepts', accepted.join(', '))}</p>`
          : nothing}
      </div>
    `;
  }

  private _renderPanel() {
    if (this._stringListMode) {
      return html`
        <uui-box headline=${this.localize.term('schemeWeaver_blockMappings')}>
          ${this._renderContextStrip()}
          <p class="string-list-notice">
            ${this.localize.term('schemeWeaver_stringListNotice', this._stringListSource)}
          </p>
        </uui-box>
      `;
    }

    if (this._blockRows.length === 0) {
      return html`
        <uui-box headline=${this.localize.term('schemeWeaver_blockMappings')}>
          ${this._renderContextStrip()}
          <p class="no-block-types-hint">${this.localize.term('schemeWeaver_noBlockTypesHint')}</p>
          <p class="no-block-types-hint">${this.localize.term('schemeWeaver_noBlockTypesConfigureHint')}</p>
        </uui-box>
      `;
    }

    return html`
      <uui-box headline=${this.localize.term('schemeWeaver_blockMappings')}>
        ${this._renderContextStrip()}
        <div class="panel-header">
          <p class="panel-description">${this.localize.term('schemeWeaver_blockMappingsDescription')}</p>
          <uui-button
            class="auto-map-all"
            look="secondary"
            data-mark="schemeweaver:block-automap-all"
            ?disabled=${this._autoMapping}
            @click=${this._handleAutoMapAll}
            label=${this.localize.term('schemeWeaver_autoMapAll')}
          >
            <uui-icon name="icon-wand"></uui-icon>
            ${this._autoMapping ? this.localize.term('schemeWeaver_loadingEllipsis') : this.localize.term('schemeWeaver_autoMapAll')}
          </uui-button>
        </div>

        ${this._renderFanOutBanner()}

        <div class="block-rows">
          ${repeat(this._blockRows, (r) => r.alias, (row, index) => this._renderBlockRow(row, index))}
        </div>
      </uui-box>
    `;
  }

  /** Inline offer to create sibling rows for off-target auto-map suggestions. */
  private _renderFanOutBanner() {
    if (this._offTargetRoutes.length === 0) return nothing;
    const summary = this._offTargetRoutes.map((o) => `${o.blockName} → ${o.target}`).join(', ');
    if (this._fanOutQueued) {
      const targets = [...new Set(this._offTargetRoutes.map((o) => o.target))].join(', ');
      return html`
        <div class="fan-out-banner queued">
          <span>${this.localize.term('schemeWeaver_blockFanOutCreated', targets)}</span>
        </div>
      `;
    }
    return html`
      <div class="fan-out-banner">
        <span>${this.localize.term('schemeWeaver_blockFanOutOffer', this._offTargetRoutes.length, summary)}</span>
        <uui-button
          compact
          look="secondary"
          data-mark="schemeweaver:block-fanout-create"
          @click=${this._handleFanOutCreate}
          label=${this.localize.term('schemeWeaver_blockFanOutCreate')}
        >
          ${this.localize.term('schemeWeaver_blockFanOutCreate')}
        </uui-button>
      </div>
    `;
  }

  /** Read-only tags for sibling rows (other targets) that already route this block. */
  private _renderClaimTags(row: BlockRow) {
    if (!row.claimedBy?.length) return nothing;
    return html`${row.claimedBy.map(
      (schemaPropertyName) => html`
        <uui-tag look="secondary" class="claim-tag">
          ${this.localize.term('schemeWeaver_mappedViaProperty', schemaPropertyName)}
        </uui-tag>
      `,
    )}`;
  }

  /**
   * The per-block nested-type picker. Constrained to the parent property's object
   * accepted types when they are known and narrower than `Thing` — with an explicit
   * "Other type…" escape hatch to the free searchable input. Broad/unknown ranges
   * keep the searchable input as before.
   */
  private _renderTypePicker(row: BlockRow, index: number) {
    const allowed = filterOutPrimitiveAcceptedTypes(this.data?.acceptedTypes ?? []);
    const constrained = allowed.length > 0 && !allowed.includes('Thing') && !this._freeTypeRows.has(row.alias);
    if (!constrained) {
      return html`
        <schemeweaver-schema-type-input
          class="schema-type-input"
          .value=${row.nestedSchemaType}
          .contentTypeAlias=${this.data?.contentTypeAlias || ''}
          @change=${(e: Event) => this._handleSchemaTypeChange(index, (e.target as SchemaTypeInputElement).value)}
        ></schemeweaver-schema-type-input>
      `;
    }
    const options = [
      ...schemaTypeSelectOptions(allowed, row.nestedSchemaType, this.localize.term('schemeWeaver_none')),
      { name: this.localize.term('schemeWeaver_otherSchemaType'), value: OTHER_TYPE_OPTION, selected: false },
    ];
    return html`
      <uui-select
        class="schema-type-select"
        label=${this.localize.term('schemeWeaver_nestedTypeForProperty', row.name)}
        .options=${options}
        @change=${(e: Event) => this._handleTypeSelectChange(index, (e.target as HTMLSelectElement).value)}
      ></uui-select>
    `;
  }

  private _renderBlockRow(row: BlockRow, index: number) {
    if (!row.mapped) {
      const suggestedType = this._suggestionByBlock.get(row.alias.toLowerCase())?.route.nestedSchemaType ?? '';
      return html`
        <div class="block-row unmapped" data-mark="schemeweaver:block-row:${row.alias}">
          <div class="block-row-main">
            <div class="block-identity">
              <strong>${row.name}</strong>
              <small class="block-alias">${row.alias}</small>
            </div>
            ${this._renderClaimTags(row)}
            <uui-tag look="secondary" class="not-mapped-badge">${this.localize.term('schemeWeaver_notMapped')}</uui-tag>
            ${row.suggestedTarget
              ? html`<span class="suggested-hint">
                  ${this.localize.term('schemeWeaver_suggestedTypeViaProperty', suggestedType, row.suggestedTarget)}
                </span>`
              : nothing}
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
    const badgeDetail = this.localize.term(
      'schemeWeaver_blockMappedCountDetail',
      mapped,
      row.propertyMappings.length,
      recommendedMapped(row),
      recommendedTotal(row),
    );
    return html`
      <div class="block-row mapped" data-mark="schemeweaver:block-row:${row.alias}">
        <div class="block-row-main">
          <div class="block-identity">
            <strong>${row.name}</strong>
            <small class="block-alias">${row.alias}</small>
          </div>

          ${this._renderClaimTags(row)}
          ${this._renderTypePicker(row, index)}

          <uui-tag
            look="secondary"
            color="positive"
            class="mapped-badge"
            title=${badgeDetail}
            aria-label=${badgeDetail}
          >
            ${this.localize.term('schemeWeaver_blockMappedCount', mapped)}
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

  /** Render-only toggle for the long-tail disclosure (the modal saves on its own button). */
  private _toggleShowAll(index: number) {
    const updated = [...this._blockRows];
    updated[index] = { ...updated[index], showAll: !updated[index].showAll };
    this._blockRows = updated;
  }

  private _renderRowTable(row: BlockRow, index: number) {
    if (row.propertyMappings.length === 0) {
      return html`<p class="row-empty-hint">${this.localize.term('schemeWeaver_blockTableEmptyHint')}</p>`;
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
        ${visible.map(({ entry, index: propIndex }) => this._renderTableRow(row, index, entry, propIndex))}
      </uui-table>
      ${hidden > 0 || row.showAll
        ? html`<uui-button
            class="show-all-toggle"
            look="default"
            compact
            label=${row.showAll ? this.localize.term('schemeWeaver_showFewerProperties') : this.localize.term('schemeWeaver_showAllProperties', row.propertyMappings.length)}
            @click=${() => this._toggleShowAll(index)}>
            ${row.showAll
              ? this.localize.term('schemeWeaver_showFewerProperties')
              : this.localize.term('schemeWeaver_showAllProperties', row.propertyMappings.length)}
          </uui-button>`
        : nothing}
    `;
  }

  /**
   * The "Wrap in Type" cell: wrap a complex scalar property's value in a Schema.org object of the
   * chosen type. Constrained dropdown of the property's object accepted types when known, else a
   * free-text input (broad/unknown).
   */
  private _renderWrapInTypeCell(m: RoutePropEntry, index: number, propIndex: number) {
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
        @change=${(e: Event) => this._handleWrapInTypeChange(index, propIndex, (e.target as HTMLInputElement).value)}
      ></uui-input>`;
    }
    return html`<uui-select
      label=${this.localize.term('schemeWeaver_wrapInTypeForProperty', m.schemaProperty)}
      .options=${schemaTypeSelectOptions(allowed, m.wrapInType, this.localize.term('schemeWeaver_none'))}
      @change=${(e: Event) => this._handleWrapInTypeChange(index, propIndex, (e.target as HTMLSelectElement).value)}
    ></uui-select>`;
  }

  private _renderTableRow(row: BlockRow, index: number, m: RoutePropEntry, propIndex: number) {
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
              @change=${(e: Event) => this._handleContentPropertyChange(index, propIndex, (e.target as HTMLSelectElement).value)}
            ></uui-select>
            ${isNestedBlock
              ? html`<uui-button
                  compact
                  look="secondary"
                  class="nested-toggle"
                  label=${m.nestedExpanded ? this.localize.term('schemeWeaver_collapseNestedBlock') : this.localize.term('schemeWeaver_routeNestedBlock')}
                  @click=${() => this._toggleNested(index, propIndex)}
                >
                  <uui-icon name="icon-box"></uui-icon>
                  ${m.nestedExpanded ? this.localize.term('schemeWeaver_collapse') : this.localize.term('schemeWeaver_routeNestedBlock')}
                </uui-button>`
              : nothing}
          </div>
        </uui-table-cell>
        <uui-table-cell>
          ${this._renderWrapInTypeCell(m, index, propIndex)}
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
                .depth=${1}
                @change=${(e: Event) => this._onNestedChange(index, propIndex, e)}
              ></schemeweaver-nested-block-routes>
            </uui-table-cell>
          </uui-table-row>`
        : nothing}
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

      .context-strip {
        border-bottom: 1px solid var(--uui-color-border);
        padding-bottom: var(--uui-size-space-3);
        margin-bottom: var(--uui-size-space-4);
      }

      .context-route {
        display: flex;
        align-items: center;
        gap: var(--uui-size-space-2);
        margin: 0 0 var(--uui-size-space-1) 0;
        flex-wrap: wrap;
      }

      .context-route code {
        font-family: monospace;
        background: var(--uui-color-surface-alt);
        padding: 1px 4px;
        border-radius: var(--uui-border-radius);
      }

      .context-kind,
      .context-type {
        color: var(--uui-color-text-alt);
        font-size: 0.85rem;
      }

      .context-arrow {
        color: var(--uui-color-text-alt);
      }

      .context-sentence,
      .context-accepts {
        color: var(--uui-color-text-alt);
        margin: 0;
        font-size: 0.9rem;
      }

      .string-list-notice {
        color: var(--uui-color-text-alt);
        margin: 0;
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

      .fan-out-banner {
        display: flex;
        align-items: center;
        gap: var(--uui-size-space-3);
        border: 1px solid var(--uui-color-warning-standalone, var(--uui-color-border));
        background: var(--uui-color-warning, var(--uui-color-surface-alt));
        color: var(--uui-color-warning-contrast, inherit);
        border-radius: var(--uui-border-radius);
        padding: var(--uui-size-space-3);
        margin-bottom: var(--uui-size-space-4);
      }

      .fan-out-banner.queued {
        border-color: var(--uui-color-positive-standalone, var(--uui-color-border));
        background: var(--uui-color-positive, var(--uui-color-surface-alt));
        color: var(--uui-color-positive-contrast, inherit);
      }

      .fan-out-banner span {
        flex: 1;
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

      .not-mapped-badge,
      .claim-tag {
        font-size: 0.75rem;
      }

      .suggested-hint {
        color: var(--uui-color-text-alt);
        font-size: 0.85rem;
        font-style: italic;
      }

      .map-block-btn {
        margin-left: auto;
      }

      .schema-type-input,
      .schema-type-select {
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

      .show-all-toggle {
        margin-top: var(--uui-size-space-2);
        width: 100%;
        --uui-button-font-weight: normal;
      }

      .value-cell {
        display: flex;
        align-items: center;
        gap: var(--uui-size-space-2);
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
