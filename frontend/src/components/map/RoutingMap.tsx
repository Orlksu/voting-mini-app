import Map from 'react-map-gl/maplibre'
import 'maplibre-gl/dist/maplibre-gl.css'

const mapTilerApiKey = import.meta.env.VITE_MAPTILER_API_KEY
const mapStyle = mapTilerApiKey
  ? `https://api.maptiler.com/maps/basic-v2-dark/style.json?key=${mapTilerApiKey}`
  : undefined

export function RoutingMap() {
  if (!mapStyle) {
    return (
      <div role="alert">
        Укажите VITE_MAPTILER_API_KEY в файле frontend/.env.local
      </div>
    )
  }

  return (
    <Map
      initialViewState={{
        longitude: 37.6176,
        latitude: 55.7558,
        zoom: 11,
      }}
      mapStyle={mapStyle}
      attributionControl={{}}
    />
  )
}
