export interface CompanionEvent<TPayload = unknown> {
  type: string;
  payload: TPayload;
  createdAt: string;
}

export type EventHandler<TPayload = unknown> = (event: CompanionEvent<TPayload>) => void;

export class EventBus {
  private readonly listeners = new Map<string, Set<EventHandler>>();

  on<TPayload>(type: string, handler: EventHandler<TPayload>) {
    const handlers = this.listeners.get(type) ?? new Set<EventHandler>();
    handlers.add(handler as EventHandler);
    this.listeners.set(type, handlers);

    return () => handlers.delete(handler as EventHandler);
  }

  emit<TPayload>(type: string, payload: TPayload) {
    const event: CompanionEvent<TPayload> = {
      type,
      payload,
      createdAt: new Date().toISOString(),
    };
    const handlers = this.listeners.get(type);
    if (!handlers) {
      return;
    }

    handlers.forEach((handler) => handler(event));
  }
}
