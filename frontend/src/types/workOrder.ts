export type WorkOrder = {
  id: string
  externalId: string
  address: string
  district?: string
  latitude: number
  longitude: number
  windowStart: string
  windowEnd: string
  priority: string
  status: string
  taskType: string
  cancellationReason?: string | null
}

export type WorkOrderCancelResult = {
  order: WorkOrder
  replanJobId: string | null
}
