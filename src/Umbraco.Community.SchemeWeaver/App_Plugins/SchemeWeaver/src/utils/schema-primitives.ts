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
