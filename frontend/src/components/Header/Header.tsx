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
  return (
    <div className={styles.headerContainer}>
      <p className={styles.title}>SMART ROUTING</p>
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
    </div>
  )
}
