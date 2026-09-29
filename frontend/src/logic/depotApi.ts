import { api } from '../api/api.ts'
import type { Depot } from '../types/depot.ts'

export const depotApi = {
  async getAll(signal?: AbortSignal): Promise<Depot[]> {
    const response = await api.get<Depot[]>('/depots', { signal })
    return response.data
  },
}
