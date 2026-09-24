import { http } from './http'
import { optimizationMock } from '../mocks/optimizationMock'
import type {
  CreateOptimizationPlanRequest,
  JobStatusResponse,
  OptimizationResult,
} from '../types/optimization'

const useMocks = import.meta.env.VITE_USE_MOCKS !== 'false'

export async function getSampleOptimizationRequest() {
  if (useMocks) {
    throw new Error(
      'Sample optimization request is not available in the current mock layer',
    )
  }

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

export async function getMockOptimizationResult() {
  if (!useMocks) {
    throw new Error('Mock data is disabled')
  }

  await new Promise((resolve) => setTimeout(resolve, 300))
  return optimizationMock
}
