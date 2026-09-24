export const TransportType = {
  Car: 1,
  PublicTransit: 2,
  Bicycle: 3,
  Pedestrian: 4,
} as const

export type TransportType = (typeof TransportType)[keyof typeof TransportType]

export const OrderPriority = {
  Low: 1,
  Normal: 2,
  High: 3,
  Emergency: 4,
} as const

export type OrderPriority = (typeof OrderPriority)[keyof typeof OrderPriority]

export const JobStatus = {
  Queued: 1,
  Processing: 2,
  Completed: 3,
  Failed: 4,
} as const

export type JobStatus = (typeof JobStatus)[keyof typeof JobStatus]

export const UnassignedReasonCode = {
  NoQualifiedEngineer: 1,
  NoTransportMatch: 2,
  TimeWindowMissed: 3,
  ShiftLimitExceeded: 4,
  CapacityOverload: 5,
  EmergencyDisplaced: 6,
} as const

export type UnassignedReasonCode =
  (typeof UnassignedReasonCode)[keyof typeof UnassignedReasonCode]

export const SkillType = {
  LocalRepair: 1,
  Installation: 2,
  Emergency: 3,
} as const

export type SkillType = (typeof SkillType)[keyof typeof SkillType]

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
