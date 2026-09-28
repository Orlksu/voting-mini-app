import { LngLatBounds, setWorkerUrl } from 'maplibre-gl'
import type { ExpressionSpecification, GeoJSONSource } from 'maplibre-gl'
import type { FeatureCollection, Point } from 'geojson'
import '@fontsource/material-symbols-rounded/400.css'
import 'maplibre-gl/dist/maplibre-gl.css'
import type { WorkOrder } from '../../types/workOrder.ts'
import Map, { Layer, Marker, Source } from 'react-map-gl/maplibre'
import type { MapLayerMouseEvent, MapRef } from 'react-map-gl/maplibre'
import { useRef, useState } from 'react'
import styles from './RoutingMap.module.css'

setWorkerUrl(
  new URL('maplibre-gl/dist/maplibre-gl-worker.mjs', import.meta.url).href,
)

const mapTilerApiKey = import.meta.env.VITE_MAPTILER_API_KEY
const mapStyle = mapTilerApiKey
  ? `https://api.maptiler.com/maps/basic-v2-dark/style.json?key=${mapTilerApiKey}`
  : undefined

const CLUSTER_LAYER_ID = 'work-orders-clusters'
const CLUSTER_FONT = ['Montserrat Bold']
const CLUSTER_COLOR = '#EABE5F'
const EMERGENCY_COLOR = '#E5484D'
const CLUSTER_FILL = '#1B1A1A'
const CLUSTER_ZOOM_PADDING = { top: 160, bottom: 80, left: 80, right: 80 }
const CLUSTER_ZOOM_MAX = 16

const clusterRadius: ExpressionSpecification = [
  'step',
  ['get', 'point_count'],
  16,
  10,
  20,
  50,
  26,
  100,
  32,
]

const clusterAccentColor: ExpressionSpecification = [
  'interpolate',
  ['linear'],
  ['/', ['get', 'emergencyCount'], ['get', 'point_count']],
  0,
  CLUSTER_COLOR,
  0.5,
  '#E8863A',
  1,
  EMERGENCY_COLOR,
]

interface RoutingMapProps {
  workOrders?: WorkOrder[]
  isEngineerRoute?: boolean
  encodedPolyline?: string | null
  startPoint?: {
    latitude: number
    longitude: number
  } | null
}

function decodePolyline(encoded: string): [number, number][] {
  let index = 0
  let latitude = 0
  let longitude = 0
  const coordinates: [number, number][] = []

  while (index < encoded.length) {
    let result = 0
    let shift = 0
    let byte: number

    do {
      byte = encoded.charCodeAt(index++) - 63
      result |= (byte & 0x1f) << shift
      shift += 5
    } while (byte >= 0x20)

    const latitudeChange =
      result & 1 ? ~(result >> 1) : result >> 1

    result = 0
    shift = 0

    do {
      byte = encoded.charCodeAt(index++) - 63
      result |= (byte & 0x1f) << shift
      shift += 5
    } while (byte >= 0x20)

    const longitudeChange =
      result & 1 ? ~(result >> 1) : result >> 1

    latitude += latitudeChange
    longitude += longitudeChange
    coordinates.push([longitude / 1e5, latitude / 1e5])
  }

  return coordinates
}

