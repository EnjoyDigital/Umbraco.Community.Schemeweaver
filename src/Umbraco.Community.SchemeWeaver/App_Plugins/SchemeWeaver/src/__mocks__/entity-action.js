export class UmbEntityActionBase {
  constructor(host, args) {
    this.host = host;
    this.args = args || {};
  }

  async getContext(token) {
    return {};
  }

  async execute() {}
}

export class UmbRequestReloadStructureForEntityEvent {
  static TYPE = 'request-reload-structure-for-entity';
  constructor() {}
}
