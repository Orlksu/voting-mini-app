import { useEffect, useRef, useState } from 'react'
import styles from './Header.module.css'

type HeaderProps = {
  isBrigadesOpen: boolean
  onBrigadesClick: () => void
  onCloseBrigades: () => void
  isRequestsOpen: boolean
  onRequestsClick: () => void
}

export default function Header({
  isBrigadesOpen,
  onBrigadesClick,
  onCloseBrigades,
  isRequestsOpen,
  onRequestsClick,
}: HeaderProps) {
  const [isUploadOpen, setIsUploadOpen] = useState(false)
  const uploadRef = useRef<HTMLDivElement>(null)

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
              <p>
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
      </div>
    </div>
  )
}
