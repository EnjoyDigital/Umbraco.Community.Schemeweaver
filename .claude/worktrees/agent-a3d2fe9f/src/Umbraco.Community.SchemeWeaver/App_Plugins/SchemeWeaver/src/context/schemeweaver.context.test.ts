import { expect } from '@open-wc/testing';
import { SchemeWeaverContext } from './schemeweaver.context.js';

describe('SchemeWeaverContext', () => {
  it('can be instantiated with a host', () => {
    const host = document.createElement('div') as any;
    const context = new SchemeWeaverContext(host);
    expect(context).to.be.instanceOf(SchemeWeaverContext);
  });

  it('exposes schemaTypes observable', () => {
    const host = document.createElement('div') as any;
    const context = new SchemeWeaverContext(host);
    expect(context.schemaTypes).to.exist;
  });

  it('exposes contentTypes observable', () => {
    const host = document.createElement('div') as any;
    const context = new SchemeWeaverContext(host);
    expect(context.contentTypes).to.exist;
  });

  it('exposes mappings observable', () => {
    const host = document.createElement('div') as any;
    const context = new SchemeWeaverContext(host);
    expect(context.mappings).to.exist;
  });

  it('exposes currentMapping observable', () => {
    const host = document.createElement('div') as any;
    const context = new SchemeWeaverContext(host);
    expect(context.currentMapping).to.exist;
  });

  it('exposes preview observable', () => {
    const host = document.createElement('div') as any;
    const context = new SchemeWeaverContext(host);
    expect(context.preview).to.exist;
  });

  it('exposes loading observable', () => {
    const host = document.createElement('div') as any;
    const context = new SchemeWeaverContext(host);
    expect(context.loading).to.exist;
  });

  it('has loadSchemaTypes method', () => {
    const host = document.createElement('div') as any;
    const context = new SchemeWeaverContext(host);
    expect(typeof context.loadSchemaTypes).to.equal('function');
  });

  it('has loadContentTypes method', () => {
    const host = document.createElement('div') as any;
    const context = new SchemeWeaverContext(host);
    expect(typeof context.loadContentTypes).to.equal('function');
  });

  it('has loadMappings method', () => {
    const host = document.createElement('div') as any;
    const context = new SchemeWeaverContext(host);
    expect(typeof context.loadMappings).to.equal('function');
  });

  it('has saveMapping method', () => {
    const host = document.createElement('div') as any;
    const context = new SchemeWeaverContext(host);
    expect(typeof context.saveMapping).to.equal('function');
  });

  it('has deleteMapping method', () => {
    const host = document.createElement('div') as any;
    const context = new SchemeWeaverContext(host);
    expect(typeof context.deleteMapping).to.equal('function');
  });

  it('has autoMap method', () => {
    const host = document.createElement('div') as any;
    const context = new SchemeWeaverContext(host);
    expect(typeof context.autoMap).to.equal('function');
  });
});
