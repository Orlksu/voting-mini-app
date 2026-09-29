export const OFFICE_ROUTE_COLORS = [
  '#FBB72C',
  '#4CC9F0',
  '#F472B6',
  '#34D399',
  '#A78BFA',
  '#FB923C',
  '#E9F59A',
  '#60A5FA',
]

export type OfficeRoute = {
  id: string
  color: string
  encodedPolyline: string | null
  startPoint: { latitude: number; longitude: number } | null
  stops: {
    externalId: string
    latitude: number
    longitude: number
    priority: string
    sequence: number
  }[]
}
