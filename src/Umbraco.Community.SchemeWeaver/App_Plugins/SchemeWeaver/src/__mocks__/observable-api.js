export class UmbArrayState {
  constructor(initial, getUnique) {
    this._value = initial;
    this._getUnique = getUnique;
    this._subscribers = [];
  }

  getValue() {
    return this._value;
  }

  setValue(value) {
    this._value = value;
    this._subscribers.forEach(cb => cb(value));
  }

  asObservable() {
    const self = this;
    return {
      subscribe(callback) {
        self._subscribers.push(callback);
        callback(self._value);
        return { unsubscribe() { self._subscribers = self._subscribers.filter(cb => cb !== callback); } };
      },
      getValue() {
        return self._value;
      },
    };
  }
}

export class UmbObjectState {
  constructor(initial) {
    this._value = initial;
    this._subscribers = [];
  }

  getValue() {
    return this._value;
  }

  setValue(value) {
    this._value = value;
    this._subscribers.forEach(cb => cb(value));
  }

  asObservable() {
    const self = this;
    return {
      subscribe(callback) {
        self._subscribers.push(callback);
        callback(self._value);
        return { unsubscribe() { self._subscribers = self._subscribers.filter(cb => cb !== callback); } };
      },
      getValue() {
        return self._value;
      },
    };
  }
}
