import { useEffect, useState } from 'react'
import type { ReactNode } from 'react'
import styles from './ImportModal.module.css'
import CsvUploadForm from '../CsvUploadForm/CsvUploadForm.tsx'
import type { CsvUpload } from '../CsvUploadForm/CsvUploadForm.tsx'

export type ImportModalTab = 'csv' | 'manual'

type ImportModalProps = {
  title: string
  tab: ImportModalTab | null
  onTabChange: (tab: ImportModalTab) => void
  onClose: () => void
  csv: {
    upload: (file: File) => Promise<{ jobId: string }>
    onUploaded: (uploads: CsvUpload[], isAllUploaded: boolean) => void
    hint?: string
  }
  renderManual: (props: {
    onBusyChange: (isBusy: boolean) => void
  }) => ReactNode
}

export default function ImportModal({
  title,
  tab,
  onTabChange,
  onClose,
  csv,
  renderManual,
}: ImportModalProps) {
  const [isBusy, setIsBusy] = useState(false)

  const handleClose = () => {
    if (!isBusy) onClose()
  }

  useEffect(() => {
    if (!tab) return

    const handleKeyDown = (event: KeyboardEvent) => {
      if (event.key === 'Escape') handleClose()
    }

    document.addEventListener('keydown', handleKeyDown)
    return () => document.removeEventListener('keydown', handleKeyDown)
  })

  if (!tab) return null

  return (
    <div className={styles.overlay} onClick={handleClose}>
      <div
        className={styles.modal}
        role="dialog"
        aria-modal="true"
        onClick={(event) => event.stopPropagation()}
      >
        <p className={styles.title}>{title}</p>

        <div className={styles.tabs} role="tablist">
          <button
            type="button"
            role="tab"
            aria-selected={tab === 'csv'}
            className={tab === 'csv' ? styles.tabActive : undefined}
            disabled={isBusy}
            onClick={() => onTabChange('csv')}
          >
            из CSV
          </button>
          <button
            type="button"
            role="tab"
            aria-selected={tab === 'manual'}
            className={tab === 'manual' ? styles.tabActive : undefined}
            disabled={isBusy}
            onClick={() => onTabChange('manual')}
          >
            вручную
          </button>
        </div>

        {tab === 'csv' ? (
          <CsvUploadForm
            upload={csv.upload}
            onUploaded={csv.onUploaded}
            onBusyChange={setIsBusy}
            hint={csv.hint}
          />
        ) : (
          renderManual({ onBusyChange: setIsBusy })
        )}
      </div>
    </div>
  )
}
