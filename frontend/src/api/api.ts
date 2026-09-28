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

    const data: unknown = error.response?.data
    const serverMessage =
      typeof data === 'string' && data.length > 0
        ? data
        : typeof data === 'object' &&
            data !== null &&
            'message' in data &&
            typeof data.message === 'string'
          ? data.message
          : null
    const message =
      serverMessage ||
      error.response?.statusText ||
      error.message ||
      'Ошибка сетевого запроса'

    return Promise.reject(new Error(message, { cause: error }))
  },
)
