import { UmbLitElement } from '@umbraco-cms/backoffice/lit-element';
import { css, html, customElement, state } from '@umbraco-cms/backoffice/external/lit';
import { UmbTextStyles } from '@umbraco-cms/backoffice/style';
import { UMB_DOCUMENT_TYPE_WORKSPACE_CONTEXT } from '@umbraco-cms/backoffice/document-type';
import { UMB_MODAL_MANAGER_CONTEXT } from '@umbraco-cms/backoffice/modal';
import { UMB_NOTIFICATION_CONTEXT } from '@umbraco-cms/backoffice/notification';
import { UMB_ACTION_EVENT_CONTEXT } from '@umbraco-cms/backoffice/action';
import { UmbRequestReloadStructureForEntityEvent } from '@umbraco-cms/backoffice/entity-action';
import type { PropertyMappingRow } from '../components/property-mapping-table.element.js';
import '../components/property-mapping-table.element.js';
import type { SchemeWeaverContext } from '../context/schemeweaver.context.js';
import { SCHEMEWEAVER_CONTEXT } from '../context/schemeweaver.context-token.js';
import { SCHEMEWEAVER_SCHEMA_PICKER_MODAL } from '../modals/schema-picker-modal.token.js';
import { SCHEMEWEAVER_PROPERTY_MAPPING_MODAL } from '../modals/property-mapping-modal.token.js';
import { SCHEMEWEAVER_SOURCE_ORIGIN_PICKER_MODAL } from '../modals/source-origin-picker-modal.token.js';
import { SCHEMEWEAVER_NESTED_MAPPING_MODAL } from '../modals/nested-mapping-modal.token.js';
import type { NestedMappingModalValue, NestedMappingModalSiblingClaim } from '../modals/nested-mapping-modal.token.js';
import { parseResolverConfig, legacyConfigToRoutes } from '../components/block-route-model.js';
import { SCHEMEWEAVER_COMPLEX_TYPE_MAPPING_MODAL } from '../modals/complex-type-mapping-modal.token.js';
import type { SchemaMappingDto, ContentTypeProperty, RankedSchemaPropertyInfo } from '../api/types.js';
import { SourceType } from '../constants/source-type.js';

import { dtoToRow, mergeAutoMapSuggestions, sortMappingRows, rowsToPropertyMappingDtos, applySourceTypeChange, applyWarningsToRows } from '../utils/mapping-converters.js';
import { changeSchemaType } from '../utils/change-schema-type.js';
import { SchemeWeaverMappingChangedEvent } from '../utils/mapping-changed-event.js';

@customElement('schemeweaver-schema-mapping-view')
export class SchemaMappingViewElement extends UmbLitElement {
  // The shared SchemeWeaverContext is provided at the app root by the
  // backoffice entry point (see src/entry-point.ts) — consume it rather than
  // build another per-view instance.
  #context?: SchemeWeaverContext;
  #notificationContext?: typeof UMB_NOTIFICATION_CONTEXT.TYPE;
  #modalManagerContext?: typeof UMB_MODAL_MANAGER_CONTEXT.TYPE;
  #actionEventContext?: typeof UMB_ACTION_EVENT_CONTEXT.TYPE;

  @state()
  private _loading = true;

  @state()
  private _mapping: SchemaMappingDto | null = null;

  @state()
  private _rows: PropertyMappingRow[] = [];

  @state()
  private _availableProperties: string[] = [];

  @state()
  private _allSchemaProperties: RankedSchemaPropertyInfo[] = [];

  @state()
  private _contentTypeAlias = '';

  @state()
  private _contentTypeKey = '';

  /** Guards the change-type flow against a second click stacking a second picker. */
  @state()
  private _changingSchemaType = false;

