import { api } from '../api/api.ts'
import type {
  JobStatusResponse,
  OptimizationResult,
} from '../types/optimization.ts'

export const optimizationApi = {
  async createJob(): Promise<JobStatusResponse> {
    const response = await api.post<JobStatusResponse>('/optimization/jobs')
    return response.data
  },

  async replan(): Promise<JobStatusResponse> {
    const response = await api.post<JobStatusResponse>('/optimization/replan')
    return response.data
  },

  async getJob(
    jobId: string,
    signal?: AbortSignal,
  ): Promise<JobStatusResponse> {
    const response = await api.get<JobStatusResponse>(
      `/optimization/jobs/${jobId}`,
      { signal },
    )
    return response.data
  },

  async getResult(
    jobId: string,
    signal?: AbortSignal,
  ): Promise<OptimizationResult> {
    const response = await api.get<OptimizationResult>(
      `/optimization/jobs/${jobId}/result`,
      { signal },
    )
    return response.data
  },
}
