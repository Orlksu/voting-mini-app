export type Engineer = {
  id: string
  externalId: string
  name: string
  district: string
  depotId: string | null
  startAddress: string
  startLatitude: number
  startLongitude: number
  shiftStart: string
  shiftEnd: string
  transport: string
  skills: string[]
  isAvailable: boolean
  maxOrdersPerShift: number
}
