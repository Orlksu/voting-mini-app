import { useState } from 'react'
import type { FormEvent } from 'react'
import styles from './AddBrigadesModal.module.css'
import { engineersApi } from '../../logic/engineerApi.ts'
import type { Engineer } from '../../types/engineer'
import type { SkillType, TransportType } from '../../types/optimization.ts'
import { brigadeToCsvFile, nextEngineerExternalId } from './brigadeCsv.ts'
import AddressInput from './AddressInput.tsx'
import { normalizeDistrict } from '../../logic/geocodingApi.ts'
import type { AddressSuggestion } from '../../logic/geocodingApi.ts'

const TRANSPORT_OPTIONS: { value: TransportType; label: string }[] = [
  { value: 'Car', label: 'автомобиль' },
  { value: 'PublicTransit', label: 'общественный транспорт' },
  { value: 'Bicycle', label: 'велосипед' },
  { value: 'Pedestrian', label: 'пешком' },
]

const SKILL_OPTIONS: { value: SkillType; label: string }[] = [
  { value: 'LocalRepair', label: 'локальный ремонт' },
  { value: 'Installation', label: 'установка' },
  { value: 'Emergency', label: 'аварийные работы' },
]

function districtKey(value: string) {
  return normalizeDistrict(value).toLowerCase().replace(/ё/g, 'е')
}

type BrigadeFormProps = {
  engineers: Engineer[]
  knownDistricts: string[]
  onCreated: (externalId: string) => Promise<void>
  onBusyChange?: (isBusy: boolean) => void
}

export default function BrigadeForm({
  engineers,
  knownDistricts,
  onCreated,
  onBusyChange,
}: BrigadeFormProps) {
  const externalId = nextEngineerExternalId(engineers)
  const districts = [
    ...new Set(
      [...engineers.map((engineer) => engineer.district), ...knownDistricts]
        .map((item) => item?.trim())
        .filter(Boolean),
    ),
  ].sort((a, b) => a.localeCompare(b, 'ru'))

  const [name, setName] = useState('')
  const [district, setDistrict] = useState('')
  const [startAddress, setStartAddress] = useState('')
  const [startPoint, setStartPoint] = useState<{
    latitude: number
    longitude: number
  } | null>(null)
  const [isDistrictAuto, setIsDistrictAuto] = useState(false)
  const [shiftStart, setShiftStart] = useState('09:00')
  const [shiftEnd, setShiftEnd] = useState('18:00')
  const [transport, setTransport] = useState<TransportType>('Car')
  const [skills, setSkills] = useState<SkillType[]>(['LocalRepair'])
  const [maxOrders, setMaxOrders] = useState(15)
  const [isSaving, setIsSaving] = useState(false)
  const [error, setError] = useState<string | null>(null)

  const handleAddressChange = (value: string) => {
    setStartAddress(value)
    setStartPoint(null)
  }

  const handleAddressSelect = (suggestion: AddressSuggestion) => {
    setStartAddress(suggestion.label)
    setStartPoint({
      latitude: suggestion.latitude,
      longitude: suggestion.longitude,
    })

    if (suggestion.district && (district.trim() === '' || isDistrictAuto)) {
      const key = districtKey(suggestion.district)
      const known = districts.find((item) => districtKey(item) === key)
      setDistrict(known ?? suggestion.district)
      setIsDistrictAuto(true)
    }
  }

  const toggleSkill = (skill: SkillType) => {
    setSkills((current) =>
      current.includes(skill)
        ? current.filter((item) => item !== skill)
        : [...current, skill],
    )
  }

  const isValid =
    name.trim() !== '' &&
    startAddress.trim() !== '' &&
    district.trim() !== '' &&
    skills.length > 0 &&
    maxOrders > 0

  const handleSubmit = async (event: FormEvent) => {
    event.preventDefault()
    if (!isValid || isSaving) return

    setIsSaving(true)
    onBusyChange?.(true)
    setError(null)

    try {
      const result = await engineersApi.importCsvSync(
        brigadeToCsvFile({
          externalId,
          name: name.trim(),
          district: district.trim(),
          startAddress: startAddress.trim(),
          startLatitude: startPoint?.latitude ?? null,
          startLongitude: startPoint?.longitude ?? null,
          shiftStart,
          shiftEnd,
          transport,
          skills,
          maxOrdersPerShift: maxOrders,
        }),
      )

      if (result.imported === 0) {
        setError(
          result.warnings.length > 0
            ? result.warnings.join('\n')
            : 'Бригада не добавлена: проверьте адрес и время смены',
        )
        return
      }

      await onCreated(externalId)
    } catch (saveError) {
      setError(
        saveError instanceof Error
          ? saveError.message
          : 'Не удалось добавить бригаду',
      )
    } finally {
      setIsSaving(false)
      onBusyChange?.(false)
    }
  }

  return (
    <form className={styles.form} onSubmit={handleSubmit}>
      <p className={styles.formHint}>
        будет создана как <span className={styles.formId}>{externalId}</span>
      </p>

      <label className={styles.field}>
        <span>ФИО</span>
        <input
          value={name}
          onChange={(event) => setName(event.target.value)}
          placeholder="Иванов Иван Иванович"
          required
        />
      </label>

      <label className={styles.field}>
        <span>адрес выезда</span>
        <AddressInput
          value={startAddress}
          onChange={handleAddressChange}
          onSelect={handleAddressSelect}
        />
      </label>

      <label className={styles.field}>
        <span>район</span>
        <input
          value={district}
          onChange={(event) => {
            setDistrict(event.target.value)
            setIsDistrictAuto(false)
          }}
          list="brigade-districts"
          placeholder="например, Центральный"
          required
        />
        {isDistrictAuto && (
          <span className={styles.districtHint}>определён по адресу</span>
        )}
        <datalist id="brigade-districts">
          {districts.map((item) => (
            <option key={item} value={item} />
          ))}
        </datalist>
      </label>

      <div className={styles.row}>
        <label className={styles.field}>
          <span>смена с</span>
          <input
            type="time"
            value={shiftStart}
            onChange={(event) => setShiftStart(event.target.value)}
            required
          />
        </label>
        <label className={styles.field}>
          <span>до</span>
          <input
            type="time"
            value={shiftEnd}
            onChange={(event) => setShiftEnd(event.target.value)}
            required
          />
        </label>
        <label className={styles.field}>
          <span>лимит заявок</span>
          <input
            type="number"
            min={1}
            max={100}
            value={maxOrders}
            onChange={(event) => setMaxOrders(Number(event.target.value))}
            required
          />
        </label>
      </div>

      <label className={styles.field}>
        <span>транспорт</span>
        <select
          value={transport}
          onChange={(event) =>
            setTransport(event.target.value as TransportType)
          }
        >
          {TRANSPORT_OPTIONS.map((option) => (
            <option key={option.value} value={option.value}>
              {option.label}
            </option>
          ))}
        </select>
      </label>

      <fieldset className={styles.skills}>
        <legend>навыки</legend>
        {SKILL_OPTIONS.map((option) => (
          <label
            key={option.value}
            className={`${styles.skill} ${skills.includes(option.value) ? styles.skillActive : ''}`}
          >
            <input
              type="checkbox"
              checked={skills.includes(option.value)}
              onChange={() => toggleSkill(option.value)}
            />
            {option.label}
          </label>
        ))}
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
        {isSaving ? 'добавление...' : 'добавить бригаду'}
      </button>
    </form>
  )
}
