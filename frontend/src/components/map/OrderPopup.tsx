import { Popup } from 'react-map-gl/maplibre'
import styles from './OrderPopup.module.css'
import type { WorkOrder } from '../../types/workOrder.ts'

type OrderPopupProps = {
  order: WorkOrder
  engineerName: string | null
  isEngineerLoading: boolean
  onClose: () => void
}

const PRIORITY_LABELS: Record<string, string> = {
  Low: 'низкий',
  Normal: 'обычный',
  High: 'высокий',
  Emergency: 'авария',
}

const STATUS_LABELS: Record<string, string> = {
  NotDispatched: 'не распределена',
  Dispatched: 'назначена',
  Assigned: 'назначена',
  InProgress: 'в работе',
  Completed: 'выполнена',
  Cancelled: 'отменена',
}

const timeFormatter = new Intl.DateTimeFormat('ru-RU', {
  hour: '2-digit',
  minute: '2-digit',
  timeZone: 'Europe/Moscow',
})

const dateFormatter = new Intl.DateTimeFormat('ru-RU', {
  day: '2-digit',
  month: '2-digit',
  timeZone: 'Europe/Moscow',
})

function formatWindow(start: string, end: string) {
  const startDate = new Date(start)
  const endDate = new Date(end)
  return `${dateFormatter.format(startDate)}, ${timeFormatter.format(startDate)}–${timeFormatter.format(endDate)}`
}

export default function OrderPopup({
  order,
  engineerName,
  isEngineerLoading,
  onClose,
}: OrderPopupProps) {
  const isEmergency = order.priority === 'Emergency'

  return (
    <Popup
      longitude={order.longitude}
      latitude={order.latitude}
      anchor="bottom"
      offset={16}
      closeButton={false}
      closeOnClick={false}
      focusAfterOpen={false}
      maxWidth="320px"
      className={styles.popup}
    >
      <div className={styles.content}>
        <div className={styles.header}>
          <p className={styles.id}>{order.externalId}</p>
          <button type="button" className={styles.close} onClick={onClose}>
            <span className="material-symbols-rounded">close</span>
          </button>
        </div>

        <p className={styles.address}>{order.address}</p>

        <dl className={styles.fields}>
          <dt>окно</dt>
          <dd>{formatWindow(order.windowStart, order.windowEnd)}</dd>
          <dt>тип</dt>
          <dd>{order.taskType}</dd>
          <dt>приоритет</dt>
          <dd className={isEmergency ? styles.emergency : undefined}>
            {PRIORITY_LABELS[order.priority] ?? order.priority}
          </dd>
          <dt>статус</dt>
          <dd>{STATUS_LABELS[order.status] ?? order.status}</dd>
          {(engineerName || isEngineerLoading) && (
            <>
              <dt>бригада</dt>
              <dd className={styles.engineer}>
                {engineerName ?? 'поиск...'}
              </dd>
            </>
          )}
        </dl>
      </div>
    </Popup>
  )
}
