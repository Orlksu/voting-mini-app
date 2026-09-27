using Backend.Contracts.Domain.Enums;
using Backend.Contracts.Requests;

namespace Backend.RoutingEngine.Solvers;

/// <summary>
/// Фаза C: честный baseline для сравнения с оптимизацией OR-Tools.
///
/// Вместо константы «расстояние × 1.25» считаем реальный конструктивный маршрут
/// на тех же матрицах (без дополнительных запросов к Valhalla):
///  — ComputeGlobal: жадный план — заявки по порядку окна вставляются в ближайшую
///    подходящую бригаду (навык + транспорт + оборудование + смена + MaxOrdersPerShift);
///  — ComputeRouteBaseline: маршрут ближайшего соседа по назначенным оптимизацией заявкам
///    конкретной бригады — используется как baseline дистанции маршрута в отчёте.
/// </summary>
public static class BaselinePlanner
{
    public sealed record GlobalBaseline(double TotalDistanceKm, int ActiveEngineers, int AssignedOrders);

    /// <summary>
    /// Жадный глобальный план. Индексы матрицы: [0..V-1] — старты бригад, [V..V+O-1] — заявки.
    /// </summary>
    public static GlobalBaseline ComputeGlobal(
        IReadOnlyList<EngineerInputDto> engineers,
        IReadOnlyList<WorkOrderInputDto> orders,
        double[][,] distanceKm,
        long[][,] timeMinutes)
    {
        int vCount = engineers.Count;
        int oCount = orders.Count;
        if (vCount == 0 || oCount == 0)
        {
            return new GlobalBaseline(0, 0, 0);
        }

        var baseTime = engineers.Min(e => e.ShiftStart);
        var sortedOrders = orders
            .Select((order, index) => (Order: order, Index: index))
            .OrderBy(x => x.Order.WindowStart ?? x.Order.WindowEnd ?? DateTimeOffset.MaxValue)
            .ThenBy(x => x.Order.ExternalId, StringComparer.Ordinal)
            .ToList();

        var lastPoint = new int[vCount];        // матричный индекс последней точки бригады
        var cumulMinutes = new long[vCount];     // момент после обслуживания последней точки
        var visited = new int[vCount];
        double totalDistance = 0;

        long ShiftStart(int v) => (long)(engineers[v].ShiftStart - baseTime).TotalMinutes;
        long ShiftEnd(int v) => (long)(engineers[v].ShiftEnd - baseTime).TotalMinutes;
        long? WindowStart(int j)
        {
            DateTimeOffset? ws = orders[j].WindowStart;
            return ws.HasValue ? (long?)(ws.Value - baseTime).TotalMinutes : null;
        }

        long? WindowEnd(int j)
        {
            DateTimeOffset? we = orders[j].WindowEnd;
            return we.HasValue ? (long?)(we.Value - baseTime).TotalMinutes : null;
        }

        int OrderNode(int j) => vCount + j;

        for (int j = 0; j < vCount; j++)
        {
            lastPoint[j] = j;
            cumulMinutes[j] = Math.Max(0, ShiftStart(j));
        }

        foreach (var (order, index) in sortedOrders)
        {
            int bestVehicle = -1;
            long bestTravel = long.MaxValue;

            for (int v = 0; v < vCount; v++)
            {
                var eng = engineers[v];

                // Жёсткие ограничения: навык, транспорт, оборудование.
                if (eng.Skills == null || !eng.Skills.Contains(order.RequiredSkill))
                {
                    continue;
                }

                if (order.RequiredTransport != null && eng.Transport != order.RequiredTransport)
                {
                    continue;
                }

                if (order.RequiredEquipment is { Count: > 0 } &&
                    (eng.Equipment == null || !order.RequiredEquipment.All(r => eng.Equipment.Contains(r))))
                {
                    continue;
                }

                // Лимит заявок за смену.
                int maxOrders = eng.MaxOrdersPerShift > 0 ? eng.MaxOrdersPerShift : int.MaxValue;
                if (visited[v] >= maxOrders)
                {
                    continue;
                }

                long travel = timeMinutes[v][lastPoint[v], OrderNode(index)];
                long arrive = Math.Max(cumulMinutes[v] + travel, ShiftStart(v));
                long serveEnd = arrive + order.DurationMinutes;

                // Обязательный обед: визит (и переезд к нему) не должен пересекать обеденный интервал.
                if (eng.LunchStart != null && eng.LunchEnd != null)
                {
                    long lunchStart = (long)(eng.LunchStart.Value - baseTime).TotalMinutes;
                    long lunchEnd = (long)(eng.LunchEnd.Value - baseTime).TotalMinutes;
                    bool fullyBefore = serveEnd <= lunchStart;
                    bool fullyAfter = arrive >= lunchEnd;
                    if (!fullyBefore && !fullyAfter)
                    {
                        continue;
                    }
                }

                // Окно заявки: выполнение целиком в окне (ServiceStart ≥ WindowStart, ServiceEnd ≤ WindowEnd).
                long? ws = WindowStart(index);
                long? we = WindowEnd(index);
                if (ws.HasValue && arrive < ws.Value)
                {
                    continue;
                }

                if (we.HasValue && serveEnd > we.Value)
                {
                    continue;
                }

                // Смена бригады.
                if (arrive > ShiftEnd(v) || serveEnd > ShiftEnd(v))
                {
                    continue;
                }

                if (travel < bestTravel)
                {
                    bestTravel = travel;
                    bestVehicle = v;
                }
            }

            if (bestVehicle < 0)
            {
                continue; // baseline не вмещает заявку (окна/смена/ресурсы) — заявка остаётся вне baseline
            }

            totalDistance += distanceKm[bestVehicle][lastPoint[bestVehicle], OrderNode(index)];
            lastPoint[bestVehicle] = OrderNode(index);
            cumulMinutes[bestVehicle] = Math.Max(cumulMinutes[bestVehicle] + bestTravel, ShiftStart(bestVehicle))
                                        + order.DurationMinutes;
            visited[bestVehicle]++;
        }

        int active = 0;
        int assigned = 0;
        for (int v = 0; v < vCount; v++)
        {
            if (visited[v] > 0)
            {
                active++;
                assigned += visited[v];
            }
        }

        return new GlobalBaseline(totalDistance, active, assigned);
    }

    /// <summary>
    /// Дистанция маршрута ближайшего соседа по назначенным бригаде заявкам (для BaselineDistanceKm в отчёте).
    /// </summary>
    public static double ComputeRouteBaseline(
        int vehicle,
        IReadOnlyList<WorkOrderInputDto> orders,
        IReadOnlyList<int> assignedOrderIndexes,
        int numVehicles,
        double[,] distanceKm)
    {
        if (assignedOrderIndexes.Count == 0)
        {
            return 0;
        }

        var remaining = new HashSet<int>(assignedOrderIndexes);
        int current = vehicle;
        double total = 0;

        while (remaining.Count > 0)
        {
            int best = -1;
            double bestDistance = double.MaxValue;
            foreach (int orderIndex in remaining)
            {
                double d = distanceKm[current, numVehicles + orderIndex];
                if (d < bestDistance)
                {
                    bestDistance = d;
                    best = orderIndex;
                }
            }

            if (best < 0)
            {
                break;
            }

            total += bestDistance;
            current = numVehicles + best;
            remaining.Remove(best);
        }

        return total;
    }
}