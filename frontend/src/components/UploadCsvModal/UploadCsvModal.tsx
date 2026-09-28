import { useEffect, useState } from 'react'
import type { ChangeEvent, DragEvent } from 'react'
import styles from './UploadCsvModal.module.css'
import { orderApi } from '../../logic/orderApi.ts'

type UploadCsvModalProps = {
  isOpen: boolean
  onClose: () => void
  onUploaded: (
    uploads: { jobId: string; fileName: string }[],
    isAllUploaded: boolean,
  ) => void
}

function isCsv(file: File) {
  return file.name.toLowerCase().endsWith('.csv')
}

export default function UploadCsvModal({
  isOpen,
  onClose,
  onUploaded,
}: UploadCsvModalProps) {
  const [files, setFiles] = useState<File[]>([])
  const [isDragOver, setIsDragOver] = useState(false)
  const [isUploading, setIsUploading] = useState(false)
  const [error, setError] = useState<string | null>(null)

  const handleClose = () => {
    if (isUploading) return

    setFiles([])
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

  const addFiles = (selected: FileList | null | undefined) => {
    if (!selected || selected.length === 0) return

    const list = Array.from(selected)
    const csvFiles = list.filter(isCsv)

    setError(
      csvFiles.length < list.length ? 'Пропущены файлы не в формате CSV' : null,
    )
    setFiles((current) => [
      ...current,
      ...csvFiles.filter(
        (file) =>
          !current.some(
            (item) => item.name === file.name && item.size === file.size,
          ),
      ),
    ])
  }

  const removeFile = (index: number) => {
    setFiles((current) => current.filter((_, i) => i !== index))
  }

  const handleDragOver = (event: DragEvent<HTMLLabelElement>) => {
    event.preventDefault()
    setIsDragOver(true)
  }

  const handleDrop = (event: DragEvent<HTMLLabelElement>) => {
    event.preventDefault()
    setIsDragOver(false)
    addFiles(event.dataTransfer.files)
  }

  const handleInputChange = (event: ChangeEvent<HTMLInputElement>) => {
    addFiles(event.target.files)
    event.target.value = ''
  }

  const handleSubmit = async () => {
    if (files.length === 0) return

    setIsUploading(true)
    setError(null)

    const results = await Promise.allSettled(
      files.map((file) => orderApi.uploadCsv(file)),
    )

    const uploads: { jobId: string; fileName: string }[] = []
    const failedFiles: File[] = []
    const errors: string[] = []

    results.forEach((result, index) => {
      const file = files[index]
      if (result.status === 'fulfilled') {
        uploads.push({ jobId: result.value.jobId, fileName: file.name })
      } else {
        failedFiles.push(file)
        const reason =
          result.reason instanceof Error
            ? result.reason.message
            : 'не удалось загрузить'
        errors.push(`${file.name}: ${reason}`)
      }
    })

    setIsUploading(false)
    setFiles(failedFiles)
    setError(errors.length > 0 ? errors.join('\n') : null)
    if (uploads.length > 0) {
      onUploaded(uploads, failedFiles.length === 0)
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
            multiple
            onChange={handleInputChange}
          />
          <span className={`material-symbols-rounded ${styles.icon}`}>
            upload_file
          </span>
          <p>перетащите CSV-файлы сюда или нажмите, чтобы выбрать</p>
        </label>

        {files.length > 0 && (
          <ul className={styles.files}>
            {files.map((file, index) => (
              <li key={`${file.name}-${file.size}`} className={styles.fileItem}>
                <span className={styles.fileName}>{file.name}</span>
                <button
                  type="button"
                  className={styles.removeFile}
                  disabled={isUploading}
                  onClick={() => removeFile(index)}
                  aria-label={`Убрать ${file.name}`}
                >
                  <span className="material-symbols-rounded">close</span>
                </button>
              </li>
            ))}
          </ul>
        )}

        {error && (
          <p className={styles.error} role="alert">
            {error}
          </p>
        )}

        <button
          type="button"
          className={styles.submit}
          disabled={files.length === 0 || isUploading}
          onClick={handleSubmit}
        >
          {isUploading
            ? 'отправка...'
            : files.length > 1
              ? `отправить (${files.length})`
              : 'отправить'}
        </button>
      </div>
    </div>
  )
}
