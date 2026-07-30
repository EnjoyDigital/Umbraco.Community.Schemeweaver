/**
 * Announces that a content type's schema mapping was changed from OUTSIDE the
 * Schema.org workspace view — currently the "Map to Schema.org" entity action.
 *
 * Deliberately not `UmbRequestReloadStructureForEntityEvent`: the view treats
 * that event as "the document type was saved" and responds by saving its own
 * rows, so reusing it here would have an open tab write its now-stale mapping
 * straight back over the change. This says "re-read", not "write".
 */
export class SchemeWeaverMappingChangedEvent extends Event {
  static readonly TYPE = 'schemeweaver:mapping-changed';

  #unique: string;

  constructor(unique: string) {
    super(SchemeWeaverMappingChangedEvent.TYPE);
    this.#unique = unique;
  }

  /** The document type key the mapping belongs to. */
  getUnique(): string {
    return this.#unique;
  }
}