export function RoutingMap({
  workOrders,
  isEngineerRoute = false,
  encodedPolyline = null,
  startPoint = null,
}: RoutingMapProps) {
  const [zoom, setZoom] = useState(11)
  const [isClusterHovered, setIsClusterHovered] = useState(false)
  const mapRef = useRef<MapRef>(null)

  const handleMapClick = async (event: MapLayerMouseEvent) => {
    const cluster = event.features?.find(
      (feature) => feature.layer.id === CLUSTER_LAYER_ID,
    )
    const map = mapRef.current
    if (!cluster || !map) return

    const source = map.getSource<GeoJSONSource>('work-orders')
    const clusterId = cluster.properties?.cluster_id
    if (!source || typeof clusterId !== 'number') return

    const leaves = await source.getClusterLeaves(clusterId, Infinity, 0)
    const bounds = new LngLatBounds()
    for (const leaf of leaves) {
      if (leaf.geometry.type === 'Point') {
        bounds.extend(leaf.geometry.coordinates as [number, number])
      }
    }
    if (bounds.isEmpty()) return

    map.fitBounds(bounds, {
      padding: CLUSTER_ZOOM_PADDING,
      maxZoom: CLUSTER_ZOOM_MAX,
      duration: 600,
    })
  }

  if (!mapStyle) {
    return (
      <div role="alert">
        Укажите VITE_MAPTILER_API_KEY в файле frontend/.env.local
      </div>
    )
  }

  if (!workOrders) return null

  const workOrdersGeoJson: FeatureCollection<Point> = {
    type: 'FeatureCollection',
    features: workOrders.map((workOrder) => ({
      type: 'Feature',
      properties: {
        externalId: workOrder.externalId,
        priority: workOrder.priority,
      },
      geometry: {
        type: 'Point',
        coordinates: [workOrder.longitude, workOrder.latitude],
      },
    })),
  }

  const routeGeoJson = encodedPolyline
    ? {
        type: 'FeatureCollection' as const,
        features: [
          {
            type: 'Feature' as const,
            properties: {},
            geometry: {
              type: 'LineString' as const,
              coordinates: decodePolyline(encodedPolyline),
            },
          },
        ],
      }
    : null

  return (
    <div
      style={{
        position: 'relative',
        width: '100%',
        height: '100%',
      }}
    >
      <Map
        ref={mapRef}
        initialViewState={{
          longitude: 37.6176,
          latitude: 55.7558,
          zoom: 11,
        }}
        mapStyle={mapStyle}
        attributionControl={{}}
        onMove={(event) => setZoom(event.viewState.zoom)}
        interactiveLayerIds={isEngineerRoute ? [] : [CLUSTER_LAYER_ID]}
        onClick={handleMapClick}
        onMouseEnter={() => setIsClusterHovered(true)}
        onMouseLeave={() => setIsClusterHovered(false)}
        cursor={isClusterHovered ? 'pointer' : undefined}
      >
        {isEngineerRoute && routeGeoJson && (
          <Source
            id="engineer-route-line"
            type="geojson"
            data={routeGeoJson}
          >
            <Layer
              id="engineer-route-line"
              type="line"
              layout={{
                'line-cap': 'round',
                'line-join': 'round',
              }}
              paint={{
                'line-color': '#DEB111',
                'line-width': [
                  'step',
                  ['zoom'],
                  4,
                  14,
                  3,
                ],
                'line-dasharray': [
                  'step',
                  ['zoom'],
                  ['literal', [4, 8]],
                  14,
                  ['literal', [1, 3]],
                ],
              }}
            />
          </Source>
        )}

        {isEngineerRoute ? (
          <>
            <Source
              key="engineer-route-orders"
              id="engineer-route-orders"
              type="geojson"
              data={workOrdersGeoJson}
            >
              <Layer
                id="engineer-route-points"
                type="circle"
                paint={{
                  'circle-radius': 10,
                  'circle-color': [
                    'match',
                    ['get', 'priority'],
                    'Emergency',
                    '#B00F19',
                    '#EABE5F',
                  ],
                  'circle-stroke-color': '#000000',
                  'circle-stroke-width': 2,
                }}
              />
            </Source>
            {startPoint && (
              <Marker
                longitude={startPoint.longitude}
                latitude={startPoint.latitude}
                anchor="bottom"
              >
                <span
                  className={`material-symbols-rounded ${styles.startMarker}`}
                  aria-label="Точка начала маршрута"
                  title="Точка начала маршрута"
                >
                  home
                </span>
              </Marker>
            )}
          </>
        ) : (
          <Source
            key="work-orders"
            id="work-orders"
            type="geojson"
            data={workOrdersGeoJson}
            cluster
            clusterMaxZoom={14}
            clusterRadius={50}
            clusterProperties={{
              emergencyCount: [
                '+',
                [
                  'case',
                  ['==', ['get', 'priority'], 'Emergency'],
                  1,
                  0,
                ],
              ],
            }}
          >
            <Layer
              id="work-orders-clusters-halo"
              type="circle"
              filter={['has', 'point_count']}
              paint={{
                'circle-radius': ['+', clusterRadius, 7],
                'circle-color': clusterAccentColor,
                'circle-opacity': 0.18,
                'circle-blur': 0.4,
              }}
            />
            <Layer
              id={CLUSTER_LAYER_ID}
              type="circle"
              filter={['has', 'point_count']}
              paint={{
                'circle-radius': clusterRadius,
                'circle-color': CLUSTER_FILL,
                'circle-opacity': 0.92,
                'circle-stroke-color': clusterAccentColor,
                'circle-stroke-width': 2.5,
              }}
            />
            <Layer
              id="work-orders-cluster-count"
              type="symbol"
              filter={['has', 'point_count']}
              layout={{
                'text-field': ['get', 'point_count_abbreviated'],
                'text-font': CLUSTER_FONT,
                'text-size': [
                  'step',
                  ['get', 'point_count'],
                  13,
                  10,
                  15,
                  50,
                  17,
                ],
                'text-allow-overlap': true,
              }}
              paint={{
                'text-color': clusterAccentColor,
              }}
            />
            <Layer
              id="work-orders-points"
              type="circle"
              filter={['!', ['has', 'point_count']]}
              paint={{
                'circle-radius': [
                  'interpolate',
                  ['linear'],
                  ['zoom'],
                  10,
                  5,
                  15,
                  7,
                  16,
                  12,
                  18,
                  18,
                ],
                'circle-color': [
                  'match',
                  ['get', 'priority'],
                  'Emergency',
                  EMERGENCY_COLOR,
                  CLUSTER_COLOR,
                ],
                'circle-stroke-color': CLUSTER_FILL,
                'circle-stroke-width': 2,
              }}
            />
          </Source>
        )}
        <Layer
          id="house-numbers"
          type="symbol"
          source="maptiler_planet"
          source-layer="housenumber"
          minzoom={11}
          layout={{
            'text-field': ['get', 'housenumber'],
            'text-size': 9,
            'text-allow-overlap': false,
          }}
          paint={{
            'text-color': '#ffffff',
            'text-halo-color': '#292929',
            'text-halo-width': 1.5,
          }}
        />
      </Map>

      <div
        style={{
          position: 'absolute',
          left: 16,
          bottom: 16,
          zIndex: 2,
          padding: '6px 10px',
          borderRadius: 6,
          color: '#ffffff',
          background: 'rgb(41 41 41 / 0.85)',
          fontFamily: 'var(--font-mono)',
          fontSize: 12,
          pointerEvents: 'none',
        }}
      >
        Zoom: {zoom.toFixed(2)}
      </div>
    </div>
  )
}
