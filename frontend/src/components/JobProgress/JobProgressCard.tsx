import type { ReactNode } from 'react'
import styles from './JobProgress.module.css'

type JobProgressCardProps = {
  title: string
  onClose?: () => void
  children: ReactNode
}

export default function JobProgressCard({
  title,
  onClose,
  children,
}: JobProgressCardProps) {
  return (
    <div className={styles.container}>
      <div className={styles.header}>
        <p className={styles.title}>{title}</p>
        {onClose && (
          <button type="button" className={styles.close} onClick={onClose}>
            <span className="material-symbols-rounded">close</span>
          </button>
        )}
      </div>
      {children}
    </div>
  )
}
