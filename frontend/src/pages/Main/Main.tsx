import styles from './Main.module.css'
import { RoutingMap } from '../../components/map/RoutingMap'
import { useCallback, useEffect, useMemo, useRef, useState } from 'react'
import Header from '../../components/Header/Header.tsx'
import EngineerList from '../../components/EngineerList/EngineerList.tsx'
import RequestsModal from '../../components/RequestsModal/RequestsModal.tsx'
import UploadCsvModal from '../../components/UploadCsvModal/UploadCsvModal.tsx'
import ImportProgress from '../../components/JobProgress/ImportProgress.tsx'
import OptimizationProgress from '../../components/JobProgress/OptimizationProgress.tsx'
import JobProgressCard from '../../components/JobProgress/JobProgressCard.tsx'
import JobProgressStack from '../../components/JobProgress/JobProgressStack.tsx'
import OptimizationResultModal from '../../components/OptimizationResultModal/OptimizationResultModal.tsx'
import { engineersApi } from '../../logic/engineerApi'
import type { Engineer } from '../../types/engineer'
import type { WorkOrder } from '../../types/workOrder.ts'
import { orderApi } from '../../logic/orderApi.ts'
import type { EngineerRoute } from '../../types/engineerRoute.ts'
import type { EngineerRouteStatus } from '../../components/EngineerList/EngineerList.tsx'
import { optimizationApi } from '../../logic/optimizationApi.ts'
import type { OptimizationResult } from '../../types/optimization.ts'

type ProgressJob = {
  type: 'import' | 'optimization'
  jobId: string
  fileName?: string
}

const PROGRESS_JOBS_STORAGE_KEY = 'progressJobs'

function parseProgressJob(value: unknown): ProgressJob | null {
  if (
    typeof value === 'object' &&
    value !== null &&
    'type' in value &&
    (value.type === 'import' || value.type === 'optimization') &&
    'jobId' in value &&
    typeof value.jobId === 'string'
  ) {
    const fileName =
      'fileName' in value && typeof value.fileName === 'string'
        ? value.fileName
        : undefined
    return { type: value.type, jobId: value.jobId, fileName }
  }
  return null
}

function readStoredProgressJobs(): ProgressJob[] {
  try {
    const raw = localStorage.getItem(PROGRESS_JOBS_STORAGE_KEY)
    if (!raw) return []

    const parsed: unknown = JSON.parse(raw)
    if (!Array.isArray(parsed)) return []

    return parsed
      .map(parseProgressJob)
      .filter((job): job is ProgressJob => job !== null)
  } catch {
    return []
  }
}

