import type { JobStatus } from './optimization.ts'

export type EngineerImportResult = {
  imported: number
  skipped: number
  unresolvedAddresses: number
  depotsCreated: number
  warnings: string[]
}

export type EngineerImportJob = {
  jobId: string
  status: JobStatus
  totalItems: number
  processedItems: number
  progressPercent: number
  currentStage: string
  result: EngineerImportResult | null
  errorMessage: string | null
  createdAt: string
}
