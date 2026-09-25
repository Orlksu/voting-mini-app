import { api } from '../api/api.ts'
import type { Engineer } from '../types/engineer'

export const engineersApi = {
  async getAll(signal?: AbortSignal): Promise<Engineer[]> {
    const response = await api.get<Engineer[]>('/engineers', { signal })
    return response.data
  },
}
