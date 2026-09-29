import { useState } from 'react'
import type { MouseEvent } from 'react'
import styles from './OrderActions.module.css'
import type { WorkOrder } from '../../types/workOrder.ts'
import {
  ORDER_STATUS_LABELS,
  isTerminalOrderStatus,
} from '../../logic/orderStatus.ts'

const MAX_REASON_LENGTH = 500

type OrderActionsProps = {
  order: WorkOrder
  variant: 'popup' | 'row'
  onComplete: (order: WorkOrder) => Promise<unknown>
  onCancel: (order: WorkOrder, reason: string) => Promise<unknown>
}

type Mode = 'idle' | 'complete' | 'cancel'

function stop(event: MouseEvent) {
  event.stopPropagation()
}

export default function OrderActions({
  order,
  variant,
  onComplete,
  onCancel,
}: OrderActionsProps) {
  const [mode, setMode] = useState<Mode>('idle')
  const [reason, setReason] = useState('')
  const [isBusy, setIsBusy] = useState(false)
  const [error, setError] = useState<string | null>(null)

  if (isTerminalOrderStatus(order.status)) {
    return (
      <div className={`${styles.status} ${styles[variant] ?? ''}`}>
        <span
          className={`${styles.badge} ${order.status === 'Cancelled' ? styles.badgeCancelled : styles.badgeCompleted}`}
        >
          {ORDER_STATUS_LABELS[order.status]}
        </span>
        {order.status === 'Cancelled' && order.cancellationReason && (
          <span className={styles.reasonText}>{order.cancellationReason}</span>
        )}
      </div>
    )
  }

  const reset = () => {
    setMode('idle')
    setReason('')
    setError(null)
  }

  const run = async (action: () => Promise<unknown>) => {
    setIsBusy(true)
    setError(null)
    try {
      await action()
      reset()
    } catch (actionError) {
      setError(
        actionError instanceof Error
          ? actionError.message
          : 'Не удалось выполнить действие',
      )
    } finally {
      setIsBusy(false)
    }
  }

  if (mode === 'idle') {
    return variant === 'popup' ? (
      <div className={styles.buttons}>
        <button
          type="button"
          className={styles.cancel}
          onClick={() => setMode('cancel')}
        >
          отменить
        </button>
        <button
          type="button"
          className={styles.complete}
          onClick={() => setMode('complete')}
        >
          завершить
        </button>
      </div>
    ) : (
      <div className={styles.iconButtons} onClick={stop}>
        <button
          type="button"
          className={styles.iconComplete}
          onClick={() => setMode('complete')}
          aria-label="Завершить заявку"
          title="Завершить"
        >
          <span className="material-symbols-rounded">check</span>
        </button>
        <button
          type="button"
          className={styles.iconCancel}
          onClick={() => setMode('cancel')}
          aria-label="Отменить заявку"
          title="Отменить"
        >
          <span className="material-symbols-rounded">close</span>
        </button>
      </div>
    )
  }

  return (
    <div
      className={`${styles.confirm} ${styles[variant] ?? ''}`}
      onClick={stop}
      role="alertdialog"
    >
      {mode === 'complete' ? (
        <p className={styles.question}>Отметить заявку выполненной?</p>
      ) : (
        <>
          <p className={styles.question}>Отменить заявку?</p>
          {order.status === 'Dispatched' && (
            <p className={styles.note}>
              заявка в маршруте — бригады будут перераспределены
            </p>
          )}
          <textarea
            className={styles.reason}
            value={reason}
            onChange={(event) =>
              setReason(event.target.value.slice(0, MAX_REASON_LENGTH))
            }
            placeholder="причина (необязательно)"
            rows={2}
            disabled={isBusy}
          />
        </>
      )}

      {error && (
        <p className={styles.error} role="alert">
          {error}
        </p>
      )}

      <div className={styles.buttons}>
        <button
          type="button"
          className={styles.back}
          disabled={isBusy}
          onClick={reset}
        >
          назад
        </button>
        {mode === 'complete' ? (
          <button
            type="button"
            className={styles.complete}
            disabled={isBusy}
            onClick={() => run(() => onComplete(order))}
          >
            {isBusy ? 'сохранение...' : 'завершить'}
          </button>
        ) : (
          <button
            type="button"
            className={styles.cancelSolid}
            disabled={isBusy}
            onClick={() => run(() => onCancel(order, reason.trim()))}
          >
            {isBusy ? 'отмена...' : 'отменить заявку'}
          </button>
        )}
      </div>
    </div>
  )
}
