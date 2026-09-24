import styles from './EngineerBlock.module.css'
import type { Engineer } from '../../../types/engineer.ts'

interface EngineerBlockProps {
  engineer: Engineer
  isActive: boolean
  isDetails: boolean
  onClick: () => void
  onDetailsClick: () => void
}

export default function EngineerBlock({
  engineer,
  isActive,
  isDetails,
  onClick,
  onDetailsClick,
}: EngineerBlockProps) {
  return (
    <div
      className={`${styles.engineerBlock} ${isActive ? styles.active : ''} ${
        isDetails ? styles.details : ''
      }`}
      onClick={isDetails ? undefined : onClick}
    >
      <div className={styles.mainContent}>
        <div className={styles.circle}></div>
        <div>
          <p className={styles.nameBrigade}>
            бригада №{engineer.externalId.split('-')[1]}
          </p>
          <p className={styles.nameEngineer}>{engineer.name}</p>
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
          подробнее
        </button>
      )}
    </div>
  )
}