export default function Main() {
  const [engineers, setEngineers] = useState<Engineer[]>([])
  const [orders, setOrders] = useState<WorkOrder[]>([])
  const [activeEngineerId, setActiveEngineerId] = useState<string | null>(null)
  const [engineerRoute, setEngineerRoute] = useState<EngineerRoute | null>(
    null,
  )
  const [routeErrorEngineerId, setRouteErrorEngineerId] = useState<
    string | null
  >(null)
  const [isLoading, setIsLoading] = useState(true)
  const [error, setError] = useState<string | null>(null)
  const [activePanel, setActivePanel] = useState<
    'brigades' | 'requests' | 'result' | null
  >(null)
  const isBrigadesOpen = activePanel === 'brigades'
  const isRequestsOpen = activePanel === 'requests'
  const [isUploadModalOpen, setIsUploadModalOpen] = useState(false)
  const [progressJobs, setProgressJobs] = useState<ProgressJob[]>(
    readStoredProgressJobs,
  )
  const [isStartingOptimization, setIsStartingOptimization] = useState(false)
  const [optimizationError, setOptimizationError] = useState<string | null>(
    null,
  )
  const [optimizationResult, setOptimizationResult] =
    useState<OptimizationResult | null>(null)
  const [selectedOrderId, setSelectedOrderId] = useState<string | null>(null)
  const [selectedOrderEngineerId, setSelectedOrderEngineerId] = useState<
    string | null
  >(null)
  const [isResolvingOrderEngineer, setIsResolvingOrderEngineer] =
    useState(false)
  const orderEngineerIndexRef = useRef<Map<string, string> | null>(null)
  const orderSelectRequestRef = useRef(0)

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
          setRouteErrorEngineerId(activeEngineerId)
        }
      }
    }

    void loadEngineerRoute()

    return () => {
      controller.abort()
    }
  }, [activeEngineerId])

  const reloadOrders = useCallback(async () => {
    orderEngineerIndexRef.current = null
    setOrders(await orderApi.getAll())
  }, [])

  const handleOptimizationCompleted = useCallback(
    async (result: OptimizationResult) => {
      orderEngineerIndexRef.current = null
      setOptimizationResult(result)
      const [ordersData, engineersData] = await Promise.all([
        orderApi.getAll(),
        engineersApi.getAll(),
      ])
      setOrders(ordersData)
      setEngineers(engineersData)
    },
    [],
  )

  useEffect(() => {
    try {
      if (progressJobs.length > 0) {
        localStorage.setItem(
          PROGRESS_JOBS_STORAGE_KEY,
          JSON.stringify(progressJobs),
        )
      } else {
        localStorage.removeItem(PROGRESS_JOBS_STORAGE_KEY)
      }
    } catch {
      // localStorage недоступен (приватный режим) — прогресс просто не переживёт перезагрузку
    }
  }, [progressJobs])

  const removeProgressJob = (jobId: string) => {
    setProgressJobs((jobs) => jobs.filter((job) => job.jobId !== jobId))
  }

  const handleUploaded = (
    uploads: { jobId: string; fileName: string }[],
    isAllUploaded: boolean,
  ) => {
    if (isAllUploaded) setIsUploadModalOpen(false)
    setProgressJobs((jobs) => [
      ...jobs,
      ...uploads.map((upload) => ({ type: 'import' as const, ...upload })),
    ])
  }

  const startOptimization = async () => {
    try {
      setIsStartingOptimization(true)
      setOptimizationError(null)
      setOptimizationResult(null)
      const job = await optimizationApi.createJob()
      setProgressJobs((jobs) => [
        ...jobs.filter((item) => item.type !== 'optimization'),
        { type: 'optimization', jobId: job.jobId },
      ])
    } catch (startError) {
      setOptimizationError(
        startError instanceof Error
          ? startError.message
          : 'Не удалось запустить распределение',
      )
    } finally {
      setIsStartingOptimization(false)
    }
  }

  const isOptimizing =
    isStartingOptimization ||
    (progressJobs.some((job) => job.type === 'optimization') &&
      optimizationResult === null)

  const handleActiveEngineerChange = (engineerId: string | null) => {
    setActiveEngineerId(engineerId)
    setEngineerRoute(null)
  }

  const handleCloseBrigades = () => {
    setActivePanel(null)
    handleActiveEngineerChange(null)
  }

  const loadOrderEngineerIndex = async () => {
    if (orderEngineerIndexRef.current) return orderEngineerIndexRef.current

    const routes = await Promise.allSettled(
      engineers.map(async (engineer) => ({
        engineerId: engineer.id,
        route: await orderApi.getEngineerRoute(engineer.id),
      })),
    )

    const index = new Map<string, string>()
    for (const item of routes) {
      if (item.status !== 'fulfilled') continue
      for (const stop of item.value.route.stops) {
        index.set(stop.externalOrderId, item.value.engineerId)
      }
    }

    orderEngineerIndexRef.current = index
    return index
  }

  const handleOrderSelect = async (externalId: string | null) => {
    const requestId = ++orderSelectRequestRef.current
    setSelectedOrderId(externalId)
    setSelectedOrderEngineerId(null)
    setIsResolvingOrderEngineer(false)

    if (!externalId) return

    const isInActiveRoute = engineerRoute?.stops.some(
      (stop) => stop.externalOrderId === externalId,
    )
    if (activeEngineerId && isInActiveRoute) {
      setSelectedOrderEngineerId(activeEngineerId)
      return
    }

    const order = orders.find((item) => item.externalId === externalId)
    if (!order || order.status === 'NotDispatched') return

    setIsResolvingOrderEngineer(true)
    try {
      const index = await loadOrderEngineerIndex()
      if (requestId !== orderSelectRequestRef.current) return

      const engineerId = index.get(externalId) ?? null
      setSelectedOrderEngineerId(engineerId)
      if (engineerId) {
        setActivePanel('brigades')
        if (engineerId !== activeEngineerId) {
          handleActiveEngineerChange(engineerId)
        }
      }
    } finally {
      if (requestId === orderSelectRequestRef.current) {
        setIsResolvingOrderEngineer(false)
      }
    }
  }

  const handleRequestsClick = () => {
    setActivePanel('requests')
    handleActiveEngineerChange(null)
  }

  const routeOrders: WorkOrder[] = useMemo(
    () =>
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
      })) ?? [],
    [engineerRoute],
  )

  const routeStartPoint = useMemo(
    () =>
      engineerRoute
        ? {
            latitude: engineerRoute.startLatitude,
            longitude: engineerRoute.startLongitude,
          }
        : null,
    [engineerRoute],
  )

  const routeStatus: EngineerRouteStatus = !activeEngineerId
    ? 'idle'
    : engineerRoute
      ? engineerRoute.stops.length > 0
        ? 'ready'
        : 'empty'
      : routeErrorEngineerId === activeEngineerId
        ? 'notFound'
        : 'loading'
  const isShowingRoute = routeStatus === 'ready'

  const selectedOrder = selectedOrderId
    ? (orders.find((order) => order.externalId === selectedOrderId) ??
      routeOrders.find((order) => order.externalId === selectedOrderId) ??
      null)
    : null
  const selectedOrderEngineerName =
    engineers.find((engineer) => engineer.id === selectedOrderEngineerId)
      ?.name ?? null

  return (
    <div className={styles.mainContainer}>
      <div className={styles.mapLayer}>
        <RoutingMap
          workOrders={isShowingRoute ? routeOrders : orders}
          isEngineerRoute={isShowingRoute}
          encodedPolyline={engineerRoute?.encodedPolyline}
          startPoint={routeStartPoint}
          selectedOrder={selectedOrder}
          selectedOrderEngineerName={selectedOrderEngineerName}
          isSelectedOrderEngineerLoading={isResolvingOrderEngineer}
          onOrderSelect={handleOrderSelect}
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
          onOptimizeClick={startOptimization}
          isOptimizing={isOptimizing}
        />
        <div className={styles.contentLayer}>
          {isBrigadesOpen && (
            <EngineerList
              engineers={engineers}
              isLoading={isLoading}
              error={error}
              activeEngineerId={activeEngineerId}
              routeStatus={routeStatus}
              onActiveEngineerChange={handleActiveEngineerChange}
            />
          )}
        </div>
      </div>

      <RequestsModal
        isOpen={isRequestsOpen}
        onClose={() => setActivePanel(null)}
        onRequestClick={(externalId) => {
          setActivePanel(null)
          void handleOrderSelect(externalId)
        }}
      />

      <UploadCsvModal
        isOpen={isUploadModalOpen}
        onClose={() => setIsUploadModalOpen(false)}
        onUploaded={handleUploaded}
      />

      <OptimizationResultModal
        isOpen={activePanel === 'result'}
        result={optimizationResult}
        engineers={engineers}
        onClose={() => setActivePanel(null)}
        onShowRoute={(engineerId) => {
          setActivePanel('brigades')
          handleActiveEngineerChange(engineerId)
        }}
      />

      <JobProgressStack>
        {progressJobs.map((job) =>
          job.type === 'import' ? (
            <ImportProgress
              key={job.jobId}
              jobId={job.jobId}
              fileName={job.fileName}
              onCompleted={reloadOrders}
              onClose={() => removeProgressJob(job.jobId)}
              onShowOrders={() => {
                removeProgressJob(job.jobId)
                handleRequestsClick()
              }}
              onOptimize={() => {
                removeProgressJob(job.jobId)
                void startOptimization()
              }}
            />
          ) : (
            <OptimizationProgress
              key={job.jobId}
              jobId={job.jobId}
              onCompleted={handleOptimizationCompleted}
              onShowResult={() => {
                setActivePanel('result')
                handleActiveEngineerChange(null)
              }}
              onClose={() => removeProgressJob(job.jobId)}
            />
          ),
        )}

        {optimizationError && (
          <JobProgressCard
            title="распределение заявок"
            onClose={() => setOptimizationError(null)}
          >
            <p role="alert">{optimizationError}</p>
          </JobProgressCard>
        )}
      </JobProgressStack>
    </div>
  )
}
