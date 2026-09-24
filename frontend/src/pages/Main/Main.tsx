import styles from './Main.module.css'
import { RoutingMap } from '../../components/map/RoutingMap'
import { useEffect, useState } from 'react'
import Header from '../../components/Header/Header.tsx'
import EngineerList from '../../components/EngineerList/EngineerList.tsx'
import { engineersApi } from '../../logic/engineerApi'
import type { Engineer } from '../../types/engineer'

export default function Main() {
  const [engineers, setEngineers] = useState<Engineer[]>([])
  const [isLoading, setIsLoading] = useState(true)
  const [error, setError] = useState<string | null>(null)
  const [isBrigadesOpen, setIsBrigadesOpen] = useState(false)

  useEffect(() => {
    const controller = new AbortController()

    const loadEngineers = async () => {
      try {
        setIsLoading(true)
        setError(null)
        const data = await engineersApi.getAll(controller.signal)
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

  return (
    <div className={styles.mainContainer}>
      <div className={styles.mapLayer}>
        <RoutingMap />
      </div>

      <div className={styles.overlayLayer}>
        <Header
          isBrigadesOpen={isBrigadesOpen}
          onBrigadesClick={() => setIsBrigadesOpen(true)}
          onCloseBrigades={() => setIsBrigadesOpen(false)}
        />
        <div className={styles.contentLayer}>
          {isBrigadesOpen && (
            <EngineerList
              engineers={engineers}
              isLoading={isLoading}
              error={error}
            />
          )}
        </div>
      </div>
    </div>
  )
}
