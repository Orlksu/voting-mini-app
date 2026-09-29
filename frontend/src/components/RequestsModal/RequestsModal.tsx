import { useEffect, useState } from 'react'
import styles from './RequestsModal.module.css'
import type { WorkOrder } from '../../types/workOrder.ts'
import { orderApi } from '../../logic/orderApi.ts'
import OrderActions from '../OrderActions/OrderActions.tsx'
import { isTerminalOrderStatus } from '../../logic/orderStatus.ts'

type RequestsModalProps = {
  isOpen: boolean
  onClose: () => void
  onRequestClick: (externalId: string) => void
  onComplete: (order: WorkOrder) => Promise<WorkOrder>
  onCancel: (order: WorkOrder, reason: string) => Promise<WorkOrder>
}

export default function RequestsModal({
  isOpen,
  onClose,
  onRequestClick,
  onComplete,
  onCancel,
}: RequestsModalProps) {
  const [requests, setRequests] = useState<WorkOrder[]>([])
  const [isLoading, setIsLoading] = useState(true)
  const [error, setError] = useState<string | null>(null)

  useEffect(() => {
    if (!isOpen) return

    const handleKeyDown = (event: KeyboardEvent) => {
      if (event.key === 'Escape') onClose()
    }

    document.addEventListener('keydown', handleKeyDown)
    return () => document.removeEventListener('keydown', handleKeyDown)
  }, [isOpen, onClose])

  useEffect(() => {
    if (!isOpen) return

    const controller = new AbortController()

    const loadRequests = async () => {
      try {
        setIsLoading(true)
        setError(null)
        const res = await orderApi.getAll(controller.signal)
        setRequests(res)
      } catch {
        if (!controller.signal.aborted) {
          setError('Не удалось загрузить список заявок')
        }
      } finally {
        if (!controller.signal.aborted) {
          setIsLoading(false)
        }
      }
    }

    void loadRequests()

    return () => {
      controller.abort()
    }
  }, [isOpen])

  if (!isOpen) return null

  const replaceRequest = (updated: WorkOrder) => {
    setRequests((current) =>
      current.map((item) => (item.id === updated.id ? updated : item)),
    )
  }

  const sortedRequests = [
    ...requests.filter((request) => !isTerminalOrderStatus(request.status)),
    ...requests.filter((request) => isTerminalOrderStatus(request.status)),
  ]

  return (
    <div className={styles.overlay} onClick={onClose}>
      <div
        className={styles.modal}
        role="dialog"
        aria-modal="true"
        onClick={(event) => event.stopPropagation()}
      >
        <p className={styles.title}>текущие заявки</p>
        {isLoading && <p>Загрузка...</p>}
        {error && <p role="alert">{error}</p>}
        <div className={styles.orders}>
          {sortedRequests.length > 0 ? (
            sortedRequests.map((request) => {
              const isTerminal = isTerminalOrderStatus(request.status)

              return (
                <div
                  key={request.id}
                  className={`${styles.request} ${isTerminal ? styles.requestTerminal : ''}`}
                  onClick={
                    isTerminal
                      ? undefined
                      : () => onRequestClick(request.externalId)
                  }
                >
                  <div className={styles.circle}></div>
                  <div className={styles.info}>
                    <p>{request.address}</p>
                    <p>{request.taskType}</p>
                  </div>
                  <OrderActions
                    order={request}
                    variant="row"
                    onComplete={async (order) =>
                      replaceRequest(await onComplete(order))
                    }
                    onCancel={async (order, reason) =>
                      replaceRequest(await onCancel(order, reason))
                    }
                  />
                </div>
              )
            })
          ) : (
            <p className={styles.noRequests}>
              Актуальных заявок нет, возможно, они ещё не загрузились
            </p>
          )}
        </div>
      </div>
    </div>
  )
}
