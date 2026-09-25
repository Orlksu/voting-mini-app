import axios from 'axios'

export const api = axios.create({
  baseURL: import.meta.env.VITE_API_URL ?? '/api/v1',
  timeout: 10_000,
  headers: {
    'Content-Type': 'application/json',
  },
})

api.interceptors.response.use(
  (response) => response,
  (error: unknown) => {
    if (!axios.isAxiosError(error)) {
      return Promise.reject(error)
    }

    const serverMessage =
      typeof error.response?.data?.message === 'string'
        ? error.response.data.message
        : null
    const message =
      serverMessage ??
      error.response?.statusText ??
      error.message ??
      'Ошибка сетевого запроса'

    return Promise.reject(new Error(message, { cause: error }))
  },
)
