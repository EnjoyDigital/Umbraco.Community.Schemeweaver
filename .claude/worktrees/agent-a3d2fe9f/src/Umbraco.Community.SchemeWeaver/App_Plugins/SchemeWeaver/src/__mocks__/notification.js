export const UMB_NOTIFICATION_CONTEXT = Symbol('UMB_NOTIFICATION_CONTEXT');

export class MockNotificationContext {
  constructor() {
    this.notifications = [];
  }

  peek(color, args) {
    this.notifications.push({ color, ...args });
  }

  reset() {
    this.notifications = [];
  }
}
