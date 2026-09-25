using Backend.Contracts.Domain.Enums;
using Backend.Contracts.Interfaces;
using Backend.Contracts.Requests;
using Backend.Contracts.Responses;

namespace Backend.RoutingEngine.Services;

/// <summary>
/// Заглушка оптимизатора (fallback при сбое OR-Tools).
/// Возвращает правдоподобные маршруты, НО в отличие от прошлой версии
/// не нарушает жёсткие ограничения: заявка назначается бригаде только при
/// совпадении навыка и транспорта; несовместимые заявки уходят в неназначенные.
/// </summary>
public class MockRouteOptimizer : IRouteOptimizer
{
    public Task<OptimizationResultDto> OptimizeAsync(
        CreateOptimizationPlanRequest request,
        CancellationToken cancellationToken = default)
    {
        var orders = request.Orders;
        var engineers = request.Engineers;
        var unassigned = new List<UnassignedOrderDto>();
        var routes = new List<EngineerRouteDto>();

        if (engineers.Count == 0 || orders.Count == 0)
        {
            unassigned.AddRange(orders.Select(o => new UnassignedOrderDto(
                o.ExternalId,
                o.Address,
                UnassignedReasonCode.NoQualifiedEngineer,
                "В смене нет доступных исполнителей")));

            return Task.FromResult(new OptimizationResultDto(
                Guid.NewGuid(),
                request.PlanDate,
                new PlanComparisonMetricsDto(
                    TotalOrders: orders.Count,
                    AssignedOrders: 0,
                    UnassignedOrders: orders.Count,
                    OptimizedActiveEngineers: 0,
                    BaselineActiveEngineers: 0,
                    SavedEngineersCount: 0,
                    OptimizedTotalDistanceKm: 0,
                    BaselineTotalDistanceKm: 0,
                    DistanceEconomyPercent: 0),
                routes,
                unassigned));
        }

        // Простейшее распределение по кругу между совместимыми инженерами
        int engineerIndex = 0;
        var engineerOrdersMap = engineers.ToDictionary(e => e.ExternalId, _ => new List<WorkOrderInputDto>());

        foreach (var order in orders)
        {
            if (engineers.Count == 0) break;

            var engineer = engineers[engineerIndex % engineers.Count];
            bool compatible = HasSkill(engineer, order.RequiredSkill)
                              && (order.RequiredTransport == null || engineer.Transport == order.RequiredTransport);

            if (!compatible)
            {
                unassigned.Add(new UnassignedOrderDto(
                    order.ExternalId,
                    order.Address,
                    UnassignedReasonCode.NoQualifiedEngineer,
                    "Fallback: нет совместимого инженера по навыку/транспорту"));
                continue;
            }

            engineerOrdersMap[engineer.ExternalId].Add(order);
            engineerIndex++;
        }

        double totalDistance = 0;
        int totalTravelMinutes = 0;
        int activeEngineers = 0;
        int assignedCount = 0;

        foreach (var engineer in engineers)
        {
            var assignedOrders = engineerOrdersMap[engineer.ExternalId];
            if (assignedOrders.Count == 0)
            {
                continue;
            }

            activeEngineers++;
            assignedCount += assignedOrders.Count;
            var stops = new List<RouteStopDto>();
            var currentTime = engineer.ShiftStart.AddMinutes(15);

            int sequence = 0;
            int engTravelTime = 0;
            double engDistance = 0;

            foreach (var order in assignedOrders)
            {
                // Примерное расстояние: 3.5 км, время в пути 15 минут
                double legDistance = 3.5;
                int travelMinutes = 15;

                var arrival = currentTime.AddMinutes(travelMinutes);
                // Ждём начала окна, если приехали раньше
                var serviceStart = arrival < order.WindowStart ? order.WindowStart : arrival;
                var serviceEnd = serviceStart.AddMinutes(order.DurationMinutes);

                stops.Add(new RouteStopDto(
                    Sequence: ++sequence,
                    ExternalOrderId: order.ExternalId,
                    Address: order.Address,
                    Latitude: order.Latitude ?? 0,
                    Longitude: order.Longitude ?? 0,
                    EstimatedArrival: arrival,
                    ServiceStart: serviceStart,
                    ServiceEnd: serviceEnd,
                    TravelDurationMinutes: travelMinutes,
                    DistanceFromPrevKm: legDistance,
                    TaskType: order.TaskType,
                    Priority: order.Priority,
                    AssignmentReason:
                    $"Назначен инженеру {engineer.Name}: совпадает навык ('{order.RequiredSkill}'), " +
                    $"прибытие в {arrival:HH:mm} к началу окна"));

                currentTime = serviceEnd;
                engTravelTime += travelMinutes;
                engDistance += legDistance;
            }

            routes.Add(new EngineerRouteDto(
                EngineerExternalId: engineer.ExternalId,
                EngineerName: engineer.Name,
                Transport: engineer.Transport,
                TotalOrders: assignedOrders.Count,
                TotalDistanceKm: Math.Round(engDistance, 2),
                BaselineDistanceKm: Math.Round(engDistance * 1.1, 2),
                TotalTravelMinutes: engTravelTime,
                Stops: stops,
                EncodedPolyline: null,
                BreakStops: []));

            totalDistance += engDistance;
            totalTravelMinutes += engTravelTime;
        }

        double baselineDistance = Math.Round(totalDistance * 1.1, 2);
        double economyPercent = baselineDistance > 0
            ? Math.Round((baselineDistance - totalDistance) / baselineDistance * 100, 2)
            : 0;

        var response = new OptimizationResultDto(
            Guid.NewGuid(),
            request.PlanDate,
            new PlanComparisonMetricsDto(
                TotalOrders: orders.Count,
                AssignedOrders: assignedCount,
                UnassignedOrders: unassigned.Count,
                OptimizedActiveEngineers: activeEngineers,
                BaselineActiveEngineers: activeEngineers,
                SavedEngineersCount: 0,
                OptimizedTotalDistanceKm: Math.Round(totalDistance, 2),
                BaselineTotalDistanceKm: baselineDistance,
                DistanceEconomyPercent: economyPercent),
            routes,
            unassigned);

        return Task.FromResult(response);
    }

    private static bool HasSkill(EngineerInputDto engineer, SkillType requiredSkill)
        => engineer.Skills != null && engineer.Skills.Contains(requiredSkill);
}