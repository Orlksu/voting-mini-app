import { http } from './http'
import type {
  CreateOptimizationPlanRequest,
  JobStatusResponse,
  OptimizationResult,
} from '../types/optimization'

export async function getSampleOptimizationRequest() {
  const response = await http.get<CreateOptimizationPlanRequest>(
    '/optimization/sample-request',
  )
  return response.data
}

export async function createOptimizationJob(
  request: CreateOptimizationPlanRequest,
) {
  const response = await http.post<JobStatusResponse>(
    '/optimization/jobs',
    request,
  )
  return response.data
}

export async function getOptimizationJobStatus(jobId: string) {
  const response = await http.get<JobStatusResponse>(
    `/optimization/jobs/${jobId}`,
  )
  return response.data
}

export async function getOptimizationResult(jobId: string) {
  const response = await http.get<OptimizationResult>(
    `/optimization/jobs/${jobId}/result`,
  )
  return response.data
}
