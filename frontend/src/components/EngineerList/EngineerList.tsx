import styles from './EngineerList.module.css'
import type { Engineer } from '../../types/engineer'
import EngineerBlock from './EngineerBlock/EngineerBlock.tsx'
import { useState } from 'react'
import EngineerDetails from './EngineerDetails/EngineerDetails.tsx'

export type EngineerRouteStatus =
  'idle' | 'loading' | 'ready' | 'empty' | 'notFound'

type EngineerListProps = {
  engineers: Engineer[]
  isLoading: boolean
  error: string | null
  activeEngineerId: string | null
  routeStatus: EngineerRouteStatus
  activeRouteOrdersCount: number | null
  onActiveEngineerChange: (engineerId: string | null) => void
  onAddClick: () => void
  onDelete: (engineer: Engineer) => Promise<void>
}

export default function EngineerList({
  engineers,
  isLoading,
  error,
  activeEngineerId,
  routeStatus,
  activeRouteOrdersCount,
  onActiveEngineerChange,
  onAddClick,
  onDelete,
}: EngineerListProps) {
  const [detailsEngineerId, setDetailsEngineerId] = useState<string | null>(
    null,
  )

  const openedDetailsId =
    detailsEngineerId === activeEngineerId ? detailsEngineerId : null

  const sortedEngineers = [...engineers].sort((a, b) =>
    a.externalId.localeCompare(b.externalId, 'ru', { numeric: true }),
  )
  const visibleEngineers = openedDetailsId
    ? sortedEngineers.filter((engineer) => engineer.id === openedDetailsId)
    : sortedEngineers

  return (
    <div className={styles.engineerListContainer}>
      {isLoading && <p>Загрузка...</p>}
      {error && <p role="alert">{error}</p>}
      {!isLoading && !error && (
        <>
          {!openedDetailsId && (
            <div className={styles.titleRow}>
              <p className={styles.titleList}>список бригад</p>
              <button
                type="button"
                className={styles.addButton}
                onClick={onAddClick}
                aria-label="Добавить бригады"
                title="Добавить бригады"
              >
                <span className="material-symbols-rounded">add</span>
              </button>
            </div>
          )}
          {!openedDetailsId && engineers.length === 0 && (
            <p className={styles.empty}>бригад пока нет</p>
          )}
          <div className={styles.listScrollArea}>
            <div className={styles.listScrollViewport}>
              {visibleEngineers.map((engineer) => (
                <EngineerBlock
                  key={engineer.id}
                  engineer={engineer}
                  isActive={engineer.id === activeEngineerId}
                  isDetails={engineer.id === openedDetailsId}
                  routeStatus={
                    engineer.id === activeEngineerId ? routeStatus : 'idle'
                  }
                  onClick={() =>
                    onActiveEngineerChange(
                      activeEngineerId === engineer.id ? null : engineer.id,
                    )
                  }
                  onDetailsClick={() => {
                    setDetailsEngineerId(
                      openedDetailsId === engineer.id ? null : engineer.id,
                    )
                  }}
                />
              ))}
              {openedDetailsId && (
                <EngineerDetails
                  engineer={visibleEngineers[0]}
                  assignedOrdersCount={activeRouteOrdersCount}
                  onDelete={onDelete}
                />
              )}
            </div>
          </div>
        </>
      )}
    </div>
  )
}
