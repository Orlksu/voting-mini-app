export type OrderImportJobStatus = 'Queued' | 'Processing' | 'Completed' | 'Failed'

export type OrderImportResult = {
  processedFiles: number
  importedOrdersCount: number
  unresolvedAddressesCount: number
  warnings: string[]
}

export type OrderImportJob = {
  jobId: string
  status: OrderImportJobStatus
  totalItems: number
  processedItems: number
  progressPercent: number
  currentStage: string
  result: OrderImportResult | null
  errorMessage: string | null
  createdAt: string
}
