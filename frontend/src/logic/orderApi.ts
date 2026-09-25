import type { WorkOrder } from '../types/workOrder.ts'
import { api } from '../api/api.ts'

export const orderApi = {
  async getAll(signal?: AbortSignal): Promise<WorkOrder[]> {
    const response = await api.get<WorkOrder[]>('/workorders', { signal })
    return response.data
  },
}
