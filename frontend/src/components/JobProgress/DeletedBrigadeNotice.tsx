import styles from './JobProgress.module.css'
import JobProgressCard from './JobProgressCard.tsx'

type DeletedBrigadeNoticeProps = {
  name: string
  assignedOrdersCount: number | null
  onClose: () => void
  onReplan: () => void
}

export default function DeletedBrigadeNotice({
  name,
  assignedOrdersCount,
  onClose,
  onReplan,
}: DeletedBrigadeNoticeProps) {
  const hasOrphanedOrders = assignedOrdersCount !== 0

  return (
    <JobProgressCard title="бригада удалена" onClose={onClose}>
      <p className={styles.stage}>{name}</p>
      {hasOrphanedOrders && (
        <>
          <p className={styles.result}>
            {assignedOrdersCount === null
              ? 'её заявки остались без исполнителя'
              : `без исполнителя: ${assignedOrdersCount}`}
          </p>
          <div className={styles.actions}>
            <button
              type="button"
              className={styles.primaryAction}
              onClick={onReplan}
            >
              перераспределить
            </button>
          </div>
        </>
      )}
    </JobProgressCard>
  )
}
