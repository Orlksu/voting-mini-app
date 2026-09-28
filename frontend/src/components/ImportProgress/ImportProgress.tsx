import { useEffect, useState } from 'react'
import axios from 'axios'
import styles from './ImportProgress.module.css'
import { orderApi } from '../../logic/orderApi.ts'
import type { OrderImportJob } from '../../types/orderImport.ts'

const POLL_INTERVAL_MS = 1000
const MAX_POLL_FAILURES = 3

// бэкенд может вернуть Processing уже после завершения (поздний Progress.Report), result при этом заполнен
function isCompleted(job: OrderImportJob) {
  return job.status === 'Completed' || job.result !== null
}

function isNotFound(error: unknown) {
  const cause = error instanceof Error ? error.cause : undefined
  return axios.isAxiosError(cause) && cause.response?.status === 404
}

type ImportProgressProps = {
  jobId: string
  onCompleted: () => Promise<void>
  onClose: () => void
  onShowOrders: () => void
}

export default function ImportProgress({
  jobId,
  onCompleted,
  onClose,
  onShowOrders,
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
        if (isNotFound(pollError)) {
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
    <div className={styles.container}>
      <div className={styles.header}>
        <p className={styles.title}>импорт заявок</p>
        {isFinished && (
          <button type="button" className={styles.close} onClick={onClose}>
            <span className="material-symbols-rounded">close</span>
          </button>
        )}
      </div>

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
          <button
            type="button"
            className={styles.showOrders}
            onClick={onShowOrders}
          >
            посмотреть заявки
          </button>
        </>
      )}

      {refreshError && <p className={styles.error}>{refreshError}</p>}
    </div>
  )
}
