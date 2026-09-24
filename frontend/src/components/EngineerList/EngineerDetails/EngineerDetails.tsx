import styles from './EngineerDetails.module.css'
import type { Engineer } from '../../../types/engineer.ts'

interface EngineerDetailsProps {
  engineer?: Engineer
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

export default function EngineerDetails({ engineer }: EngineerDetailsProps) {
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
          `${new Date(engineer.shiftStart).toLocaleTimeString('ru-RU', {
            hour: '2-digit',
            minute: '2-digit',
          })}-${new Date(engineer.shiftEnd).toLocaleTimeString('ru-RU', {
            hour: '2-digit',
            minute: '2-digit',
          })}`,
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
    </div>
  )
}
