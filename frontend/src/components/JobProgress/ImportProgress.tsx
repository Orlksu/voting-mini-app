import { useEffect, useState } from 'react'
import styles from './JobProgress.module.css'
import JobProgressCard from './JobProgressCard.tsx'
import { isNotFoundError } from '../../api/api.ts'
import { orderApi } from '../../logic/orderApi.ts'
import type { OrderImportJob } from '../../types/orderImport.ts'

const POLL_INTERVAL_MS = 1000
const MAX_POLL_FAILURES = 3

// бэкенд может вернуть Processing уже после завершения (поздний Progress.Report), result при этом заполнен
function isCompleted(job: OrderImportJob) {
  return job.status === 'Completed' || job.result !== null
}

type ImportProgressProps = {
  jobId: string
  fileName?: string
  onCompleted: () => Promise<void>
  onClose: () => void
  onShowOrders: () => void
  onOptimize: () => void
}

export default function ImportProgress({
  jobId,
  fileName,
  onCompleted,
  onClose,
  onShowOrders,
  onOptimize,
}: ImportProgressProps) {
  const [job, setJob] = useState<OrderImportJob | null>(null)
  const [error, setError] = useState<string | null>(null)
  const [refreshError, setRefreshError] = useState<string | null>(null)

  useEffect(() => {
    const controller = new AbortController()
    let timeoutId: ReturnType<typeof setTimeout>
    let failures = 0

    const poll = async () => {
      let status: OrderImportJob

      try {
        status = await orderApi.getImportJob(jobId, controller.signal)
        failures = 0
      } catch (pollError) {
        if (controller.signal.aborted) return

        failures += 1
        if (isNotFoundError(pollError)) {
          setError('Задача импорта не найдена')
        } else if (failures >= MAX_POLL_FAILURES) {
          setError('Не удалось получить статус импорта')
        } else {
          timeoutId = setTimeout(poll, POLL_INTERVAL_MS)
        }
        return
      }

      setJob(status)

      if (isCompleted(status)) {
        try {
          await onCompleted()
        } catch {
          if (!controller.signal.aborted) {
            setRefreshError('Заявки загружены, но карта не обновилась')
          }
        }
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

  const isDone = job !== null && isCompleted(job)
  const isFinished = isDone || job?.status === 'Failed' || error !== null
  const percent = isDone ? 100 : (job?.progressPercent ?? 0)

  return (
    <JobProgressCard
      title={fileName ? `импорт: ${fileName}` : 'импорт заявок'}
      onClose={isFinished ? onClose : undefined}
    >

      {error ? (
        <p className={styles.error}>{error}</p>
      ) : job?.status === 'Failed' ? (
        <p className={styles.error}>{job.errorMessage ?? 'Ошибка импорта'}</p>
      ) : (
        <>
          <p className={styles.stage}>{job?.currentStage ?? 'В очереди'}</p>
          <div className={styles.bar}>
            <div className={styles.barFill} style={{ width: `${percent}%` }} />
          </div>
          <p className={styles.counter}>
            {job && job.totalItems > 0
              ? `${job.processedItems} / ${job.totalItems}`
              : ''}
            <span>{percent}%</span>
          </p>
        </>
      )}

      {isDone && job.result && (
        <>
          <p className={styles.result}>
            загружено: {job.result.importedOrdersCount}
            {job.result.unresolvedAddressesCount > 0 &&
              `, адрес не найден: ${job.result.unresolvedAddressesCount}`}
          </p>
          <div className={styles.actions}>
            <button
              type="button"
              className={styles.secondaryAction}
              onClick={onShowOrders}
            >
              посмотреть заявки
            </button>
            <button
              type="button"
              className={styles.primaryAction}
              onClick={onOptimize}
            >
              распределить
            </button>
          </div>
        </>
      )}

      {refreshError && <p className={styles.error}>{refreshError}</p>}
    </JobProgressCard>
  )
}