  constructor() {
    super();
    this.consumeContext(SCHEMEWEAVER_CONTEXT, (context) => {
      this.#context = context;
    });
    this.consumeContext(UMB_NOTIFICATION_CONTEXT, (context) => {
      this.#notificationContext = context;
    });
    this.consumeContext(UMB_MODAL_MANAGER_CONTEXT, (context) => {
      this.#modalManagerContext = context;
    });

    // The action event context is provided once at the backoffice root, so it
    // outlives every workspace view. Listeners must therefore be removed before
    // being re-added and torn down on destroy — otherwise each visit to a
    // Schema.org tab leaves another detached view listening, and one document
    // type save fires a POST from every one of them.
    this.consumeContext(UMB_ACTION_EVENT_CONTEXT, (context) => {
      this.#actionEventContext?.removeEventListener(
        UmbRequestReloadStructureForEntityEvent.TYPE, this.#onReloadStructure);
      this.#actionEventContext?.removeEventListener(
        SchemeWeaverMappingChangedEvent.TYPE, this.#onMappingChanged);

      this.#actionEventContext = context;

      context?.addEventListener(UmbRequestReloadStructureForEntityEvent.TYPE, this.#onReloadStructure);
      context?.addEventListener(SchemeWeaverMappingChangedEvent.TYPE, this.#onMappingChanged);
    });
  }

  /**
   * Whether an event about entity `unique` concerns the document type this view
   * is showing. Compared case-insensitively: a `unique` arrives in whatever
   * casing its source used, and the workspace context and an entity action's
   * args need not agree — a mismatch would silently stop the tab refreshing.
   */
  #isThisContentType(unique: string | undefined): boolean {
    if (!this._contentTypeKey || !unique) return false;
    return unique.toLowerCase() === this._contentTypeKey.toLowerCase();
  }

  /**
   * Auto-save the mapping when *this* document type is saved. The reload event
   * is emitted for every entity in the backoffice, hence the scoping.
   */
  #onReloadStructure = (event: Event) => {
    const reloadEvent = event as UmbRequestReloadStructureForEntityEvent;
    if (!this.#isThisContentType(reloadEvent.getUnique?.())) return;
    if (this._mapping && this._rows.length > 0) {
      this._handleSave();
    }
  };

  /**
   * A mapping changed elsewhere (an entity action) — re-read rather than save,
   * or this view would write its stale rows over that change.
   */
  #onMappingChanged = (event: Event) => {
    const changedEvent = event as SchemeWeaverMappingChangedEvent;
    if (!this.#isThisContentType(changedEvent.getUnique?.())) return;
    this._fetchMapping();
  };

  override destroy(): void {
    this.#actionEventContext?.removeEventListener(
      UmbRequestReloadStructureForEntityEvent.TYPE, this.#onReloadStructure);
    this.#actionEventContext?.removeEventListener(
      SchemeWeaverMappingChangedEvent.TYPE, this.#onMappingChanged);
    this.#actionEventContext = undefined;
    super.destroy();
  }

  async connectedCallback() {
    super.connectedCallback();

    try {
      const workspaceContext = await this.getContext(UMB_DOCUMENT_TYPE_WORKSPACE_CONTEXT);
      if (!workspaceContext) {
        // Workspace view rendered outside its expected document-type workspace.
        // In production this should never happen because the workspaceView
        // condition gates it. Leave the loader in place rather than revealing
        // an empty state to the user.
        return;
      }

      this.observe(
        workspaceContext.alias,
        (alias) => {
          if (alias) {
            this._contentTypeAlias = alias;
            this._fetchMapping();
          }
        },
        '_observeAlias',
      );

      const unique = workspaceContext.getUnique();
      if (unique) {
        this._contentTypeKey = unique;
      }
    } catch {
      // Workspace context wasn't available (e.g., element rendered outside its
      // expected workspace, or running under a test fixture). Surface a warning
      // notification but keep `_loading` true so the user sees a spinner rather
      // than an empty state — the workspaceView condition guarantees this only
      // ever runs inside a real document-type workspace in production.
      this.#notificationContext?.peek('warning', {
        data: { message: this.localize.term('schemeWeaver_workspaceContextUnavailable') },
      });
    }
  }

  private async _fetchMapping() {
    this._loading = true;

    try {
      const mapping = await this.#context?.requestMapping(this._contentTypeAlias);

      if (!mapping) {
        this._mapping = null;
        this._rows = [];
        this._loading = false;
        return;
      }

      this._mapping = mapping;
      this._rows = sortMappingRows(mapping.propertyMappings.map(dtoToRow));

      // Enrich rows with schema property info (acceptedTypes, isComplexType, recommendation rank).
      // ranked=true gives confidence/isPopular so the table orders recommended properties first.
      const schemaProps = await this.#context?.requestSchemaTypeProperties(mapping.schemaTypeName, true);
      if (schemaProps) {
        this._allSchemaProperties = schemaProps;
        this._rows = this._rows.map(row => {
          const schemaProp = schemaProps.find(
            (sp: RankedSchemaPropertyInfo) => sp.name.toLowerCase() === row.schemaPropertyName.toLowerCase()
          );
          if (schemaProp) {
            const enriched = {
              ...row,
              schemaPropertyType: schemaProp.propertyType || row.schemaPropertyType,
              acceptedTypes: schemaProp.acceptedTypes || [],
              isComplexType: schemaProp.isComplexType || false,
              schemaRank: schemaProp.confidence,
            };
            // Restore sub-mappings from saved resolverConfig for complexType rows
            if (enriched.sourceType === SourceType.ComplexType && enriched.resolverConfig) {
              try {
                const config = JSON.parse(enriched.resolverConfig);
                if (config.complexTypeMappings?.length) {
                  enriched.selectedSubType = enriched.nestedSchemaTypeName || enriched.acceptedTypes[0] || '';
                  enriched.subMappings = config.complexTypeMappings.map((m: Record<string, string>) => ({
                    schemaProperty: m.schemaProperty || '',
                    schemaPropertyType: '',
                    sourceType: (m.sourceType as typeof SourceType[keyof typeof SourceType]) || SourceType.Property,
                    contentTypePropertyAlias: m.contentTypePropertyAlias || '',
                    staticValue: m.staticValue || '',
                  }));
                }
              } catch { /* ignore parse errors */ }
            }
            return enriched;
          }
          return row;
        });
        this._rows = sortMappingRows(this._rows);
      }

      // Server-authoritative range warnings, keyed back to rows by schema
      // property name. Refreshed on every fetch — including the re-fetch after
      // save — so badges stay in sync with the persisted mapping.
      this._rows = applyWarningsToRows(this._rows, mapping.warnings);

      const props = await this.#context?.requestContentTypeProperties(this._contentTypeAlias);
      if (props) {
        this._availableProperties = props.map((p: ContentTypeProperty) => p.alias);
        // Enrich rows with each property's editor alias — dtoToRow can't know it,
        // and the picked-item mode block only renders for picker editors.
        const editorByAlias = new Map(props.map((p: ContentTypeProperty) => [p.alias, p.editorAlias ?? '']));
        this._rows = this._rows.map((row) =>
          !row.editorAlias && row.contentTypePropertyAlias && editorByAlias.has(row.contentTypePropertyAlias)
            ? { ...row, editorAlias: editorByAlias.get(row.contentTypePropertyAlias)! }
            : row,
        );
      }

      // Hydrate the drill-down dropdown for stored picker rows that carry a
      // pickedContentTypeAlias hint (mirrors the ancestor hydration below).
      const pickedAliases = [...new Set(
        this._rows
          .filter((r) => r.pickedContentTypeAlias)
          .map((r) => r.pickedContentTypeAlias!)
      )];
      if (pickedAliases.length > 0) {
        const pickedPropsMap = new Map<string, string[]>();
        await Promise.all(
          pickedAliases.map(async (alias) => {
            const pickedProps = await this.#context?.requestContentTypeProperties(alias);
            if (pickedProps) {
              pickedPropsMap.set(alias, pickedProps.map((p) => p.alias));
            }
          })
        );
        const contentTypesForPicked = await this.#context?.requestContentTypes();
        this._rows = this._rows.map((row) => {
          if (row.pickedContentTypeAlias && pickedPropsMap.has(row.pickedContentTypeAlias)) {
            const ctMatch = contentTypesForPicked?.find((ct) => ct.alias === row.pickedContentTypeAlias);
            return {
              ...row,
              pickedContentTypeProperties: pickedPropsMap.get(row.pickedContentTypeAlias)!,
              pickedContentTypeUnique: ctMatch?.key,
            };
          }
          return row;
        });
      }

      // Fetch properties for any existing parent/ancestor/sibling source content types
      const sourceAliases = [...new Set(
        this._rows
          .filter((r) => r.sourceContentTypeAlias && [SourceType.Parent, SourceType.Ancestor, SourceType.Sibling].includes(r.sourceType))
          .map((r) => r.sourceContentTypeAlias)
      )];

      if (sourceAliases.length > 0) {
        const sourcePropsMap = new Map<string, string[]>();
        await Promise.all(
          sourceAliases.map(async (alias) => {
            const sourceProps = await this.#context?.requestContentTypeProperties(alias);
            if (sourceProps) {
              sourcePropsMap.set(alias, sourceProps.map((p) => p.alias));
            }
          })
        );

        // Fetch content types to reconstruct sourceDocumentTypeUnique from alias
        const contentTypes = await this.#context?.requestContentTypes();

        this._rows = this._rows.map((row) => {
          if (row.sourceContentTypeAlias && sourcePropsMap.has(row.sourceContentTypeAlias)) {
            const ctMatch = contentTypes?.find((ct) => ct.alias === row.sourceContentTypeAlias);
            return {
              ...row,
              sourceContentTypeProperties: sourcePropsMap.get(row.sourceContentTypeAlias)!,
              sourceDocumentTypeUnique: ctMatch?.key,
            };
          }
          return row;
        });
      }
    } catch (error) {
      this.#notificationContext?.peek('danger', {
        data: {
          message: error instanceof Error ? error.message : this.localize.term('schemeWeaver_failedToLoadMapping'),
        },
      });
    } finally {
      this._loading = false;
    }
  }

  private async _handleMapToSchema() {
    if (!this.#modalManagerContext) return;

    const pickerResult = await this.#modalManagerContext
      .open(this, SCHEMEWEAVER_SCHEMA_PICKER_MODAL, {
        data: { contentTypeAlias: this._contentTypeAlias },
      })
      .onSubmit()
      .catch(() => null);

    if (!pickerResult?.schemaType) return;

    const mappingResult = await this.#modalManagerContext
      .open(this, SCHEMEWEAVER_PROPERTY_MAPPING_MODAL, {
        data: {
          contentTypeAlias: this._contentTypeAlias,
          schemaType: pickerResult.schemaType,
          contentTypeKey: this._contentTypeKey,
        },
      })
      .onSubmit()
      .catch(() => null);

    if (mappingResult?.saved) {
      await this._fetchMapping();
    }
  }

  /**
   * Re-point this mapping at a different Schema.org type, keeping every property
   * mapping the new type still accepts (issue #41). Persists on confirmation
   * rather than waiting for the document type to be saved: the auto-save listener
   * above only fires when rows remain, so a change that dropped them all would
   * otherwise never reach the database.
   */
  private async _handleChangeSchemaType() {
    // Without this, a second click stacks a second picker; both flows capture
    // the same pre-change rows, and confirming both would have the second
    // reconcile stale rows over the first result.
    if (!this.#modalManagerContext || !this._mapping || this._changingSchemaType) return;
    this._changingSchemaType = true;

    try {
      const result = await changeSchemaType({
        host: this,
        modalManager: this.#modalManagerContext,
        localize: this.localize,
        contentTypeAlias: this._contentTypeAlias,
        currentSchemaType: this._mapping.schemaTypeName,
        rows: this._rows,
        requestSchemaTypeProperties: async (name) => this.#context?.requestSchemaTypeProperties(name, true),
      });

      if (!result) return;

      this._mapping = { ...this._mapping, schemaTypeName: result.schemaTypeName };
      this._allSchemaProperties = result.schemaProperties;
      this._rows = result.rows;

      await this._handleSave(this.localize.term('schemeWeaver_schemaTypeChanged', result.schemaTypeName));
    } catch (error) {
      this.#notificationContext?.peek('danger', {
        data: {
          message: error instanceof Error ? error.message : this.localize.term('schemeWeaver_failedToSave'),
        },
      });
    } finally {
      this._changingSchemaType = false;
    }
  }

  private async _handleAutoMap() {
    if (!this._contentTypeAlias || !this._mapping?.schemaTypeName) return;

    this._loading = true;
    try {
      const suggestions = await this.#context?.autoMap(
        this._contentTypeAlias,
        this._mapping.schemaTypeName
      );

      if (suggestions && Array.isArray(suggestions)) {
        this._rows = mergeAutoMapSuggestions(this._rows, suggestions);
      }
    } catch (error) {
      this.#notificationContext?.peek('danger', {
        data: {
          message: error instanceof Error ? error.message : this.localize.term('schemeWeaver_autoMapFailed'),
        },
      });
    } finally {
      this._loading = false;
    }
  }

  private async _handleSave(successMessage?: string) {
    if (!this._mapping) return;

    try {
      const dto: SchemaMappingDto = {
        ...this._mapping,
        contentTypeKey: this._contentTypeKey || this._mapping.contentTypeKey,
        idOverride: this._mapping.idOverride ?? null,
        propertyMappings: rowsToPropertyMappingDtos(this._rows),
      };
      await this.#context?.saveMapping(dto);
      this.#notificationContext?.peek('positive', {
        data: { message: successMessage ?? this.localize.term('schemeWeaver_mappingSaved') },
      });
      await this._fetchMapping();
    } catch (error) {
      this.#notificationContext?.peek('danger', {
        data: {
          message: error instanceof Error ? error.message : this.localize.term('schemeWeaver_failedToSave'),
        },
      });
      // Re-read so the view stops asserting state that was never persisted. The
      // change-type flow mutates `_mapping`/`_rows` optimistically before saving,
      // so leaving them in place would show a type the database does not hold —
      // and the next document type save would quietly persist it.
      await this._fetchMapping();
    }
  }

  private _handleMappingsChanged(e: CustomEvent) {
    this._rows = e.detail.mappings;
  }

  private _handleInheritedToggle(e: Event) {
    if (!this._mapping) return;
    this._mapping = {
      ...this._mapping,
      isInherited: (e.target as HTMLInputElement).checked,
    };
  }

  private _handleIdOverrideInput(e: Event) {
    if (!this._mapping) return;
    const raw = (e.target as HTMLInputElement).value;
    this._mapping = {
      ...this._mapping,
      idOverride: raw.trim() === '' ? null : raw,
    };
  }

  private async _handleResolveDocumentType(e: CustomEvent) {
    const { index, documentTypeUnique } = e.detail;
    if (!documentTypeUnique) return;

    // Look up the content type by its unique key to get the alias
    const contentTypes = await this.#context?.requestContentTypes();
    const match = contentTypes?.find((ct) => ct.key === documentTypeUnique);
    if (!match) return;

    const props = await this.#context?.requestContentTypeProperties(match.alias);
    const propertyAliases = props?.map((p) => p.alias) || [];

    const updated = [...this._rows];
    updated[index] = {
      ...updated[index],
      sourceContentTypeAlias: match.alias,
      sourceContentTypeProperties: propertyAliases,
      contentTypePropertyAlias: '',
    };
    this._rows = updated;
  }

  /**
   * Picker drill-down: the user browsed a document type to list the picked
   * item's properties. Stores the alias hint into the row (and via the next
   * picked-property selection, into resolverConfig).
   */
  private async _handleResolvePickedDocumentType(e: CustomEvent) {
    const { index, documentTypeUnique } = e.detail;
    if (!documentTypeUnique) return;

    const contentTypes = await this.#context?.requestContentTypes();
    const match = contentTypes?.find((ct) => ct.key === documentTypeUnique);
    if (!match) return;

    const props = await this.#context?.requestContentTypeProperties(match.alias);
    const propertyAliases = props?.map((p) => p.alias) || [];

    const updated = [...this._rows];
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
    this._rows = updated;
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

    const updated = [...this._rows];
    updated[index] = applySourceTypeChange(updated[index], result.sourceType);
    this._rows = updated;
  }

  private async _handleConfigureNestedMapping(e: CustomEvent) {
    const { index } = e.detail;
    const mapping = this._rows[index];

    // The panel is scoped to THIS row: it maps the block-list property's element
    // types INTO this row's schema property. It needs a chosen property alias.
    if (!mapping || !mapping.contentTypePropertyAlias) {
      this.#notificationContext?.peek('warning', {
        data: { message: this.localize.term('schemeWeaver_pleaseSelectBlockContentProperty') },
      });
      return;
    }

    if (!this.#modalManagerContext) return;

    const blockListProp = mapping.contentTypePropertyAlias;

    const modalHandler = this.#modalManagerContext.open(this, SCHEMEWEAVER_NESTED_MAPPING_MODAL, {
      data: {
        contentTypeAlias: this._contentTypeAlias,
        contentTypePropertyAlias: blockListProp,
        schemaPropertyName: mapping.schemaPropertyName,
        schemaPropertyType: mapping.schemaPropertyType || undefined,
        acceptedTypes: mapping.acceptedTypes,
        existingConfig: mapping.resolverConfig ?? null,
        nestedSchemaTypeName: mapping.nestedSchemaTypeName || null,
        siblingClaims: this._computeSiblingClaims(index, blockListProp),
      },
    });

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
    this._rows.forEach((row, i) => {
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
   * opened row's `resolverConfig`; a verbatim-unchanged config touches NOTHING
   * (so `isAutoMapped`/confidence stay stable). Explicit fan-out entries merge
   * into an existing sibling row or append a new one. No row is ever deleted or
   * re-keyed.
   */
  private _applyNestedMappingResult(index: number, editorAlias: string, value: NestedMappingModalValue) {
    const opened = this._rows[index];
    const blockListProp = opened.contentTypePropertyAlias;
    const rows = [...this._rows];

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

    this._rows = sortMappingRows(rows);
  }

  private async _handleConfigureComplexTypeMapping(e: CustomEvent) {
    const { index, schemaPropertyName, acceptedTypes, selectedSubType, resolverConfig } = e.detail;
    if (!this.#modalManagerContext) return;

    const modalHandler = this.#modalManagerContext.open(this, SCHEMEWEAVER_COMPLEX_TYPE_MAPPING_MODAL, {
      data: {
        schemaPropertyName,
        acceptedTypes: acceptedTypes || [],
        selectedSubType: selectedSubType || '',
        contentTypeAlias: this._contentTypeAlias,
        availableProperties: this._availableProperties,
        existingConfig: resolverConfig,
      },
    });

    try {
      const result = await modalHandler.onSubmit();
      if (result?.resolverConfig) {
        const updated = [...this._rows];
        updated[index] = {
          ...updated[index],
          resolverConfig: result.resolverConfig,
          selectedSubType: result.selectedSubType,
          nestedSchemaTypeName: result.selectedSubType,
        };
        this._rows = updated;
      }
    } catch {
      // Modal was rejected / closed
    }
  }

  render() {
    // The workspace editor already provides the surrounding umb-body-layout
    // (headline bar, scroll container) — this view renders content only.
    if (this._loading) {
      return html`
        <div id="loader">
          <uui-loader></uui-loader>
        </div>
      `;
    }

    if (!this._mapping) {
      return html`
        <div class="empty-state uui-text">
          <umb-icon name="icon-brackets"></umb-icon>
          <h4>${this.localize.term('schemeWeaver_noMapping')}</h4>
          <p>${this.localize.term('schemeWeaver_noMappingDescription')}</p>
          <uui-button
            look="primary"
            color="positive"
            label=${this.localize.term('schemeWeaver_mapToSchema')}
            data-mark="schemeweaver:map-to-schema"
            @click=${this._handleMapToSchema}></uui-button>
        </div>
      `;
    }

    return html`
      <uui-box headline=${this.localize.term('schemeWeaver_schemaType')}>
        <umb-property-layout label=${this.localize.term('schemeWeaver_schemaType')}>
          <div id="schema-type-badge" slot="editor" data-mark="schemeweaver:schema-type-badge">
            <uui-tag color="primary" look="primary">${this._mapping.schemaTypeName}</uui-tag>
            <code>${this._mapping.contentTypeAlias}</code>
            <uui-button
              look="outline"
              compact
              label=${this.localize.term('schemeWeaver_changeSchemaType')}
              title=${this.localize.term('schemeWeaver_changeSchemaTypeHint')}
              data-mark="schemeweaver:change-schema-type"
              ?disabled=${this._changingSchemaType}
              @click=${this._handleChangeSchemaType}>
              ${this.localize.term('schemeWeaver_change')}
            </uui-button>
          </div>
        </umb-property-layout>

        <umb-property-layout
          label=${this.localize.term('schemeWeaver_inherited')}
          description=${this.localize.term('schemeWeaver_inheritedDescription')}>
          <uui-toggle
            slot="editor"
            label=${this.localize.term('schemeWeaver_inherited')}
            .checked=${this._mapping.isInherited}
            @change=${this._handleInheritedToggle}
            data-mark="schemeweaver:inherited-toggle"></uui-toggle>
        </umb-property-layout>

        <umb-property-layout
          label=${this.localize.term('schemeWeaver_idOverrideLabel')}
          description=${this.localize.term('schemeWeaver_idOverrideHint')}>
          <uui-input
            slot="editor"
            type="text"
            label=${this.localize.term('schemeWeaver_idOverrideLabel')}
            .value=${this._mapping.idOverride ?? ''}
            placeholder="{url}#{type}"
            @input=${this._handleIdOverrideInput}
            data-mark="schemeweaver:id-override"></uui-input>
        </umb-property-layout>
      </uui-box>

      <uui-box headline=${this.localize.term('schemeWeaver_propertyMappings')}>
        <uui-button
          slot="header-actions"
          look="outline"
          compact
          label=${this.localize.term('schemeWeaver_autoMap')}
          data-mark="schemeweaver:auto-map"
          @click=${this._handleAutoMap}></uui-button>

        <schemeweaver-property-mapping-table
          .mappings=${this._rows}
          .availableProperties=${this._availableProperties}
          .allSchemaProperties=${this._allSchemaProperties}
          @mappings-changed=${this._handleMappingsChanged}
          @pick-source-origin=${this._handlePickSourceOrigin}
          @resolve-document-type=${this._handleResolveDocumentType}
          @resolve-picked-document-type=${this._handleResolvePickedDocumentType}
          @configure-nested-mapping=${this._handleConfigureNestedMapping}
          @configure-complex-type-mapping=${this._handleConfigureComplexTypeMapping}
        ></schemeweaver-property-mapping-table>
      </uui-box>
    `;
  }

  static styles = [
    UmbTextStyles,
    css`
      :host {
        display: block;
        padding: var(--uui-size-layout-1);
      }

      /* Delayed fade-in — never flashes on fast loads. */
      #loader {
        display: flex;
        justify-content: center;
        align-items: center;
        height: 50vh;
        opacity: 0;
        animation: fadeIn 240ms 240ms forwards;
      }

      .empty-state {
        display: flex;
        flex-direction: column;
        align-items: center;
        justify-content: center;
        gap: var(--uui-size-space-4);
        min-height: 50vh;
        text-align: center;
        opacity: 0;
        animation: fadeIn 240ms 240ms forwards;
      }

      .empty-state umb-icon {
        font-size: var(--uui-size-12);
        color: var(--uui-color-border-emphasis);
      }

      .empty-state h4,
      .empty-state p {
        margin: 0;
      }

      uui-box + uui-box {
        margin-top: var(--uui-size-layout-1);
      }

      umb-property-layout {
        border-top: 1px solid var(--uui-color-border);
      }
      umb-property-layout:first-child {
        padding-top: 0;
        border: none;
      }

      #schema-type-badge {
        display: flex;
        align-items: center;
        gap: var(--uui-size-space-3);
      }

      #schema-type-badge code {
        font-family: monospace;
        color: var(--uui-color-text-alt);
      }

      /* Trailing edge of the row, away from the type it acts on. */
      #schema-type-badge uui-button {
        margin-left: auto;
      }

      uui-input {
        width: 100%;
      }

      @keyframes fadeIn {
        to {
          opacity: 1;
        }
      }
    `,
  ];
}

export default SchemaMappingViewElement;

declare global {
  interface HTMLElementTagNameMap {
    'schemeweaver-schema-mapping-view': SchemaMappingViewElement;
  }
}
