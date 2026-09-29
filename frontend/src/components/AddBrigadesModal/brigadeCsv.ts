import type { Engineer } from '../../types/engineer'
import type { SkillType, TransportType } from '../../types/optimization.ts'

export type BrigadeDraft = {
  externalId: string
  name: string
  district: string
  startAddress: string
  startLatitude: number | null
  startLongitude: number | null
  shiftStart: string
  shiftEnd: string
  transport: TransportType
  skills: SkillType[]
  maxOrdersPerShift: number
}

const ID_PATTERN = /^ENG-(\d+)$/i

export function nextEngineerExternalId(engineers: Engineer[]) {
  const max = engineers.reduce((current, engineer) => {
    const match = ID_PATTERN.exec(engineer.externalId.trim())
    return match ? Math.max(current, Number(match[1])) : current
  }, 0)
  return `ENG-${String(max + 1).padStart(3, '0')}`
}

function quote(value: string | number) {
  return `"${String(value).replace(/"/g, '""')}"`
}

// JSON-эндпоинта создания бригады нет, поэтому форма отправляется как CSV из одной строки
export function brigadeToCsvFile(draft: BrigadeDraft) {
  const header = [
    'ExternalId',
    'Name',
    'District',
    'StartAddress',
    'StartLatitude',
    'StartLongitude',
    'ShiftStart',
    'ShiftEnd',
    'Transport',
    'Skills',
    'MaxOrdersPerShift',
  ].join(';')

  const row = [
    draft.externalId,
    draft.name,
    draft.district,
    draft.startAddress,
    draft.startLatitude ?? '',
    draft.startLongitude ?? '',
    draft.shiftStart,
    draft.shiftEnd,
    draft.transport,
    draft.skills.join(','),
    draft.maxOrdersPerShift,
  ]
    .map(quote)
    .join(';')

  return new File([`${header}\n${row}\n`], `${draft.externalId}.csv`, {
    type: 'text/csv',
  })
}
