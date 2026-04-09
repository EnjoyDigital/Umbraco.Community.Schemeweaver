import { expect } from '@open-wc/testing';
import {
  SCHEMA_PRIMITIVE_TYPES,
  isPrimitiveSchemaType,
  filterOutPrimitiveSchemaTypes,
} from './schema-primitives.js';

describe('schema-primitives', () => {
  describe('SCHEMA_PRIMITIVE_TYPES', () => {
    it('contains every primitive currently defined in C# SchemaAutoMapper', () => {
      // If this test fails, check C# SchemaAutoMapper.GetFirstNonPrimitiveAcceptedType
      // and keep the two lists in sync.
      ['text', 'number', 'boolean', 'date', 'datetime', 'time', 'url', 'integer', 'float', 'duration']
        .forEach((t) => expect(SCHEMA_PRIMITIVE_TYPES.has(t)).to.equal(true, `missing: ${t}`));
    });

    it('has exactly 10 primitives — bump this number deliberately when adding one', () => {
      // Guard against silent drift. When you legitimately add a primitive,
      // update BOTH this count AND the list in the test above.
      expect(SCHEMA_PRIMITIVE_TYPES.size).to.equal(10);
    });
  });

  describe('isPrimitiveSchemaType', () => {
    it('returns true for primitive types regardless of case', () => {
      expect(isPrimitiveSchemaType('Text')).to.equal(true);
      expect(isPrimitiveSchemaType('text')).to.equal(true);
      expect(isPrimitiveSchemaType('URL')).to.equal(true);
      expect(isPrimitiveSchemaType('Number')).to.equal(true);
      expect(isPrimitiveSchemaType('DateTime')).to.equal(true);
    });

    it('returns false for complex Schema.org types', () => {
      expect(isPrimitiveSchemaType('Thing')).to.equal(false);
      expect(isPrimitiveSchemaType('Person')).to.equal(false);
      expect(isPrimitiveSchemaType('PhysicalActivityCategory')).to.equal(false);
    });

    it('returns false for null, undefined, and empty strings', () => {
      expect(isPrimitiveSchemaType(null)).to.equal(false);
      expect(isPrimitiveSchemaType(undefined)).to.equal(false);
      expect(isPrimitiveSchemaType('')).to.equal(false);
    });
  });

  describe('filterOutPrimitiveSchemaTypes', () => {
    it('removes primitive types while preserving complex ones', () => {
      const result = filterOutPrimitiveSchemaTypes([
        'Text', 'Thing', 'URL', 'PhysicalActivityCategory',
      ]);
      expect(result).to.deep.equal(['Thing', 'PhysicalActivityCategory']);
    });

    it('returns an empty array when all types are primitive', () => {
      expect(filterOutPrimitiveSchemaTypes(['Text', 'URL'])).to.deep.equal([]);
    });

    it('returns an empty array when given an empty array', () => {
      expect(filterOutPrimitiveSchemaTypes([])).to.deep.equal([]);
    });

    it('preserves order of complex types', () => {
      const result = filterOutPrimitiveSchemaTypes([
        'CategoryCode', 'Text', 'Thing', 'URL', 'PhysicalActivityCategory',
      ]);
      expect(result).to.deep.equal(['CategoryCode', 'Thing', 'PhysicalActivityCategory']);
    });
  });
});
