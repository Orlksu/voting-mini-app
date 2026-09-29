import { useEffect, useState } from 'react'
import styles from './AddBrigadesModal.module.css'
import CsvUploadForm from '../CsvUploadForm/CsvUploadForm.tsx'
import type { CsvUpload } from '../CsvUploadForm/CsvUploadForm.tsx'
import BrigadeForm from './BrigadeForm.tsx'
import { engineersApi } from '../../logic/engineerApi.ts'
import type { Engineer } from '../../types/engineer'

type Tab = 'csv' | 'manual'

type AddBrigadesModalProps = {
  isOpen: boolean
  engineers: Engineer[]
  knownDistricts: string[]
  onClose: () => void
  onUploaded: (uploads: CsvUpload[], isAllUploaded: boolean) => void
  onCreated: (externalId: string) => Promise<void>
}

export default function AddBrigadesModal({
  isOpen,
  engineers,
  knownDistricts,
  onClose,
  onUploaded,
  onCreated,
}: AddBrigadesModalProps) {
  const [tab, setTab] = useState<Tab>('csv')
  const [isBusy, setIsBusy] = useState(false)

  const handleClose = () => {
    if (!isBusy) onClose()
  }

  useEffect(() => {
    if (!isOpen) return

    const handleKeyDown = (event: KeyboardEvent) => {
      if (event.key === 'Escape') handleClose()
    }

    document.addEventListener('keydown', handleKeyDown)
    return () => document.removeEventListener('keydown', handleKeyDown)
  })

  if (!isOpen) return null

  return (
    <div className={styles.overlay} onClick={handleClose}>
      <div
        className={styles.modal}
        role="dialog"
        aria-modal="true"
        onClick={(event) => event.stopPropagation()}
      >
        <p className={styles.title}>добавить бригады</p>

        <div className={styles.tabs} role="tablist">
          <button
            type="button"
            role="tab"
            aria-selected={tab === 'csv'}
            className={tab === 'csv' ? styles.tabActive : undefined}
            disabled={isBusy}
            onClick={() => setTab('csv')}
          >
            из CSV
          </button>
          <button
            type="button"
            role="tab"
            aria-selected={tab === 'manual'}
            className={tab === 'manual' ? styles.tabActive : undefined}
            disabled={isBusy}
            onClick={() => setTab('manual')}
          >
            вручную
          </button>
        </div>

        {tab === 'csv' ? (
          <CsvUploadForm
            upload={engineersApi.uploadCsv}
            onUploaded={onUploaded}
            onBusyChange={setIsBusy}
            hint="перетащите CSV-файлы с бригадами сюда или нажмите, чтобы выбрать"
          />
        ) : (
          <BrigadeForm
            engineers={engineers}
            knownDistricts={knownDistricts}
            onCreated={onCreated}
            onBusyChange={setIsBusy}
          />
        )}
      </div>
    </div>
  )
}
