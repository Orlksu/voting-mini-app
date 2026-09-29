import { useState } from 'react'
import type { FormEvent } from 'react'
import styles from '../forms/Form.module.css'
import AddressInput from '../forms/AddressInput.tsx'
import { matchKnownDistrict, uniqueDistricts } from '../forms/district.ts'
import { orderApi } from '../../logic/orderApi.ts'
import type { AddressSuggestion } from '../../logic/geocodingApi.ts'
import type {
  CreateWorkOrderRequest,
  WorkOrder,
} from '../../types/workOrder.ts'

type Skill = CreateWorkOrderRequest['requiredSkill']
type Priority = CreateWorkOrderRequest['priority']

const SKILL_OPTIONS: { value: Skill; label: string }[] = [
  { value: 'LocalRepair', label: 'локальный ремонт' },
  { value: 'Installation', label: 'установка' },
  { value: 'Emergency', label: 'аварийные работы' },
]

const PRIORITY_OPTIONS: { value: Priority; label: string }[] = [
  { value: 'Low', label: 'низкий' },
  { value: 'Normal', label: 'обычный' },
  { value: 'High', label: 'высокий' },
  { value: 'Emergency', label: 'авария' },
]

const MSK_OFFSET = '+03:00'

function todayInMoscow() {
  return new Intl.DateTimeFormat('en-CA', { timeZone: 'Europe/Moscow' }).format(
    new Date(),
  )
}

function toMskIso(date: string, time: string) {
  return `${date}T${time}:00${MSK_OFFSET}`
}

type OrderFormProps = {
  knownDistricts: string[]
  onCreated: (order: WorkOrder) => Promise<void>
  onBusyChange?: (isBusy: boolean) => void
}

