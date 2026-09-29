export const ORDER_STATUS_LABELS: Record<string, string> = {
  NotDispatched: 'не распределена',
  Dispatched: 'назначена',
  Assigned: 'назначена',
  InProgress: 'в работе',
  Completed: 'выполнена',
  Cancelled: 'отменена',
}

const TERMINAL_STATUSES = new Set(['Completed', 'Cancelled'])

export function isTerminalOrderStatus(status: string | undefined) {
  return status !== undefined && TERMINAL_STATUSES.has(status)
}
