import { useEffect, useRef } from 'react'
import styles from './EngineerBlock.module.css'
import type { Engineer } from '../../../types/engineer.ts'
import type { EngineerRouteStatus } from '../EngineerList.tsx'

const ROUTE_STATUS_LABELS: Partial<Record<EngineerRouteStatus, string>> = {
  loading: 'загрузка маршрута...',
  empty: 'маршрут пуст',
  notFound: 'маршрут не найден',
}

interface EngineerBlockProps {
  engineer: Engineer
  isActive: boolean
  isDetails: boolean
  routeStatus: EngineerRouteStatus
  onClick: () => void
  onDetailsClick: () => void
}

export default function EngineerBlock({
  engineer,
  isActive,
  isDetails,
  routeStatus,
  onClick,
  onDetailsClick,
}: EngineerBlockProps) {
  const blockRef = useRef<HTMLDivElement>(null)
  const routeStatusLabel = ROUTE_STATUS_LABELS[routeStatus]

  useEffect(() => {
    if (isActive) {
      blockRef.current?.scrollIntoView({ block: 'nearest', behavior: 'smooth' })
    }
  }, [isActive])

  return (
    <div
      ref={blockRef}
      className={`${styles.engineerBlock} ${isActive ? styles.active : ''} ${
        isDetails ? styles.details : ''
      }`}
      onClick={isDetails ? undefined : onClick}
    >
      <div className={styles.mainContent}>
        <div className={styles.circle}></div>
        <div>
          <p className={styles.nameBrigade}>
            бригада №{engineer.externalId.split('-')[1] ?? engineer.externalId}
          </p>
          <p className={styles.nameEngineer}>{engineer.name}</p>
          {routeStatusLabel && (
            <p className={styles.routeStatus}>{routeStatusLabel}</p>
          )}
        </div>
      </div>
      {isActive && (
        <button
          type="button"
          className={styles.moreButton}
          onClick={(event) => {
            event.stopPropagation()
            onDetailsClick()
          }}
        >
          {isDetails ? 'назад' : 'подробнее'}
        </button>
      )}
    </div>
  )
}
