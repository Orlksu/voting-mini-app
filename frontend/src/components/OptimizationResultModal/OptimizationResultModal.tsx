import { useEffect } from 'react'
import styles from './OptimizationResultModal.module.css'
import type { Engineer } from '../../types/engineer'
import type { OptimizationResult } from '../../types/optimization.ts'

type OptimizationResultModalProps = {
  isOpen: boolean
  result: OptimizationResult | null
  engineers: Engineer[]
  onClose: () => void
  onShowRoute: (engineerId: string) => void
}

function formatKm(value: number) {
  return `${value.toFixed(1)} км`
}

function formatMinutes(value: number) {
  const hours = Math.floor(value / 60)
  const minutes = value % 60
  return hours > 0 ? `${hours} ч ${minutes} мин` : `${minutes} мин`
}

export default function OptimizationResultModal({
  isOpen,
  result,
  engineers,
  onClose,
  onShowRoute,
}: OptimizationResultModalProps) {
  useEffect(() => {
    if (!isOpen) return

    const handleKeyDown = (event: KeyboardEvent) => {
      if (event.key === 'Escape') onClose()
    }

    document.addEventListener('keydown', handleKeyDown)
    return () => document.removeEventListener('keydown', handleKeyDown)
  }, [isOpen, onClose])

  if (!isOpen || !result) return null

  const { metrics } = result
  const engineerIdByExternalId = new Map(
    engineers.map((engineer) => [engineer.externalId, engineer.id]),
  )

  return (
    <div className={styles.overlay} onClick={onClose}>
      <div
        className={styles.modal}
        role="dialog"
        aria-modal="true"
        onClick={(event) => event.stopPropagation()}
      >
        <p className={styles.title}>результат распределения</p>

        <div className={styles.metrics}>
          <div className={styles.metric}>
            <p className={styles.metricValue}>
              {metrics.assignedOrders} / {metrics.totalOrders}
            </p>
            <p className={styles.metricLabel}>заявок распределено</p>
          </div>
          <div className={styles.metric}>
            <p className={styles.metricValue}>
              {metrics.optimizedActiveEngineers}
            </p>
            <p className={styles.metricLabel}>
              бригад задействовано
              {metrics.savedEngineersCount > 0 &&
                ` (−${metrics.savedEngineersCount})`}
            </p>
          </div>
          <div className={styles.metric}>
            <p className={styles.metricValue}>
              {formatKm(metrics.optimizedTotalDistanceKm)}
            </p>
            <p className={styles.metricLabel}>
              общий пробег
              {metrics.distanceEconomyPercent > 0 &&
                ` (−${metrics.distanceEconomyPercent.toFixed(0)}%)`}
            </p>
          </div>
        </div>

        <div className={styles.lists}>
          <p className={styles.sectionTitle}>маршруты</p>
          {result.routes.length > 0 ? (
            result.routes.map((route) => {
              const engineerId = engineerIdByExternalId.get(
                route.engineerExternalId,
              )

              return (
                <div
                  key={route.engineerExternalId}
                  className={`${styles.route} ${engineerId ? styles.routeClickable : ''}`}
                  onClick={engineerId ? () => onShowRoute(engineerId) : undefined}
                >
                  <p className={styles.routeName}>{route.engineerName}</p>
                  <p className={styles.routeStats}>
                    {route.totalOrders} заявок · {formatKm(route.totalDistanceKm)}{' '}
                    · {formatMinutes(route.totalTravelMinutes)} в пути
                  </p>
                  {engineerId && (
                    <span className={`material-symbols-rounded ${styles.routeIcon}`}>
                      map
                    </span>
                  )}
                </div>
              )
            })
          ) : (
            <p className={styles.empty}>маршрутов нет</p>
          )}

          {result.unassignedOrders.length > 0 && (
            <>
              <p className={styles.sectionTitle}>
                не распределены ({result.unassignedOrders.length})
              </p>
              {result.unassignedOrders.map((order) => (
                <div key={order.externalOrderId} className={styles.unassigned}>
                  <p>{order.address}</p>
                  <p className={styles.reason}>{order.reasonDescription}</p>
                </div>
              ))}
            </>
          )}
        </div>
      </div>
    </div>
  )
}
