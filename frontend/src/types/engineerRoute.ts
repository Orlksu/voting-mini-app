export type EngineerRouteStop = {
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
  priority: string
  assignmentReason: string
}

export type EngineerRoute = {
  engineerExternalId: string
  engineerName: string
  transport: string
  totalOrders: number
  totalDistanceKm: number
  baselineDistanceKm: number
  totalTravelMinutes: number
  stops: EngineerRouteStop[]
  encodedPolyline: string | null
  startAddress: string
  startLatitude: number
  startLongitude: number
}
