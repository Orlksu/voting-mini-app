import type { ReactNode } from 'react'
import styles from './JobProgress.module.css'

type JobProgressStackProps = {
  children: ReactNode
}

export default function JobProgressStack({ children }: JobProgressStackProps) {
  return <div className={styles.stack}>{children}</div>
}
