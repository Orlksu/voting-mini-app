import type { WorkOrder } from '../types/workOrder.ts'
import type { EngineerRoute } from '../types/engineerRoute.ts'
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
}
