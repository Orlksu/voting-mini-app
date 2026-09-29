using Backend.Contracts.Domain.Enums;
using Backend.Contracts.Requests;
using Backend.Contracts.Responses;

namespace Backend.RoutingEngine.Solvers;

/// <summary>
/// Предварительная обработка входа: исключение завершённых/отменённых заявок и заявок без координат,
/// нормализация длительностей по нормативам, отсев заявок с невыполнимыми окнами,
/// исключение бригад с некорректной сменой.
/// </summary>
public static class PlanPreprocessor
{
    /// <summary>
    /// Географический bbox Москвы (с запасом на МКАД + ближайшие города-спутники).
    /// Координаты за пределами отклоняются как ошибки геокодера.
    /// </summary>
    private const double MoscowLatMin = 55.48;
    private const double MoscowLatMax = 56.00;
    private const double MoscowLonMin = 36.80;
    private const double MoscowLonMax = 38.00;

    public sealed record Result(
        List<EngineerInputDto> Engineers,
        List<WorkOrderInputDto> Orders,
        List<UnassignedOrderDto> Unassigned);

    public static Result Preprocess(CreateOptimizationPlanRequest request)
    {
        var engineers = request.Engineers
            .Where(e => e.ShiftEnd > e.ShiftStart)
            .ToList();

        var unassigned = new List<UnassignedOrderDto>();
        var orders = new List<WorkOrderInputDto>();

        foreach (var order in request.Orders ?? [])
        {
            // Завершённые/отменённые заявки в планирование не попадают.
            if (order.Status is OrderStatus.Completed or OrderStatus.Cancelled)
            {
                unassigned.Add(ToUnassigned(order, UnassignedReasonCode.NoQualifiedEngineer,
                    "Заявка завершена или отменена и не участвует в планировании."));
                continue;
            }

            // Без координат маршрут построить нельзя.
            if (order.Latitude == null || order.Longitude == null)
            {
                unassigned.Add(ToUnassigned(order, UnassignedReasonCode.MissingCoordinates,
                    "Заявка не содержит координат (широта/долгота)."));
                continue;
            }

            // Географический bbox-фильтр: координаты за пределами Москвы — ошибка геокодера.
            double lat = order.Latitude.Value;
            double lon = order.Longitude.Value;
            if (lat < MoscowLatMin || lat > MoscowLatMax || lon < MoscowLonMin || lon > MoscowLonMax)
            {
                unassigned.Add(ToUnassigned(order, UnassignedReasonCode.MissingCoordinates,
                    $"Координаты заявки ({lat:F4}, {lon:F4}) находятся за пределами зоны обслуживания Москвы. " +
                    $"Ожидается lat ∈ [{MoscowLatMin}, {MoscowLatMax}], lon ∈ [{MoscowLonMin}, {MoscowLonMax}]. " +
                    "Вероятно, ошибка геокодера — уточните адрес."));
                continue;
            }

            // Длительность по нормативу (без времени на дорогу), если тип распознан.
            var duration = NormativeDurations.TryGetDurationMinutes(order.TaskType) ?? order.DurationMinutes;
            if (duration <= 0) duration = 30;

            // Окно должно позволять выполнить работы целиком: ServiceStart ≥ WindowStart,
            // ServiceEnd ≤ WindowEnd → длительность не больше ширины окна.
            // Если границ окна нет — ограничение не применяется.
            if (order.WindowStart.HasValue && order.WindowEnd.HasValue)
            {
                double windowLengthMinutes = (order.WindowEnd.Value - order.WindowStart.Value).TotalMinutes;
                if (duration > windowLengthMinutes)
                {
                    unassigned.Add(ToUnassigned(order, UnassignedReasonCode.TimeWindowMissed,
                        $"Длительность работ ({duration} мин) превышает временное окно заявки ({Math.Ceiling(windowLengthMinutes)} мин)."));
                    continue;
                }
            }

            orders.Add(order with { DurationMinutes = duration });
        }

        return new Result(engineers, orders, unassigned);
    }

    /// <summary>Точки для матриц: старты инженеров, затем заявки (индексы 0..V-1 — старты).</summary>
    public static List<(double Lat, double Lon)> BuildLocations(
        IReadOnlyList<EngineerInputDto> engineers,
        IReadOnlyList<WorkOrderInputDto> orders)
    {
        var locations = new List<(double Lat, double Lon)>();
        foreach (var eng in engineers)
            locations.Add((eng.StartLatitude ?? 0.0, eng.StartLongitude ?? 0.0));
        foreach (var ord in orders)
            locations.Add((ord.Latitude ?? 0.0, ord.Longitude ?? 0.0));
        return locations;
    }

    private static UnassignedOrderDto ToUnassigned(WorkOrderInputDto order, UnassignedReasonCode reason, string description)
        => new(order.ExternalId, order.Address ?? string.Empty, reason, description);
}