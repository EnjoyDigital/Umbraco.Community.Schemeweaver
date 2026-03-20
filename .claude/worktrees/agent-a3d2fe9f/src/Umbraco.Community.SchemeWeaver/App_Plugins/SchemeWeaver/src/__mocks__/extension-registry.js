export class UmbExtensionRegistry {
  constructor() {
    this._extensions = [];
  }

  register(extension) {
    this._extensions.push(extension);
  }

  getByAlias(alias) {
    return this._extensions.find(e => e.alias === alias);
  }
}
