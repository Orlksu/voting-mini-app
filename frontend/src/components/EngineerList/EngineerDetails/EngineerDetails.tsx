import { useState } from 'react'
import styles from './EngineerDetails.module.css'
import type { Engineer } from '../../../types/engineer.ts'

interface EngineerDetailsProps {
  engineer?: Engineer
  assignedOrdersCount: number | null
  onDelete: (engineer: Engineer) => Promise<void>
}

function ordersWord(count: number) {
  const mod10 = count % 10
  const mod100 = count % 100
  if (mod10 === 1 && mod100 !== 11) return 'заявка'
  if (mod10 >= 2 && mod10 <= 4 && (mod100 < 12 || mod100 > 14)) return 'заявки'
  return 'заявок'
}

function DeleteBrigade({
  engineer,
  assignedOrdersCount,
  onDelete,
}: Required<Omit<EngineerDetailsProps, 'engineer'>> & { engineer: Engineer }) {
  const [isConfirming, setIsConfirming] = useState(false)
  const [isDeleting, setIsDeleting] = useState(false)
  const [error, setError] = useState<string | null>(null)

  const handleDelete = async () => {
    setIsDeleting(true)
    setError(null)
    try {
      await onDelete(engineer)
    } catch (deleteError) {
      setError(
        deleteError instanceof Error
          ? deleteError.message
          : 'Не удалось удалить бригаду',
      )
      setIsDeleting(false)
    }
  }

  if (!isConfirming) {
    return (
      <button
        type="button"
        className={styles.deleteButton}
        onClick={() => setIsConfirming(true)}
      >
        <span className="material-symbols-rounded">delete</span>
        удалить бригаду
      </button>
    )
  }

  return (
    <div className={styles.deleteConfirm} role="alertdialog">
      <p className={styles.deleteTitle}>
        <span className="material-symbols-rounded">warning</span>
        удалить {engineer.name}?
      </p>
      <ul className={styles.deleteConsequences}>
        <li>бригада удалится без возможности восстановления</li>
        <li>
          {assignedOrdersCount === null
            ? 'назначенные ей заявки останутся без исполнителя'
            : assignedOrdersCount > 0
              ? `${assignedOrdersCount} ${ordersWord(assignedOrdersCount)} останутся без исполнителя`
              : 'назначенных заявок нет'}
        </li>
        <li>после удаления стоит запустить перераспределение</li>
      </ul>
      {error && (
        <p className={styles.deleteError} role="alert">
          {error}
        </p>
      )}
      <div className={styles.deleteActions}>
        <button
          type="button"
          className={styles.cancelButton}
          disabled={isDeleting}
          onClick={() => {
            setIsConfirming(false)
            setError(null)
          }}
        >
          отмена
        </button>
        <button
          type="button"
          className={styles.confirmDeleteButton}
          disabled={isDeleting}
          onClick={handleDelete}
        >
          {isDeleting ? 'удаление...' : 'удалить'}
        </button>
      </div>
    </div>
  )
}

const transportLabels: Record<string, string> = {
  Car: 'автомобиль',
  PublicTransit: 'общественный транспорт',
  Bicycle: 'велосипед',
  Pedestrian: 'пешком',
}

const skillLabels: Record<string, string> = {
  LocalRepair: 'локальный ремонт',
  Installation: 'установка',
  Emergency: 'аварийные работы',
}

const shiftTimeFormatter = new Intl.DateTimeFormat('ru-RU', {
  hour: '2-digit',
  minute: '2-digit',
  timeZone: 'Europe/Moscow',
})

const EngineerLabel = ({
  label,
  fields,
}: {
  label: string
  fields: string[]
}) => {
  const fieldsLayoutClass =
    fields.length === 1
      ? styles.fieldsSingle
      : fields.length === 2
        ? styles.fieldsDouble
        : fields.length === 3
          ? styles.fieldsTriple
          : styles.fieldsMultiple

  return (
    <>
      <div className={styles.engineerLabel}>
        <p className={styles.label}>{label}</p>
        <div className={`${styles.fields} ${fieldsLayoutClass}`}>
          {fields.map((field) => (
            <p className={styles.field} key={field}>
              {field}
            </p>
          ))}
        </div>
      </div>
    </>
  )
}

export default function EngineerDetails({
  engineer,
  assignedOrdersCount,
  onDelete,
}: EngineerDetailsProps) {
  if (!engineer) {
    return null
  }

  return (
    <div
      className={styles.engineerDetailsContainer}
      data-engineer-id={engineer.id}
    >
      <EngineerLabel
        label="транспорт"
        fields={[transportLabels[engineer.transport] ?? engineer.transport]}
      />
      <EngineerLabel
        label="смена"
        fields={[
          `${shiftTimeFormatter.format(new Date(engineer.shiftStart))}–${shiftTimeFormatter.format(new Date(engineer.shiftEnd))}`,
        ]}
      />
      <EngineerLabel
        label="навыки"
        fields={engineer.skills.map((skill) => skillLabels[skill] ?? skill)}
      />
      <EngineerLabel
        label="текущий статус"
        fields={[engineer.isAvailable ? 'свободен' : 'на вызове']}
      />
      <DeleteBrigade
        key={engineer.id}
        engineer={engineer}
        assignedOrdersCount={assignedOrdersCount}
        onDelete={onDelete}
      />
    </div>
  )
}
