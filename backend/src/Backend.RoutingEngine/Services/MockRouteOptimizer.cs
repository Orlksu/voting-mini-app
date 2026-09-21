using Backend.Contracts.Interfaces;
using Backend.Contracts.Models;
using Backend.Contracts.Requests;
using Backend.Contracts.Responses;

namespace Backend.RoutingEngine.Services;

/// <summary>
/// Заглушка оптимизатора для параллельной работы фронтенда и API разработчика.
/// Возвращает реалистичные распределённые маршруты.
/// </summary>
public class MockRouteOptimizer : IRouteOptimizer
{
    public Task<OptimizeRoutesResponse> OptimizeAsync(OptimizeRoutesRequest request, CancellationToken cancellationToken = default)
    {
        var routes = new List<EngineerRouteDto>();
        var orders = request.Orders;
        var engineers = request.Engineers;

        if (engineers.Count == 0 || orders.Count == 0)
        {
            return Task.FromResult(new OptimizeRoutesResponse(
                Routes: routes,
                UnassignedOrderIds: orders.Select(o => o.Id).ToList(),
                Metrics: new RoutingMetricsDto(orders.Count, 0, orders.Count, 0, 0, 0, 5)
            ));
        }

        // Простейшее распределение заказов по кругу между доступными инженерами
        int engineerIndex = 0;
        var engineerOrdersMap = engineers.ToDictionary(e => e.Id, _ => new List<OrderDto>());

        foreach (var order in orders)
        {
            var engineer = engineers[engineerIndex % engineers.Count];
            engineerOrdersMap[engineer.Id].Add(order);
            engineerIndex++;
        }

        double totalDistance = 0;
        int totalTravelTime = 0;

        foreach (var engineer in engineers)
        {
            var assignedOrders = engineerOrdersMap[engineer.Id];
            var stops = new List<RouteStopDto>();
            var currentTime = engineer.WorkHours.Start.AddMinutes(15);
            var prevLocation = engineer.StartLocation;

            // Начальная точка (депо / старт)
            stops.Add(new RouteStopDto(
                StopOrder: 0,
                OrderId: null,
                Location: engineer.StartLocation,
                EstimatedArrival: engineer.WorkHours.Start,
                ServiceStart: engineer.WorkHours.Start,
                ServiceEnd: engineer.WorkHours.Start,
                EstimatedDeparture: currentTime,
                TravelDurationMinutes: 0,
                DistanceKm: 0
            ));

            int stopIndex = 1;
            int engTravelTime = 0;
            int engServiceTime = 0;
            double engDistance = 0;

            foreach (var order in assignedOrders)
            {
                // Примерное расстояние: 2-5 км, время в пути 10-20 минут
                double legDistance = 3.5;
                int travelMinutes = 15;

                var arrival = currentTime.AddMinutes(travelMinutes);
                // Ждём начала окна, если приехали раньше
                var serviceStart = arrival < order.TimeWindow.Start ? order.TimeWindow.Start : arrival;
                var serviceEnd = serviceStart.AddMinutes(order.DurationMinutes);
                var departure = serviceEnd;

                stops.Add(new RouteStopDto(
                    StopOrder: stopIndex++,
                    OrderId: order.Id,
                    Location: order.Location,
                    EstimatedArrival: arrival,
                    ServiceStart: serviceStart,
                    ServiceEnd: serviceEnd,
                    EstimatedDeparture: departure,
                    TravelDurationMinutes: travelMinutes,
                    DistanceKm: legDistance,
                    IsLate: arrival > order.TimeWindow.End
                ));

                currentTime = departure;
                engTravelTime += travelMinutes;
                engServiceTime += order.DurationMinutes;
                engDistance += legDistance;
            }

            routes.Add(new EngineerRouteDto(
                EngineerId: engineer.Id,
                EngineerName: engineer.Name,
                Stops: stops,
                TotalTravelTimeMinutes: engTravelTime,
                TotalServiceTimeMinutes: engServiceTime,
                TotalDistanceKm: engDistance
            ));

            totalDistance += engDistance;
            totalTravelTime += engTravelTime;
        }

        var response = new OptimizeRoutesResponse(
            Routes: routes,
            UnassignedOrderIds: [],
            Metrics: new RoutingMetricsDto(
                TotalOrders: orders.Count,
                AssignedOrders: orders.Count,
                UnassignedOrders: 0,
                TotalDistanceKm: Math.Round(totalDistance, 2),
                TotalDurationMinutes: totalTravelTime,
                SlaViolationsCount: 0,
                SolverExecutionTimeMs: 12
            )
        );

        return Task.FromResult(response);
    }
}
