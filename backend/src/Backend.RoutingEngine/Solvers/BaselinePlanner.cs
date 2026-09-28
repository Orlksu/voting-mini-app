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
        long[][,] timeMinutes,
        IReadOnlyList<LunchWindow?>? lunchWindows = null)
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
            long bestServiceStart = 0;

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

                // Физические ограничения пешехода: максимум 3.5 км от депо и 2.0 км на один переход
                if (eng.Transport == TransportType.Pedestrian)
                {
                    double legDist = distanceKm[v][lastPoint[v], OrderNode(index)];
                    double depotDist = distanceKm[v][v, OrderNode(index)];
                    if (legDist > 2.0 || depotDist > 3.5)
                    {
                        continue;
                    }
                }

                long travel = timeMinutes[v][lastPoint[v], OrderNode(index)];
                long arrive = Math.Max(cumulMinutes[v] + travel, ShiftStart(v));
                long ws = WindowStart(index) ?? 0;
                long serviceStart = Math.Max(arrive, ws);
                long serveEnd = serviceStart + order.DurationMinutes;

                // Обязательный обед: визит не должен пересекать обеденный интервал бригады.
                var lunch = lunchWindows != null && v < lunchWindows.Count ? lunchWindows[v] : null;
                if (lunch == null && eng.LunchStart != null && eng.LunchEnd != null)
                {
                    lunch = new LunchWindow(eng.LunchStart.Value, eng.LunchEnd.Value, eng.LunchDurationMinutes);
                }

                if (lunch != null)
                {
                    long lunchStart = (long)(lunch.Start - baseTime).TotalMinutes;
                    long lunchEnd = (long)(lunch.End - baseTime).TotalMinutes;

                    // Если приехали до обеда, но обслуживание наползает на обед — переносим начало на после обеда
                    if (serviceStart < lunchStart && serveEnd > lunchStart)
                    {
                        serviceStart = Math.Max(lunchEnd, ws);
                        serveEnd = serviceStart + order.DurationMinutes;
                    }
                    else if (serviceStart >= lunchStart && serviceStart < lunchEnd)
                    {
                        // Прибыли во время обеда — ждём окончания обеда
                        serviceStart = Math.Max(lunchEnd, ws);
                        serveEnd = serviceStart + order.DurationMinutes;
                    }
                }

                // Окно заявки: выполнение целиком в окне (ServiceStart >= WindowStart, ServiceEnd <= WindowEnd)
                long? we = WindowEnd(index);
                if (we.HasValue && serveEnd > we.Value)
                {
                    continue;
                }

                // Смена бригады: окончание работ должно укладываться в смену
                if (serviceStart > ShiftEnd(v) || serveEnd > ShiftEnd(v))
                {
                    continue;
                }

                if (travel < bestTravel)
                {
                    bestTravel = travel;
                    bestVehicle = v;
                    bestServiceStart = serviceStart;
                }
            }

            if (bestVehicle < 0)
            {
                continue; // baseline не вмещает заявку (окна/смена/ресурсы) — заявка остаётся вне baseline
            }

            totalDistance += distanceKm[bestVehicle][lastPoint[bestVehicle], OrderNode(index)];
            lastPoint[bestVehicle] = OrderNode(index);
            cumulMinutes[bestVehicle] = bestServiceStart + order.DurationMinutes;
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
    /// Дистанция базового (наивного) маршрута бригады: обход назначенных заявок в хронологическом
    /// порядке их временных окон (Earliest Deadline First) без комбинаторной оптимизации перестановок.
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

        var chronological = assignedOrderIndexes
            .OrderBy(idx => orders[idx].WindowStart ?? orders[idx].WindowEnd ?? DateTimeOffset.MaxValue)
            .ThenBy(idx => orders[idx].ExternalId, StringComparer.Ordinal)
            .ToList();

        int current = vehicle;
        double total = 0;

        foreach (int orderIndex in chronological)
        {
            total += distanceKm[current, numVehicles + orderIndex];
            current = numVehicles + orderIndex;
        }

        return total;
    }
}