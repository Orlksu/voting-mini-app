import { http } from '../api/http'
import type { Engineer } from '../types/engineer'

export const engineersApi = {
  async getAll(signal?: AbortSignal): Promise<Engineer[]> {
    const response = await http.get<Engineer[]>('/engineers', { signal })
    return response.data
  },
}
