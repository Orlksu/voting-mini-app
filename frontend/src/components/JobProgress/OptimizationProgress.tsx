import { useEffect, useState } from 'react'
import styles from './JobProgress.module.css'
import JobProgressCard from './JobProgressCard.tsx'
import { isNotFoundError } from '../../api/api.ts'
import { optimizationApi } from '../../logic/optimizationApi.ts'
import type {
  JobStatusResponse,
  OptimizationResult,
} from '../../types/optimization.ts'

const POLL_INTERVAL_MS = 1500
const MAX_POLL_FAILURES = 3

type OptimizationProgressProps = {
  jobId: string
  title: string
  onCompleted: (result: OptimizationResult) => Promise<void>
  onShowResult: () => void
  onClose: () => void
}

export default function OptimizationProgress({
  jobId,
  title,
  onCompleted,
  onShowResult,
  onClose,
}: OptimizationProgressProps) {
  const [job, setJob] = useState<JobStatusResponse | null>(null)
  const [result, setResult] = useState<OptimizationResult | null>(null)
  const [error, setError] = useState<string | null>(null)

  useEffect(() => {
    const controller = new AbortController()
    let timeoutId: ReturnType<typeof setTimeout>
    let failures = 0

    const handleFailure = (pollError: unknown, retry: () => void) => {
      if (controller.signal.aborted) return

      failures += 1
      if (isNotFoundError(pollError)) {
        setError('Задача распределения не найдена')
      } else if (failures >= MAX_POLL_FAILURES) {
        setError('Не удалось получить статус распределения')
      } else {
        timeoutId = setTimeout(retry, POLL_INTERVAL_MS)
      }
    }

    const loadResult = async () => {
      try {
        const data = await optimizationApi.getResult(jobId, controller.signal)
        if (!data) {
          timeoutId = setTimeout(loadResult, POLL_INTERVAL_MS)
          return
        }
        setResult(data)
        await onCompleted(data)
      } catch (resultError) {
        handleFailure(resultError, loadResult)
      }
    }

    const poll = async () => {
      let status: JobStatusResponse

      try {
        status = await optimizationApi.getJob(jobId, controller.signal)
        failures = 0
      } catch (pollError) {
        handleFailure(pollError, poll)
        return
      }

      setJob(status)

      if (status.status === 'Completed') {
        void loadResult()
        return
      }
      if (status.status === 'Failed') return

      timeoutId = setTimeout(poll, POLL_INTERVAL_MS)
    }

    void poll()

    return () => {
      controller.abort()
      clearTimeout(timeoutId)
    }
  }, [jobId, onCompleted])

  const isFailed = job?.status === 'Failed'
  const isFinished = result !== null || isFailed || error !== null

  return (
    <JobProgressCard title={title} onClose={isFinished ? onClose : undefined}>
      {error ? (
        <p className={styles.error}>{error}</p>
      ) : isFailed ? (
        <p className={styles.error}>
          {job.errorMessage ?? 'Ошибка распределения'}
        </p>
      ) : result ? (
        <>
          <p className={styles.result}>
            распределено: {result.metrics.assignedOrders} из{' '}
            {result.metrics.totalOrders}
          </p>
          <div className={styles.actions}>
            <button
              type="button"
              className={styles.primaryAction}
              onClick={onShowResult}
            >
              посмотреть результат
            </button>
          </div>
        </>
      ) : (
        <>
          <p className={styles.stage}>
            {job?.status === 'Processing' ? 'Расчёт маршрутов...' : 'В очереди'}
          </p>
          <div className={styles.bar}>
            <div className={`${styles.barFill} ${styles.barIndeterminate}`} />
          </div>
        </>
      )}
    </JobProgressCard>
  )
}
