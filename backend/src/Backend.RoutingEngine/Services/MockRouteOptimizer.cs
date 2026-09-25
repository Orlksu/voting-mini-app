using Backend.Contracts.Interfaces;
using Backend.Contracts.Requests;
using Backend.Contracts.Responses;
using Backend.Contracts.Domain.Enums;

namespace Backend.RoutingEngine.Services;

/// <summary>
/// Заглушка оптимизатора для параллельной работы фронтенда и API разработчика.
/// Возвращает реалистичные распределённые маршруты.
/// </summary>
public class MockRouteOptimizer : IRouteOptimizer
{
    public Task<OptimizationResultDto> OptimizeAsync(CreateOptimizationPlanRequest request, CancellationToken cancellationToken = default)
    {
        var routes = new List<EngineerRouteDto>();
        var orders = request.Orders;
        var engineers = request.Engineers;

        if (engineers.Count == 0 || orders.Count == 0)
        {
            var unassigned = orders
                .Select(o => new UnassignedOrderDto(
                    o.ExternalId,
                    o.Address,
                    UnassignedReasonCode.NoQualifiedEngineer,
                    "В смене нет доступных исполнителей"))
                .ToList();

            return Task.FromResult(new OptimizationResultDto(
                PlanId: Guid.NewGuid(),
                PlanDate: request.PlanDate,
                Metrics: new PlanComparisonMetricsDto(
                    TotalOrders: orders.Count,
                    AssignedOrders: 0,
                    UnassignedOrders: orders.Count,
                    OptimizedActiveEngineers: 0,
                    BaselineActiveEngineers: 0,
                    SavedEngineersCount: 0,
                    OptimizedTotalDistanceKm: 0,
                    BaselineTotalDistanceKm: 0,
                    DistanceEconomyPercent: 0),
                Routes: routes,
                UnassignedOrders: unassigned));
        }

        // Простейшее распределение заявок по кругу между доступными инженерами
        int engineerIndex = 0;
        var engineerOrdersMap = engineers.ToDictionary(e => e.ExternalId, _ => new List<WorkOrderInputDto>());

        foreach (var order in orders)
        {
            var engineer = engineers[engineerIndex % engineers.Count];
            engineerOrdersMap[engineer.ExternalId].Add(order);
            engineerIndex++;
        }

        double totalDistance = 0;
        int totalTravelMinutes = 0;
        int activeEngineers = 0;

        foreach (var engineer in engineers)
        {
            var assignedOrders = engineerOrdersMap[engineer.ExternalId];
            if (assignedOrders.Count == 0)
            {
                continue;
            }

            activeEngineers++;
            var stops = new List<RouteStopDto>();
            var currentTime = engineer.ShiftStart.AddMinutes(15);

            int sequence = 0;
            int engTravelTime = 0;
            double engDistance = 0;

            foreach (var order in assignedOrders)
            {
                double legDistance = 3.5;
                int travelMinutes = 15;

                var arrival = currentTime.AddMinutes(travelMinutes);
            
                var serviceStart = order.WindowStart is { } windowStart && arrival < windowStart ? windowStart : arrival;
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
                EncodedPolyline: null));

            totalDistance += engDistance;
            totalTravelMinutes += engTravelTime;
        }

        double baselineDistance = Math.Round(totalDistance * 1.1, 2);
        double economyPercent = baselineDistance > 0
            ? Math.Round((baselineDistance - totalDistance) / baselineDistance * 100, 2)
            : 0;

        var response = new OptimizationResultDto(
            PlanId: Guid.NewGuid(),
            PlanDate: request.PlanDate,
            Metrics: new PlanComparisonMetricsDto(
                TotalOrders: orders.Count,
                AssignedOrders: routes.Sum(r => r.TotalOrders),
                UnassignedOrders: 0,
                OptimizedActiveEngineers: activeEngineers,
                BaselineActiveEngineers: activeEngineers,
                SavedEngineersCount: 0,
                OptimizedTotalDistanceKm: Math.Round(totalDistance, 2),
                BaselineTotalDistanceKm: baselineDistance,
                DistanceEconomyPercent: economyPercent),
            Routes: routes,
            UnassignedOrders: []);

        return Task.FromResult(response);
    }
}
