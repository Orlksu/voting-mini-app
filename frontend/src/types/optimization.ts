export type TransportType = 'Car' | 'PublicTransit' | 'Bicycle' | 'Pedestrian'

export type OrderPriority = 'Low' | 'Normal' | 'High' | 'Emergency'

export type JobStatus = 'Queued' | 'Processing' | 'Completed' | 'Failed'

export type UnassignedReasonCode =
  | 'NoQualifiedEngineer'
  | 'NoTransportMatch'
  | 'TimeWindowMissed'
  | 'ShiftLimitExceeded'
  | 'CapacityOverload'
  | 'EmergencyDisplaced'

export type SkillType = 'LocalRepair' | 'Installation' | 'Emergency'

export type EngineerInput = {
  externalId: string
  name: string
  startAddress: string
  startLatitude: number | null
  startLongitude: number | null
  shiftStart: string
  shiftEnd: string
  transport: TransportType
  skills: SkillType[]
  maxOrdersPerShift: number
}

export type WorkOrderInput = {
  externalId: string
  address: string
  latitude: number | null
  longitude: number | null
  windowStart: string
  windowEnd: string
  requiredSkill: SkillType
  requiredTransport: TransportType | null
  priority: OrderPriority
  taskType: string
  durationMinutes: number
}

export type CreateOptimizationPlanRequest = {
  planDate: string
  engineers: EngineerInput[]
  orders: WorkOrderInput[]
  timeLimitSeconds?: number
}

export type RouteStop = {
  sequence: number
  externalOrderId: string
  address: string
  latitude: number
  longitude: number
  estimatedArrival: string
  serviceStart: string
  serviceEnd: string
  travelDurationMinutes: number
  distanceFromPrevKm: number
  taskType: string
  priority: OrderPriority
  assignmentReason: string
}

export type EngineerRoute = {
  engineerExternalId: string
  engineerName: string
  transport: TransportType
  totalOrders: number
  totalDistanceKm: number
  baselineDistanceKm: number
  totalTravelMinutes: number
  stops: RouteStop[]
  encodedPolyline: string | null
  startAddress: string | null
  startLatitude: number | null
  startLongitude: number | null
}

export type PlanComparisonMetrics = {
  totalOrders: number
  assignedOrders: number
  unassignedOrders: number
  optimizedActiveEngineers: number
  baselineActiveEngineers: number
  savedEngineersCount: number
  optimizedTotalDistanceKm: number
  baselineTotalDistanceKm: number
  distanceEconomyPercent: number
}

export type UnassignedOrder = {
  externalOrderId: string
  address: string
  reasonCode: UnassignedReasonCode
  reasonDescription: string
}

export type OptimizationResult = {
  planId: string
  planDate: string
  metrics: PlanComparisonMetrics
  routes: EngineerRoute[]
  unassignedOrders: UnassignedOrder[]
}

export type JobStatusResponse = {
  jobId: string
  status: JobStatus
  errorMessage: string | null
  createdAt: string
}

export type RouteGeometry = {
  type: 'Feature'
  properties: Record<string, unknown>
  geometry: {
    type: 'LineString'
    coordinates: [longitude: number, latitude: number][]
  }
}
