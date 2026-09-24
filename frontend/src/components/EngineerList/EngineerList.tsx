import styles from './EngineerList.module.css'
import type { Engineer } from '../../types/engineer'
import EngineerBlock from './EngineerBlock/EngineerBlock.tsx'
import { useState } from 'react'
import EngineerDetails from './EngineerDetails/EngineerDetails.tsx'

type EngineerListProps = {
  engineers: Engineer[]
  isLoading: boolean
  error: string | null
}

export default function EngineerList({
  engineers,
  isLoading,
  error,
}: EngineerListProps) {
  const [activeEngineerId, setActiveEngineerId] = useState<string | null>(null)
  const [detailsEngineerId, setDetailsEngineerId] = useState<string | null>(
    null,
  )

  const visibleEngineers = detailsEngineerId
    ? engineers.filter((engineer) => engineer.id === detailsEngineerId)
    : engineers

  return (
    <div className={styles.engineerListContainer}>
      {isLoading && <p>Загрузка...</p>}
      {error && <p role="alert">{error}</p>}
      {!isLoading && !error && (
        <>
          {!detailsEngineerId && (
            <p className={styles.titleList}>список бригад</p>
          )}
          {visibleEngineers.map((engineer) => (
            <EngineerBlock
              key={engineer.id}
              engineer={engineer}
              isActive={engineer.id === activeEngineerId}
              isDetails={engineer.id === detailsEngineerId}
              onClick={() =>
                setActiveEngineerId((currentId) =>
                  currentId === engineer.id ? null : engineer.id,
                )
              }
              onDetailsClick={() => {
                setActiveEngineerId(null)
                setDetailsEngineerId(engineer.id)
              }}
            />
          ))}
          {detailsEngineerId && (
            <EngineerDetails engineer={visibleEngineers[0]} />
          )}
        </>
      )}
    </div>
  )
}
