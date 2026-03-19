export class UmbControllerBase {
  constructor(host) {
    this._host = host;
  }

  getHostElement() {
    return this._host;
  }

  async getContext(token) {
    return {};
  }

  destroy() {}
}
