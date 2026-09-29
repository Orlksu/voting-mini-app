import { useEffect, useState } from 'react'
import styles from './JobProgress.module.css'
import JobProgressCard from './JobProgressCard.tsx'
import { isNotFoundError } from '../../api/api.ts'
import { orderApi } from '../../logic/orderApi.ts'
import { engineersApi } from '../../logic/engineerApi.ts'
import type { JobStatus } from '../../types/optimization.ts'

const POLL_INTERVAL_MS = 1000
const MAX_POLL_FAILURES = 3

export type ImportTarget = 'orders' | 'engineers'

type ImportJobView = {
  status: JobStatus
  totalItems: number
  processedItems: number
  progressPercent: number
  currentStage: string
  errorMessage: string | null
  resultText: string | null
}

async function fetchImportJob(
  target: ImportTarget,
  jobId: string,
  signal: AbortSignal,
): Promise<ImportJobView> {
  if (target === 'engineers') {
    const job = await engineersApi.getImportJob(jobId, signal)
    return {
      ...job,
      resultText: job.result
        ? [
            `добавлено бригад: ${job.result.imported}`,
            job.result.skipped > 0 && `пропущено: ${job.result.skipped}`,
            job.result.unresolvedAddresses > 0 &&
              `адрес не найден: ${job.result.unresolvedAddresses}`,
          ]
            .filter(Boolean)
            .join(', ')
        : null,
    }
  }

  const job = await orderApi.getImportJob(jobId, signal)
  return {
    ...job,
    resultText: job.result
      ? [
          `загружено: ${job.result.importedOrdersCount}`,
          job.result.unresolvedAddressesCount > 0 &&
            `адрес не найден: ${job.result.unresolvedAddressesCount}`,
        ]
          .filter(Boolean)
          .join(', ')
      : null,
  }
}

// бэкенд может вернуть Processing уже после завершения (поздний Progress.Report), result при этом заполнен
function isCompleted(job: ImportJobView) {
  return job.status === 'Completed' || job.resultText !== null
}

const TITLES: Record<ImportTarget, string> = {
  orders: 'импорт заявок',
  engineers: 'импорт бригад',
}

type ImportProgressProps = {
  jobId: string
  target: ImportTarget
  fileName?: string
  onCompleted: () => Promise<void>
  onClose: () => void
  onShow: () => void
  onOptimize?: () => void
}

export default function ImportProgress({
  jobId,
  target,
  fileName,
  onCompleted,
  onClose,
  onShow,
  onOptimize,
}: ImportProgressProps) {
  const [job, setJob] = useState<ImportJobView | null>(null)
  const [error, setError] = useState<string | null>(null)
  const [refreshError, setRefreshError] = useState<string | null>(null)

  useEffect(() => {
    const controller = new AbortController()
    let timeoutId: ReturnType<typeof setTimeout>
    let failures = 0

    const poll = async () => {
      let status: ImportJobView

      try {
        status = await fetchImportJob(target, jobId, controller.signal)
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
            setRefreshError('Данные загружены, но карта не обновилась')
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
  }, [jobId, target, onCompleted])

  const isDone = job !== null && isCompleted(job)
  const isFinished = isDone || job?.status === 'Failed' || error !== null
  const percent = isDone ? 100 : (job?.progressPercent ?? 0)

  return (
    <JobProgressCard
      title={fileName ? `${TITLES[target]}: ${fileName}` : TITLES[target]}
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

      {isDone && job.resultText && (
        <>
          <p className={styles.result}>{job.resultText}</p>
          <div className={styles.actions}>
            <button
              type="button"
              className={styles.secondaryAction}
              onClick={onShow}
            >
              {target === 'engineers'
                ? 'посмотреть бригады'
                : 'посмотреть заявки'}
            </button>
            {onOptimize && (
              <button
                type="button"
                className={styles.primaryAction}
                onClick={onOptimize}
              >
                распределить
              </button>
            )}
          </div>
        </>
      )}

      {refreshError && <p className={styles.error}>{refreshError}</p>}
    </JobProgressCard>
  )
}
