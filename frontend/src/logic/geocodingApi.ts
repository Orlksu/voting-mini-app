const mapTilerApiKey = import.meta.env.VITE_MAPTILER_API_KEY

// Москва и Московская область, поиск смещён к центру
const SEARCH_BBOX = '35.1,54.2,40.3,57.0'
const SEARCH_PROXIMITY = '37.6176,55.7558'

export type AddressSuggestion = {
  id: string
  label: string
  latitude: number
  longitude: number
  district: string | null
}

type MapTilerContext = { id: string; text: string }

type MapTilerFeature = {
  id: string
  center: [number, number]
  place_name: string
  matching_place_name?: string
  context?: MapTilerContext[]
}

function contextText(context: MapTilerContext[], type: string) {
  return context.find((item) => item.id.startsWith(`${type}.`))?.text ?? null
}

export function normalizeDistrict(value: string) {
  return value
    .replace(
      /(^|\s)(район|р-н|городской округ|муниципальный округ|поселение)(?=\s|$)/gi,
      ' ',
    )
    .replace(/\s+/g, ' ')
    .trim()
}

function toSuggestion(feature: MapTilerFeature): AddressSuggestion {
  const context = feature.context ?? []
  const district =
    contextText(context, 'municipality') ??
    contextText(context, 'joint_municipality')

  const [longitude, latitude] = feature.center
  return {
    id: feature.id,
    label: (feature.matching_place_name ?? feature.place_name)
      .replace(/,\s*\d{6}(?=,)/, '')
      .replace(/,\s*Россия$/, ''),
    latitude,
    longitude,
    district: district ? normalizeDistrict(district) : null,
  }
}

export async function searchAddress(
  query: string,
  signal?: AbortSignal,
): Promise<AddressSuggestion[]> {
  if (!mapTilerApiKey) return []

  const params = new URLSearchParams({
    key: mapTilerApiKey,
    language: 'ru',
    country: 'ru',
    autocomplete: 'true',
    limit: '5',
    types: 'address',
    bbox: SEARCH_BBOX,
    proximity: SEARCH_PROXIMITY,
  })

  const response = await fetch(
    `https://api.maptiler.com/geocoding/${encodeURIComponent(query)}.json?${params}`,
    { signal },
  )
  if (!response.ok) {
    throw new Error('Не удалось найти адрес')
  }

  const data: { features?: MapTilerFeature[] } = await response.json()
  return (data.features ?? []).map(toSuggestion)
}
