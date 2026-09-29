import { normalizeDistrict } from '../../logic/geocodingApi.ts'

function districtKey(value: string) {
  return normalizeDistrict(value).toLowerCase().replace(/ё/g, 'е')
}

export function uniqueDistricts(values: (string | null | undefined)[]) {
  return [
    ...new Set(values.map((item) => item?.trim()).filter(Boolean) as string[]),
  ].sort((a, b) => a.localeCompare(b, 'ru'))
}

// «район Кузьминки» из геокодера → «Кузьминки», как уже записано в данных
export function matchKnownDistrict(suggested: string, known: string[]) {
  const key = districtKey(suggested)
  return known.find((item) => districtKey(item) === key) ?? suggested
}
