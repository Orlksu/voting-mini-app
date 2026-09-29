import { useEffect, useState } from 'react'
import styles from './UploadCsvModal.module.css'
import { orderApi } from '../../logic/orderApi.ts'
import CsvUploadForm from '../CsvUploadForm/CsvUploadForm.tsx'
import type { CsvUpload } from '../CsvUploadForm/CsvUploadForm.tsx'

type UploadCsvModalProps = {
  isOpen: boolean
  onClose: () => void
  onUploaded: (uploads: CsvUpload[], isAllUploaded: boolean) => void
}

export default function UploadCsvModal({
  isOpen,
  onClose,
  onUploaded,
}: UploadCsvModalProps) {
  const [isUploading, setIsUploading] = useState(false)

  const handleClose = () => {
    if (!isUploading) onClose()
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
        <p className={styles.title}>загрузка заявок</p>
        <CsvUploadForm
          upload={orderApi.uploadCsv}
          onUploaded={onUploaded}
          onBusyChange={setIsUploading}
        />
      </div>
    </div>
  )
}
