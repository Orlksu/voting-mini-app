import styles from './Header.module.css'

type HeaderProps = {
  isBrigadesOpen: boolean
  onBrigadesClick: () => void
  onCloseBrigades: () => void
}

export default function Header({
  isBrigadesOpen,
  onBrigadesClick,
  onCloseBrigades,
}: HeaderProps) {
  return (
    <div className={styles.headerContainer}>
      <p className={styles.title}>SMART ROUTING</p>
      {isBrigadesOpen ? (
        <div className={styles.sectionControls}>
          <p className={styles.sectionTitle}>бригады</p>
          <p
            className={styles.closeButton}
            aria-label="Закрыть список бригад"
            onClick={onCloseBrigades}
          >
            ×
          </p>
        </div>
      ) : (
        <nav>
          <button type="button" onClick={onBrigadesClick}>
            бригады
          </button>
          <button type="button">заявки</button>
        </nav>
      )}
      <p className={styles.uploadCsv}>загрузить заявки</p>
    </div>
  )
}
