import { useEffect, useState } from 'react'
import type { ChangeEvent, DragEvent } from 'react'
import styles from './UploadCsvModal.module.css'
import { orderApi } from '../../logic/orderApi.ts'

type UploadCsvModalProps = {
  isOpen: boolean
  onClose: () => void
  onUploaded: (jobId: string) => void
}

export default function UploadCsvModal({
  isOpen,
  onClose,
  onUploaded,
}: UploadCsvModalProps) {
  const [file, setFile] = useState<File | null>(null)
  const [isDragOver, setIsDragOver] = useState(false)
  const [isUploading, setIsUploading] = useState(false)
  const [error, setError] = useState<string | null>(null)

  const handleClose = () => {
    if (isUploading) return

    setFile(null)
    setError(null)
    setIsDragOver(false)
    onClose()
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

  const selectFile = (selected: File | undefined) => {
    if (!selected) return

    if (!selected.name.toLowerCase().endsWith('.csv')) {
      setError('Нужен файл в формате CSV')
      return
    }

    setError(null)
    setFile(selected)
  }

  const handleDragOver = (event: DragEvent<HTMLLabelElement>) => {
    event.preventDefault()
    setIsDragOver(true)
  }

  const handleDrop = (event: DragEvent<HTMLLabelElement>) => {
    event.preventDefault()
    setIsDragOver(false)
    selectFile(event.dataTransfer.files[0])
  }

  const handleInputChange = (event: ChangeEvent<HTMLInputElement>) => {
    selectFile(event.target.files?.[0])
    event.target.value = ''
  }

  const handleSubmit = async () => {
    if (!file) return

    try {
      setIsUploading(true)
      setError(null)
      const job = await orderApi.uploadCsv(file)
      setFile(null)
      onUploaded(job.jobId)
    } catch (uploadError) {
      setError(
        uploadError instanceof Error
          ? uploadError.message
          : 'Не удалось загрузить файл',
      )
    } finally {
      setIsUploading(false)
    }
  }

  return (
    <div className={styles.overlay} onClick={handleClose}>
      <div
        className={styles.modal}
        role="dialog"
        aria-modal="true"
        onClick={(event) => event.stopPropagation()}
      >
        <p className={styles.title}>загрузка заявок</p>

        <label
          className={`${styles.dropzone} ${isDragOver ? styles.dropzoneActive : ''}`}
          onDragOver={handleDragOver}
          onDragLeave={() => setIsDragOver(false)}
          onDrop={handleDrop}
        >
          <input
            className={styles.input}
            type="file"
            accept=".csv,text/csv"
            onChange={handleInputChange}
          />
          <span className={`material-symbols-rounded ${styles.icon}`}>
            upload_file
          </span>
          {file ? (
            <p className={styles.fileName}>{file.name}</p>
          ) : (
            <p>перетащите CSV-файл сюда или нажмите, чтобы выбрать</p>
          )}
        </label>

        {error && (
          <p className={styles.error} role="alert">
            {error}
          </p>
        )}

        <button
          type="button"
          className={styles.submit}
          disabled={!file || isUploading}
          onClick={handleSubmit}
        >
          {isUploading ? 'отправка...' : 'отправить'}
        </button>
      </div>
    </div>
  )
}
