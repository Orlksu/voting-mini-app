import { Popup } from 'react-map-gl/maplibre'
import styles from './OrderPopup.module.css'
import type { WorkOrder } from '../../types/workOrder.ts'
import OrderActions from '../OrderActions/OrderActions.tsx'
import { ORDER_STATUS_LABELS } from '../../logic/orderStatus.ts'

type OrderPopupProps = {
  order: WorkOrder
  engineerName: string | null
  isEngineerLoading: boolean
  onClose: () => void
  onComplete: (order: WorkOrder) => Promise<unknown>
  onCancel: (order: WorkOrder, reason: string) => Promise<unknown>
}

const PRIORITY_LABELS: Record<string, string> = {
  Low: 'низкий',
  Normal: 'обычный',
  High: 'высокий',
  Emergency: 'авария',
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
  onComplete,
  onCancel,
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
          <dd>{ORDER_STATUS_LABELS[order.status] ?? order.status}</dd>
          {(engineerName || isEngineerLoading) && (
            <>
              <dt>бригада</dt>
              <dd className={styles.engineer}>{engineerName ?? 'поиск...'}</dd>
            </>
          )}
        </dl>

        <OrderActions
          key={order.id}
          order={order}
          variant="popup"
          onComplete={onComplete}
          onCancel={onCancel}
        />
      </div>
    </Popup>
  )
}
