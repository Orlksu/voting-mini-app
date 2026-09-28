import styles from './Main.module.css'
import { RoutingMap } from '../../components/map/RoutingMap'
import { useCallback, useEffect, useState } from 'react'
import Header from '../../components/Header/Header.tsx'
import EngineerList from '../../components/EngineerList/EngineerList.tsx'
import RequestsModal from '../../components/RequestsModal/RequestsModal.tsx'
import UploadCsvModal from '../../components/UploadCsvModal/UploadCsvModal.tsx'
import ImportProgress from '../../components/ImportProgress/ImportProgress.tsx'
import { engineersApi } from '../../logic/engineerApi'
import type { Engineer } from '../../types/engineer'
import type { WorkOrder } from '../../types/workOrder.ts'
import { orderApi } from '../../logic/orderApi.ts'
import type { EngineerRoute } from '../../types/engineerRoute.ts'

const IMPORT_JOB_STORAGE_KEY = 'importJobId'

function readStoredImportJobId(): string | null {
  try {
    return localStorage.getItem(IMPORT_JOB_STORAGE_KEY)
  } catch {
    return null
  }
}

export default function Main() {
  const [engineers, setEngineers] = useState<Engineer[]>([])
  const [orders, setOrders] = useState<WorkOrder[]>([])
  const [activeEngineerId, setActiveEngineerId] = useState<string | null>(null)
  const [engineerRoute, setEngineerRoute] = useState<EngineerRoute | null>(
    null,
  )
  const [isLoading, setIsLoading] = useState(true)
  const [error, setError] = useState<string | null>(null)
  const [activePanel, setActivePanel] = useState<'brigades' | 'requests' | null>(null)
  const isBrigadesOpen = activePanel === 'brigades'
  const isRequestsOpen = activePanel === 'requests'
  const [isUploadModalOpen, setIsUploadModalOpen] = useState(false)
  const [importJobId, setImportJobId] = useState<string | null>(
    readStoredImportJobId,
  )

  useEffect(() => {
    const controller = new AbortController()

    const loadEngineers = async () => {
      try {
        setIsLoading(true)
        setError(null)
        const data = await engineersApi.getAll(controller.signal)
        const orders = await orderApi.getAll(controller.signal)
        setOrders(orders)
        setEngineers(data)
      } catch {
        if (!controller.signal.aborted) {
          setError('Не удалось загрузить список бригад')
        }
      } finally {
        if (!controller.signal.aborted) {
          setIsLoading(false)
        }
      }
    }

    void loadEngineers()

    return () => {
      controller.abort()
    }
  }, [])

  useEffect(() => {
    if (!activeEngineerId) {
      return
    }

    const controller = new AbortController()

    const loadEngineerRoute = async () => {
      try {
        const route = await orderApi.getEngineerRoute(
          activeEngineerId,
          controller.signal,
        )
        setEngineerRoute(route)
      } catch {
        if (!controller.signal.aborted) {
          setEngineerRoute(null)
        }
      }
    }

    void loadEngineerRoute()

    return () => {
      controller.abort()
    }
  }, [activeEngineerId])

  const reloadOrders = useCallback(async () => {
    setOrders(await orderApi.getAll())
  }, [])

  useEffect(() => {
    try {
      if (importJobId) {
        localStorage.setItem(IMPORT_JOB_STORAGE_KEY, importJobId)
      } else {
        localStorage.removeItem(IMPORT_JOB_STORAGE_KEY)
      }
    } catch {
      // localStorage недоступен (приватный режим) — прогресс просто не переживёт перезагрузку
    }
  }, [importJobId])

  const handleUploaded = (jobId: string) => {
    setIsUploadModalOpen(false)
    setImportJobId(jobId)
  }

  const handleActiveEngineerChange = (engineerId: string | null) => {
    setActiveEngineerId(engineerId)
    setEngineerRoute(null)
  }

  const handleCloseBrigades = () => {
    setActivePanel(null)
    handleActiveEngineerChange(null)
  }

  const handleRequestsClick = () => {
    setActivePanel('requests')
    handleActiveEngineerChange(null)
  }

  const routeOrders: WorkOrder[] =
    engineerRoute?.stops.map((stop) => ({
      id: stop.externalOrderId,
      externalId: stop.externalOrderId,
      address: stop.address,
      latitude: stop.latitude,
      longitude: stop.longitude,
      windowStart: stop.serviceStart,
      windowEnd: stop.serviceEnd,
      priority: stop.priority,
      status: 'Assigned',
      taskType: stop.taskType,
    })) ?? []

  return (
    <div className={styles.mainContainer}>
      <div className={styles.mapLayer}>
        <RoutingMap
          workOrders={activeEngineerId ? routeOrders : orders}
          isEngineerRoute={Boolean(activeEngineerId)}
          encodedPolyline={engineerRoute?.encodedPolyline}
          startPoint={
            engineerRoute
              ? {
                  latitude: engineerRoute.startLatitude,
                  longitude: engineerRoute.startLongitude,
                }
              : null
          }
        />
      </div>

      <div className={styles.overlayLayer}>
        <Header
          isBrigadesOpen={isBrigadesOpen}
          onBrigadesClick={() => setActivePanel('brigades')}
          onCloseBrigades={handleCloseBrigades}
          isRequestsOpen={isRequestsOpen}
          onRequestsClick={handleRequestsClick}
          onUploadFileClick={() => setIsUploadModalOpen(true)}
        />
        <div className={styles.contentLayer}>
          {isBrigadesOpen && (
            <EngineerList
              engineers={engineers}
              isLoading={isLoading}
              error={error}
              activeEngineerId={activeEngineerId}
              onActiveEngineerChange={handleActiveEngineerChange}
            />
          )}
        </div>
      </div>

      <RequestsModal
        isOpen={isRequestsOpen}
        onClose={() => setActivePanel(null)}
      />

      <UploadCsvModal
        isOpen={isUploadModalOpen}
        onClose={() => setIsUploadModalOpen(false)}
        onUploaded={handleUploaded}
      />

      {importJobId && (
        <ImportProgress
          key={importJobId}
          jobId={importJobId}
          onCompleted={reloadOrders}
          onClose={() => setImportJobId(null)}
          onShowOrders={() => {
            setImportJobId(null)
            handleRequestsClick()
          }}
        />
      )}
    </div>
  )
}
