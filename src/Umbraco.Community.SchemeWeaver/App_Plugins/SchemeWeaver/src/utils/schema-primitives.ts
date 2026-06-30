/**
 * Schema.org "primitive" data types — value types with no sub-properties to map.
 * Mirrors C# SchemaAutoMapper.GetFirstNonPrimitiveAcceptedType.
 * Keep in sync with: src/Umbraco.Community.SchemeWeaver/Services/SchemaAutoMapper.cs
 *
 * Values are lowercased for case-insensitive matching — prefer the
 * `isPrimitiveSchemaType` helper over calling `.has()` directly, otherwise
 * mixed-case inputs like `'Text'` will return `false`.
 */
export const SCHEMA_PRIMITIVE_TYPES: ReadonlySet<string> = new Set([
  'text', 'number', 'boolean', 'date', 'datetime',
  'time', 'url', 'integer', 'float', 'duration',
]);

export function isPrimitiveSchemaType(typeName: string | null | undefined): boolean {
  if (!typeName) return false;
  return SCHEMA_PRIMITIVE_TYPES.has(typeName.toLowerCase());
}

export function filterOutPrimitiveSchemaTypes(types: readonly string[]): string[] {
  return types.filter((t) => !isPrimitiveSchemaType(t));
}

/**
 * Primitive type names as spelled in a property's `acceptedTypes` — these come from Schema.NET
 * and use CLR-ish names (`String`, `Uri`, `TimeSpan`, `Int32`…) rather than the Schema.org
 * tokens in {@link SCHEMA_PRIMITIVE_TYPES} (`text`, `url`…). Kept separate so the C#-mirrored
 * set above stays untouched. Used to filter a type picker down to object types only.
 */
const SCHEMA_ACCEPTED_PRIMITIVE_TYPES: ReadonlySet<string> = new Set([
  ...SCHEMA_PRIMITIVE_TYPES,
  'string', 'uri', 'timespan', 'double', 'int32', 'datetimeoffset',
]);

/** Filter `acceptedTypes` down to object types, dropping CLR/Schema.org primitive value types. */
export function filterOutPrimitiveAcceptedTypes(types: readonly string[]): string[] {
  return types.filter((t) => !!t && !SCHEMA_ACCEPTED_PRIMITIVE_TYPES.has(t.toLowerCase()));
}
