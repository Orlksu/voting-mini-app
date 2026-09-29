import { api } from '../api/api.ts'
import type { Engineer } from '../types/engineer'
import type {
  EngineerImportJob,
  EngineerImportResult,
} from '../types/engineerImport.ts'

function toFormData(file: File) {
  const formData = new FormData()
  formData.append('file', file)
  return formData
}

const multipartConfig = {
  headers: { 'Content-Type': 'multipart/form-data' },
  timeout: 120_000,
}

export const engineersApi = {
  async getAll(signal?: AbortSignal): Promise<Engineer[]> {
    const response = await api.get<Engineer[]>('/engineers', { signal })
    return response.data
  },

  async uploadCsv(file: File): Promise<EngineerImportJob> {
    const response = await api.post<EngineerImportJob>(
      '/engineers/upload-csv',
      toFormData(file),
      multipartConfig,
    )
    return response.data
  },

  async importCsvSync(file: File): Promise<EngineerImportResult> {
    const response = await api.post<EngineerImportResult>(
      '/engineers/upload-csv',
      toFormData(file),
      { ...multipartConfig, params: { sync: true } },
    )
    return response.data
  },

  async getImportJob(
    jobId: string,
    signal?: AbortSignal,
  ): Promise<EngineerImportJob> {
    const response = await api.get<EngineerImportJob>(
      `/engineers/import-jobs/${jobId}`,
      { signal },
    )
    return response.data
  },
}
