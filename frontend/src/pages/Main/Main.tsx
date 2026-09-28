import styles from './Main.module.css'
import { RoutingMap } from '../../components/map/RoutingMap'
import { useCallback, useEffect, useState } from 'react'
import Header from '../../components/Header/Header.tsx'
import EngineerList from '../../components/EngineerList/EngineerList.tsx'
import RequestsModal from '../../components/RequestsModal/RequestsModal.tsx'
import UploadCsvModal from '../../components/UploadCsvModal/UploadCsvModal.tsx'
import ImportProgress from '../../components/JobProgress/ImportProgress.tsx'
import OptimizationProgress from '../../components/JobProgress/OptimizationProgress.tsx'
import JobProgressCard from '../../components/JobProgress/JobProgressCard.tsx'
import OptimizationResultModal from '../../components/OptimizationResultModal/OptimizationResultModal.tsx'
import { engineersApi } from '../../logic/engineerApi'
import type { Engineer } from '../../types/engineer'
import type { WorkOrder } from '../../types/workOrder.ts'
import { orderApi } from '../../logic/orderApi.ts'
import type { EngineerRoute } from '../../types/engineerRoute.ts'
import { optimizationApi } from '../../logic/optimizationApi.ts'
import type { OptimizationResult } from '../../types/optimization.ts'

type ProgressJob = {
  type: 'import' | 'optimization'
  jobId: string
}

const PROGRESS_JOB_STORAGE_KEY = 'progressJob'

function readStoredProgressJob(): ProgressJob | null {
  try {
    const raw = localStorage.getItem(PROGRESS_JOB_STORAGE_KEY)
    if (!raw) return null

    const parsed: unknown = JSON.parse(raw)
    if (
      typeof parsed === 'object' &&
      parsed !== null &&
      'type' in parsed &&
      (parsed.type === 'import' || parsed.type === 'optimization') &&
      'jobId' in parsed &&
      typeof parsed.jobId === 'string'
    ) {
      return { type: parsed.type, jobId: parsed.jobId }
    }
    return null
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
  const [activePanel, setActivePanel] = useState<
    'brigades' | 'requests' | 'result' | null
  >(null)
  const isBrigadesOpen = activePanel === 'brigades'
  const isRequestsOpen = activePanel === 'requests'
  const [isUploadModalOpen, setIsUploadModalOpen] = useState(false)
  const [progressJob, setProgressJob] = useState<ProgressJob | null>(
    readStoredProgressJob,
  )
  const [isStartingOptimization, setIsStartingOptimization] = useState(false)
  const [optimizationError, setOptimizationError] = useState<string | null>(
    null,
  )
  const [optimizationResult, setOptimizationResult] =
    useState<OptimizationResult | null>(null)

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

  const handleOptimizationCompleted = useCallback(
    async (result: OptimizationResult) => {
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
      if (progressJob) {
        localStorage.setItem(
          PROGRESS_JOB_STORAGE_KEY,
          JSON.stringify(progressJob),
        )
      } else {
        localStorage.removeItem(PROGRESS_JOB_STORAGE_KEY)
      }
    } catch {
      // localStorage недоступен (приватный режим) — прогресс просто не переживёт перезагрузку
    }
  }, [progressJob])

  const handleUploaded = (jobId: string) => {
    setIsUploadModalOpen(false)
    setOptimizationError(null)
    setProgressJob({ type: 'import', jobId })
  }

  const startOptimization = async () => {
    try {
      setIsStartingOptimization(true)
      setOptimizationError(null)
      setOptimizationResult(null)
      const job = await optimizationApi.createJob()
      setProgressJob({ type: 'optimization', jobId: job.jobId })
    } catch (startError) {
      setProgressJob(null)
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
    (progressJob?.type === 'optimization' && optimizationResult === null)

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

      {progressJob?.type === 'import' && (
        <ImportProgress
          key={progressJob.jobId}
          jobId={progressJob.jobId}
          onCompleted={reloadOrders}
          onClose={() => setProgressJob(null)}
          onShowOrders={() => {
            setProgressJob(null)
            handleRequestsClick()
          }}
          onOptimize={startOptimization}
        />
      )}

      {progressJob?.type === 'optimization' && (
        <OptimizationProgress
          key={progressJob.jobId}
          jobId={progressJob.jobId}
          onCompleted={handleOptimizationCompleted}
          onShowResult={() => {
            setActivePanel('result')
            handleActiveEngineerChange(null)
          }}
          onClose={() => setProgressJob(null)}
        />
      )}

      {optimizationError && !progressJob && (
        <JobProgressCard
          title="распределение заявок"
          onClose={() => setOptimizationError(null)}
        >
          <p role="alert">{optimizationError}</p>
        </JobProgressCard>
      )}
    </div>
  )
}
