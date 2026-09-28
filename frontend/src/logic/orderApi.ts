import type { WorkOrder } from '../types/workOrder.ts'
import type { EngineerRoute } from '../types/engineerRoute.ts'
import type { OrderImportJob } from '../types/orderImport.ts'
import { api } from '../api/api.ts'

export const orderApi = {
  async getAll(signal?: AbortSignal): Promise<WorkOrder[]> {
    const response = await api.get<WorkOrder[]>('/workorders', { signal })
    return response.data
  },

  async getEngineerRoute(
    engineerId: string,
    signal?: AbortSignal,
  ): Promise<EngineerRoute> {
    const response = await api.get<EngineerRoute>(
      `/engineers/${engineerId}/route`,
      { signal },
    )
    return response.data
  },

  async uploadCsv(file: File): Promise<OrderImportJob> {
    const formData = new FormData()
    formData.append('files', file)

    const response = await api.post<OrderImportJob>(
      '/workorders/upload-csv',
      formData,
      {
        headers: { 'Content-Type': 'multipart/form-data' },
        timeout: 120_000,
      },
    )
    return response.data
  },

  async getImportJob(
    jobId: string,
    signal?: AbortSignal,
  ): Promise<OrderImportJob> {
    const response = await api.get<OrderImportJob>(
      `/workorders/import-jobs/${jobId}`,
      { signal },
    )
    return response.data
  },
}
