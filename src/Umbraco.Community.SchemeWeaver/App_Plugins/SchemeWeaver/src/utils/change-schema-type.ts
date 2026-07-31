import { html, nothing } from '@umbraco-cms/backoffice/external/lit';
import { UMB_CONFIRM_MODAL, UMB_MODAL_MANAGER_CONTEXT } from '@umbraco-cms/backoffice/modal';
import type { UmbControllerHost } from '@umbraco-cms/backoffice/controller-api';
import type { PropertyMappingRow } from '../components/property-mapping-table.element.js';
import type { RankedSchemaPropertyInfo } from '../api/types.js';
import { SCHEMEWEAVER_SCHEMA_PICKER_MODAL } from '../modals/schema-picker-modal.token.js';
import { isRowConfigured, reconcileRowsForSchemaType } from './mapping-converters.js';
import { SourceType, sourceTypeLabelKey } from '../constants/source-type.js';

/** Beyond this many removals the dialog summarises the rest rather than growing off-screen. */
const DROPPED_LIST_CAP = 10;

/** Structural shape of the localisation controllers both call sites already own. */
interface LocalizeLike {
  term(key: string, ...args: unknown[]): string;
}

export interface ChangeSchemaTypeArgs {
  /** Controller host the modals are opened against. */
  host: UmbControllerHost;
  modalManager: typeof UMB_MODAL_MANAGER_CONTEXT.TYPE;
  localize: LocalizeLike;
  contentTypeAlias: string;
  /** The type the mapping is on today — pre-selected in the picker. */
  currentSchemaType: string;
  /** The mapping's current rows, in whatever state the caller holds them. */
  rows: PropertyMappingRow[];
  /** Ranked properties of the chosen type (`?ranked=true`, so rows keep their display rank). */
  requestSchemaTypeProperties: (schemaTypeName: string) => Promise<RankedSchemaPropertyInfo[] | undefined>;
}

export interface ChangeSchemaTypeResult {
  schemaTypeName: string;
  /** The new type's properties — callers swap these in so the table re-derives affordances. */
  schemaProperties: RankedSchemaPropertyInfo[];
  /** Reconciled rows to persist, already in display order. */
  rows: PropertyMappingRow[];
  /** Configured rows that could not carry over, as confirmed by the user. */
  droppedConfigured: PropertyMappingRow[];
}

/**
 * The one change-the-mapped-type flow, shared by the Schema.org workspace view
 * and the "Map to Schema.org" entity action: pick a type, work out what carries
 * over, and confirm the losses before the caller writes anything.
 *
 * Returns `null` whenever nothing should happen — the user cancelled at either
 * step, or picked the type the mapping is already on. Throws if the chosen type's
 * properties can't be loaded, because reconciling against an empty property set
 * would silently discard every row; callers surface that through their own error
 * notification.
 */
export async function changeSchemaType(args: ChangeSchemaTypeArgs): Promise<ChangeSchemaTypeResult | null> {
  const { host, modalManager, localize, contentTypeAlias, currentSchemaType, rows } = args;

  const picked = await modalManager
    .open(host, SCHEMEWEAVER_SCHEMA_PICKER_MODAL, {
      data: { contentTypeAlias, currentSchemaType },
    })
    .onSubmit()
    .catch(() => null);

  const schemaTypeName = picked?.schemaType;
  if (!schemaTypeName) return null;
  if (schemaTypeName.toLowerCase() === currentSchemaType.toLowerCase()) return null;

  const schemaProperties = await args.requestSchemaTypeProperties(schemaTypeName);
  if (!schemaProperties?.length) {
    throw new Error(localize.term('schemeWeaver_changeSchemaTypeLoadFailed', schemaTypeName));
  }

  const { kept, droppedConfigured } = reconcileRowsForSchemaType(rows, schemaProperties);

  // Counts describe configured rows only — unconfigured placeholders are not the
  // user's work, and quoting them would overstate both sides of the trade.
  const configuredTotal = rows.filter(isRowConfigured).length;
  const keptConfigured = kept.filter(isRowConfigured).length;

  const confirmed = await modalManager
    .open(host, UMB_CONFIRM_MODAL, {
      data: {
        headline: localize.term('schemeWeaver_changeSchemaType'),
        color: droppedConfigured.length > 0 ? 'warning' : 'positive',
        confirmLabel: localize.term('schemeWeaver_change'),
        content: html`
          <p>${localize.term('schemeWeaver_changeSchemaTypeIntro', currentSchemaType, schemaTypeName)}</p>
          <p>${localize.term('schemeWeaver_changeSchemaTypeKept', keptConfigured, configuredTotal)}</p>
          ${droppedConfigured.length > 0
            ? html`
                <p>${localize.term('schemeWeaver_changeSchemaTypeDropped', schemaTypeName)}</p>
                <ul>
                  ${droppedConfigured.slice(0, DROPPED_LIST_CAP).map(
                    (row) => html`<li><strong>${row.schemaPropertyName}</strong> — ${describeRowSource(row, localize)}</li>`,
                  )}
                </ul>
                ${droppedConfigured.length > DROPPED_LIST_CAP
                  ? html`<p>${localize.term(
                      'schemeWeaver_changeSchemaTypeDroppedMore',
                      droppedConfigured.length - DROPPED_LIST_CAP,
                    )}</p>`
                  : nothing}
              `
            : nothing}
        `,
      },
    })
    .onSubmit()
    .then(() => true)
    .catch(() => false);

  if (!confirmed) return null;

  return { schemaTypeName, schemaProperties, rows: kept, droppedConfigured };
}

/**
 * Short "what would be lost" description for a dropped row — the bound property
 * alias where there is one, otherwise whatever else identifies the row's source.
 * Long static values are clipped so one verbose row can't swamp the dialog, and
 * the fallback is the source type's LABEL: a configured complexType row carries
 * only a resolverConfig, and rendering its raw wire value ("complexType") at the
 * user would be meaningless.
 */
function describeRowSource(row: PropertyMappingRow, localize: LocalizeLike): string {
  if (row.contentTypePropertyAlias) return row.contentTypePropertyAlias;
  if (row.sourceType === SourceType.Static && row.staticValue) {
    const value = row.staticValue;
    return value.length > 40 ? `"${value.slice(0, 40)}…"` : `"${value}"`;
  }
  if (row.sourceType === SourceType.Reference && row.targetPieceKey) return row.targetPieceKey;
  return localize.term(sourceTypeLabelKey(row.sourceType));
}
