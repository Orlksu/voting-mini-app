export type WorkOrder = {
  id: string
  externalId: string
  address: string
  district?: string
  depotId?: string | null
  latitude: number
  longitude: number
  windowStart: string
  windowEnd: string
  priority: string
  status: string
  taskType: string
  cancellationReason?: string | null
  sequence?: number
}

export type WorkOrderCancelResult = {
  order: WorkOrder
  replanJobId: string | null
}

export type CreateWorkOrderRequest = {
  externalId?: string
  district?: string
  address: string
  latitude?: number
  longitude?: number
  windowStart: string
  windowEnd: string
  requiredSkill: 'LocalRepair' | 'Installation' | 'Emergency'
  requiredTransport: 'Car' | null
  priority: 'Low' | 'Normal' | 'High' | 'Emergency'
  taskType?: string
  durationMinutes: number
}
