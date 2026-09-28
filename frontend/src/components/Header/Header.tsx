import { useEffect, useRef, useState } from 'react'
import styles from './Header.module.css'

type HeaderProps = {
  isBrigadesOpen: boolean
  onBrigadesClick: () => void
  onCloseBrigades: () => void
  isRequestsOpen: boolean
  onRequestsClick: () => void
  onUploadFileClick: () => void
  onOptimizeClick: () => void
  onReplanClick: () => void
  isOptimizing: boolean
}

export default function Header({
  isBrigadesOpen,
  onBrigadesClick,
  onCloseBrigades,
  isRequestsOpen,
  onRequestsClick,
  onUploadFileClick,
  onOptimizeClick,
  onReplanClick,
  isOptimizing,
}: HeaderProps) {
  const [isUploadOpen, setIsUploadOpen] = useState(false)
  const uploadRef = useRef<HTMLDivElement>(null)
  const [isOptimizeMenuOpen, setIsOptimizeMenuOpen] = useState(false)
  const optimizeRef = useRef<HTMLDivElement>(null)

  useEffect(() => {
    if (!isOptimizeMenuOpen) return

    const handleClickOutside = (event: MouseEvent) => {
      if (!optimizeRef.current?.contains(event.target as Node)) {
        setIsOptimizeMenuOpen(false)
      }
    }

    document.addEventListener('mousedown', handleClickOutside)
    return () => document.removeEventListener('mousedown', handleClickOutside)
  }, [isOptimizeMenuOpen])

  useEffect(() => {
    if (!isUploadOpen) return

    const handleClickOutside = (event: MouseEvent) => {
      if (!uploadRef.current?.contains(event.target as Node)) {
        setIsUploadOpen(false)
      }
    }

    document.addEventListener('mousedown', handleClickOutside)
    return () => document.removeEventListener('mousedown', handleClickOutside)
  }, [isUploadOpen])

  return (
    <div className={styles.headerContainer}>
      <p className={styles.title}>SMART ROUTING</p>
      <div className={styles.buttons}>
        <nav>
          <p
            className={`${styles.brigades} ${isBrigadesOpen ? styles.active : ''}`}
            onClick={isBrigadesOpen ? onCloseBrigades : onBrigadesClick}
          >
            бригады
          </p>
          <p
            className={isRequestsOpen ? styles.active : ''}
            onClick={onRequestsClick}
          >
            текущие заявки
          </p>
        </nav>
        <div
          className={`${styles.upload} ${isUploadOpen ? styles.uploadOpen : ''}`}
          ref={uploadRef}
        >
          <p
            className={styles.uploadCsv}
            onClick={() => setIsUploadOpen((isOpen) => !isOpen)}
          >
            загрузить заявки{' '}
            <span
              className={`material-symbols-rounded ${styles.chevron} ${isUploadOpen ? styles.chevronUp : ''}`}
            >
              keyboard_arrow_down
            </span>
          </p>
          <div
            className={`${styles.uploadMenu} ${isUploadOpen ? styles.uploadMenuOpen : ''}`}
          >
            <div className={styles.uploadMenuInner}>
              <p
                onClick={() => {
                  setIsUploadOpen(false)
                  onUploadFileClick()
                }}
              >
                <span className="material-symbols-rounded">upload_file</span>
                загрузить из файла (CSV)
              </p>
              <p>
                <span className="material-symbols-rounded">edit_square</span>
                ввести вручную
              </p>
            </div>
          </div>
        </div>
        <div className={styles.optimizeGroup} ref={optimizeRef}>
          <button
            type="button"
            className={styles.optimize}
            disabled={isOptimizing}
            onClick={onOptimizeClick}
          >
            <span className="material-symbols-rounded">route</span>
            {isOptimizing ? 'распределение...' : 'распределить'}
          </button>
          <button
            type="button"
            className={styles.optimizeToggle}
            disabled={isOptimizing}
            aria-label="Другие варианты распределения"
            onClick={() => setIsOptimizeMenuOpen((isOpen) => !isOpen)}
          >
            <span
              className={`material-symbols-rounded ${styles.chevron} ${isOptimizeMenuOpen ? styles.chevronUp : ''}`}
            >
              keyboard_arrow_down
            </span>
          </button>
          <div
            className={`${styles.optimizeMenu} ${isOptimizeMenuOpen && !isOptimizing ? styles.optimizeMenuOpen : ''}`}
          >
            <button
              type="button"
              className={styles.optimizeMenuItem}
              onClick={() => {
                setIsOptimizeMenuOpen(false)
                onReplanClick()
              }}
            >
              <span className="material-symbols-rounded">sync</span>
              <span>
                <span className={styles.optimizeMenuTitle}>
                  перераспределить все
                </span>
                <span className={styles.optimizeMenuHint}>
                  пересобрать маршруты, включая уже назначенные заявки
                </span>
              </span>
            </button>
          </div>
        </div>
      </div>
    </div>
  )
}
