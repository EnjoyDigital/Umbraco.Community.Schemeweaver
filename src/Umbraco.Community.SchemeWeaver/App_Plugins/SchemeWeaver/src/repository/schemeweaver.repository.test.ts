import { expect } from '@open-wc/testing';
import { SchemeWeaverRepository } from './schemeweaver.repository.js';

describe('SchemeWeaverRepository', () => {
  it('can be instantiated with a host', () => {
    const host = document.createElement('div') as any;
    const repo = new SchemeWeaverRepository(host);
    expect(repo).to.be.instanceOf(SchemeWeaverRepository);
  });

  it('has requestMappings method', () => {
    const host = document.createElement('div') as any;
    const repo = new SchemeWeaverRepository(host);
    expect(typeof repo.requestMappings).to.equal('function');
  });

  it('has requestContentTypes method', () => {
    const host = document.createElement('div') as any;
    const repo = new SchemeWeaverRepository(host);
    expect(typeof repo.requestContentTypes).to.equal('function');
  });

  it('has requestSchemaTypes method', () => {
    const host = document.createElement('div') as any;
    const repo = new SchemeWeaverRepository(host);
    expect(typeof repo.requestSchemaTypes).to.equal('function');
  });

  it('has saveMapping method', () => {
    const host = document.createElement('div') as any;
    const repo = new SchemeWeaverRepository(host);
    expect(typeof repo.saveMapping).to.equal('function');
  });

  it('has deleteMapping method', () => {
    const host = document.createElement('div') as any;
    const repo = new SchemeWeaverRepository(host);
    expect(typeof repo.deleteMapping).to.equal('function');
  });

  it('has requestAutoMap method', () => {
    const host = document.createElement('div') as any;
    const repo = new SchemeWeaverRepository(host);
    expect(typeof repo.requestAutoMap).to.equal('function');
  });

  it('has requestPreview method', () => {
    const host = document.createElement('div') as any;
    const repo = new SchemeWeaverRepository(host);
    expect(typeof repo.requestPreview).to.equal('function');
  });

  it('has generateContentType method', () => {
    const host = document.createElement('div') as any;
    const repo = new SchemeWeaverRepository(host);
    expect(typeof repo.generateContentType).to.equal('function');
  });

  it('has requestContentTypeProperties method', () => {
    const host = document.createElement('div') as any;
    const repo = new SchemeWeaverRepository(host);
    expect(typeof repo.requestContentTypeProperties).to.equal('function');
  });

  it('has requestSchemaTypeProperties method', () => {
    const host = document.createElement('div') as any;
    const repo = new SchemeWeaverRepository(host);
    expect(typeof repo.requestSchemaTypeProperties).to.equal('function');
  });

  it('has requestMapping method', () => {
    const host = document.createElement('div') as any;
    const repo = new SchemeWeaverRepository(host);
    expect(typeof repo.requestMapping).to.equal('function');
  });

  it('has resolveContentTypeAlias method', () => {
    const host = document.createElement('div') as any;
    const repo = new SchemeWeaverRepository(host);
    expect(typeof repo.resolveContentTypeAlias).to.equal('function');
  });
});