export default function OrderForm({
  knownDistricts,
  onCreated,
  onBusyChange,
}: OrderFormProps) {
  const districts = uniqueDistricts(knownDistricts)

  const [address, setAddress] = useState('')
  const [point, setPoint] = useState<{
    latitude: number
    longitude: number
  } | null>(null)
  const [district, setDistrict] = useState('')
  const [isDistrictAuto, setIsDistrictAuto] = useState(false)
  const [externalId, setExternalId] = useState('')
  const [date, setDate] = useState(todayInMoscow)
  const [windowStart, setWindowStart] = useState('09:00')
  const [windowEnd, setWindowEnd] = useState('18:00')
  const [taskType, setTaskType] = useState('')
  const [skill, setSkill] = useState<Skill>('LocalRepair')
  const [priority, setPriority] = useState<Priority>('Normal')
  const [duration, setDuration] = useState(30)
  const [needsCar, setNeedsCar] = useState(false)
  const [isSaving, setIsSaving] = useState(false)
  const [error, setError] = useState<string | null>(null)

  const handleAddressChange = (value: string) => {
    setAddress(value)
    setPoint(null)
  }

  const handleAddressSelect = (suggestion: AddressSuggestion) => {
    setAddress(suggestion.label)
    setPoint({ latitude: suggestion.latitude, longitude: suggestion.longitude })

    if (suggestion.district && (district.trim() === '' || isDistrictAuto)) {
      setDistrict(matchKnownDistrict(suggestion.district, districts))
      setIsDistrictAuto(true)
    }
  }

  const isWindowValid = windowEnd > windowStart
  const isValid =
    address.trim() !== '' &&
    date !== '' &&
    isWindowValid &&
    duration >= 1 &&
    duration <= 720

  const handleSubmit = async (event: FormEvent) => {
    event.preventDefault()
    if (!isValid || isSaving) return

    setIsSaving(true)
    onBusyChange?.(true)
    setError(null)

    try {
      const created = await orderApi.create({
        externalId: externalId.trim() || undefined,
        district: district.trim() || undefined,
        address: address.trim(),
        latitude: point?.latitude,
        longitude: point?.longitude,
        windowStart: toMskIso(date, windowStart),
        windowEnd: toMskIso(date, windowEnd),
        requiredSkill: skill,
        requiredTransport: needsCar ? 'Car' : null,
        priority,
        taskType: taskType.trim() || undefined,
        durationMinutes: duration,
      })
      await onCreated(created)
    } catch (saveError) {
      setError(
        saveError instanceof Error
          ? saveError.message
          : 'Не удалось создать заявку',
      )
    } finally {
      setIsSaving(false)
      onBusyChange?.(false)
    }
  }

  return (
    <form className={styles.form} onSubmit={handleSubmit}>
      <label className={styles.field}>
        <span>адрес</span>
        <AddressInput
          value={address}
          onChange={handleAddressChange}
          onSelect={handleAddressSelect}
        />
      </label>

      <div className={styles.row}>
        <label className={styles.field}>
          <span>район</span>
          <input
            value={district}
            onChange={(event) => {
              setDistrict(event.target.value)
              setIsDistrictAuto(false)
            }}
            list="order-districts"
            placeholder="пусто — ближайший офис"
          />
          {isDistrictAuto && (
            <span className={styles.districtHint}>определён по адресу</span>
          )}
          <datalist id="order-districts">
            {districts.map((item) => (
              <option key={item} value={item} />
            ))}
          </datalist>
        </label>
        <label className={styles.field}>
          <span>номер заявки</span>
          <input
            value={externalId}
            onChange={(event) => setExternalId(event.target.value)}
            placeholder="сгенерируется сам"
            maxLength={100}
          />
        </label>
      </div>

      <div className={styles.row}>
        <label className={styles.field}>
          <span>дата</span>
          <input
            type="date"
            value={date}
            onChange={(event) => setDate(event.target.value)}
            required
          />
        </label>
        <label className={styles.field}>
          <span>окно с</span>
          <input
            type="time"
            value={windowStart}
            onChange={(event) => setWindowStart(event.target.value)}
            required
          />
        </label>
        <label className={styles.field}>
          <span>до</span>
          <input
            type="time"
            value={windowEnd}
            onChange={(event) => setWindowEnd(event.target.value)}
            required
          />
        </label>
      </div>
      {!isWindowValid && (
        <p className={styles.fieldError}>
          окончание окна должно быть позже начала
        </p>
      )}

      <div className={styles.row}>
        <label className={styles.field}>
          <span>тип работ</span>
          <input
            value={taskType}
            onChange={(event) => setTaskType(event.target.value)}
            placeholder="например, Нет связи"
            maxLength={100}
          />
        </label>
        <label className={`${styles.field} ${styles.fieldNarrow}`}>
          <span>длительность, мин</span>
          <input
            type="number"
            min={1}
            max={720}
            value={duration}
            onChange={(event) => setDuration(Number(event.target.value))}
            required
          />
        </label>
      </div>

      <fieldset className={styles.skills} role="radiogroup">
        <legend>квалификация</legend>
        {SKILL_OPTIONS.map((option) => (
          <label
            key={option.value}
            className={`${styles.skill} ${skill === option.value ? styles.skillActive : ''}`}
          >
            <input
              type="radio"
              name="order-skill"
              checked={skill === option.value}
              onChange={() => setSkill(option.value)}
            />
            {option.label}
          </label>
        ))}
      </fieldset>

      <fieldset className={styles.skills} role="radiogroup">
        <legend>приоритет</legend>
        {PRIORITY_OPTIONS.map((option) => (
          <label
            key={option.value}
            className={`${styles.skill} ${priority === option.value ? styles.skillActive : ''} ${option.value === 'Emergency' ? styles.skillDanger : ''}`}
          >
            <input
              type="radio"
              name="order-priority"
              checked={priority === option.value}
              onChange={() => setPriority(option.value)}
            />
            {option.label}
          </label>
        ))}
      </fieldset>

      <fieldset className={styles.skills}>
        <legend>транспорт</legend>
        <label
          className={`${styles.skill} ${needsCar ? styles.skillActive : ''}`}
        >
          <input
            type="checkbox"
            checked={needsCar}
            onChange={(event) => setNeedsCar(event.target.checked)}
          />
          нужен автомобиль
        </label>
      </fieldset>

      {error && (
        <p className={styles.error} role="alert">
          {error}
        </p>
      )}

      <button
        type="submit"
        className={styles.submit}
        disabled={!isValid || isSaving}
      >
        {isSaving ? 'создание...' : 'создать заявку'}
      </button>
    </form>
  )
}
