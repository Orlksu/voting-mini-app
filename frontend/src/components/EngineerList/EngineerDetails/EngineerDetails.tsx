import styles from './EngineerDetails.module.css'
import type { Engineer } from '../../../types/engineer.ts'

interface EngineerDetailsProps {
  engineer?: Engineer
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
  return (
    <div
      className={styles.engineerDetailsContainer}
      data-engineer-id={engineer?.id}
    >
      <EngineerLabel label={'транспорт'} fields={[engineer!.transport]} />
      <EngineerLabel
        label={'смена'}
        fields={[
          `${new Date(engineer!.shiftStart).toLocaleTimeString('ru-RU', {
            hour: '2-digit',
            minute: '2-digit',
          })}-${new Date(engineer!.shiftEnd).toLocaleTimeString('ru-RU', {
            hour: '2-digit',
            minute: '2-digit',
          })}`,
        ]}
      />
      <EngineerLabel label={'навыки'} fields={engineer!.skills} />
      <EngineerLabel
        label={'текущий статус'}
        fields={[!engineer!.isAvailable ? 'на вызове' : 'на воле']}
      />
    </div>
  )
}
