using Backend.Contracts.Domain.Enums;
using Backend.Contracts.Interfaces;
using Backend.Contracts.Requests;
using Backend.Contracts.Responses;
using Backend.RoutingEngine.Helpers;

namespace Backend.RoutingEngine.Services;

/// <summary>
/// Реалистичный жадный оптимизатор (Greedy Dispatcher) для параллельной работы фронтенда и базового бенчмарка.
/// Учитывает навыки, транспорт, временные окна, границы смен и дневные лимиты инженеров.
/// </summary>
public class MockRouteOptimizer : IRouteOptimizer
{
    public Task<OptimizationResultDto> OptimizeAsync(CreateOptimizationPlanRequest request, CancellationToken cancellationToken = default)
    {
        var orders = request.Orders;
        var engineers = request.Engineers;
        var routes = new List<EngineerRouteDto>();
        var unassignedOrders = new List<UnassignedOrderDto>();

        if (orders.Count == 0)
        {
            return Task.FromResult(new OptimizationResultDto(
                PlanId: Guid.NewGuid(),
                PlanDate: request.PlanDate,
                Metrics: new PlanComparisonMetricsDto(
                    TotalOrders: 0,
                    AssignedOrders: 0,
                    UnassignedOrders: 0,
                    OptimizedActiveEngineers: 0,
                    BaselineActiveEngineers: engineers.Count,
                    SavedEngineersCount: 0,
                    OptimizedTotalDistanceKm: 0,
                    BaselineTotalDistanceKm: 0,
                    DistanceEconomyPercent: 0),
                Routes: [],
                UnassignedOrders: []));
        }

        if (engineers.Count == 0)
        {
            var allUnassigned = orders
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
                Routes: [],
                UnassignedOrders: allUnassigned));
        }
        
        var validOrders = new List<WorkOrderInputDto>();
        foreach (var order in orders)
        {
            if (order.Latitude is null || order.Longitude is null ||
                order.Latitude is < -90 or > 90 || order.Longitude is < -180 or > 180 ||
                (Math.Abs(order.Latitude.Value) < 0.0001 && Math.Abs(order.Longitude.Value) < 0.0001))
            {
                unassignedOrders.Add(new UnassignedOrderDto(
                    order.ExternalId,
                    order.Address,
                    UnassignedReasonCode.CapacityOverload,
                    "Не удалось определить точные географические координаты адреса"));
            }
            else
            {
                validOrders.Add(order);
            }
        }

     
        var engineerStates = engineers.Select(e => new EngineerState(e)).ToList();

      
        var pendingOrders = validOrders
            .OrderByDescending(o => o.Priority)
            .ThenBy(o => o.WindowStart ?? DateTimeOffset.MaxValue)
            .ThenBy(o => o.WindowEnd ?? DateTimeOffset.MaxValue)
            .ToList();

       
        foreach (var order in pendingOrders)
        {
            double orderLat = order.Latitude!.Value;
            double orderLon = order.Longitude!.Value;

            // 0. Фильтр по привязке к офису/району: заявка может быть назначена только инженеру своего офиса/района
            var districtCandidates = engineerStates
                .Where(s => string.IsNullOrWhiteSpace(order.District)
                         || string.IsNullOrWhiteSpace(s.Engineer.District)
                         || string.Equals(s.Engineer.District.Trim(), order.District.Trim(), StringComparison.OrdinalIgnoreCase))
                .ToList();

            if (districtCandidates.Count == 0)
            {
                unassignedOrders.Add(new UnassignedOrderDto(
                    order.ExternalId,
                    order.Address,
                    UnassignedReasonCode.NoQualifiedEngineer,
                    $"В офисе/районе '{order.District}' нет доступных инженеров"));
                continue;
            }

            // 1. Фильтр по квалификации (среди инженеров ТОЛЬКО этого офиса/района)
            var skillCandidates = districtCandidates
                .Where(s => s.Engineer.Skills.Contains(order.RequiredSkill))
                .ToList();

            if (skillCandidates.Count == 0)
            {
                unassignedOrders.Add(new UnassignedOrderDto(
                    order.ExternalId,
                    order.Address,
                    UnassignedReasonCode.NoQualifiedEngineer,
                    $"В офисе '{order.District}' нет инженеров с квалификацией '{order.RequiredSkill}'"));
                continue;
            }

           
            var transportCandidates = skillCandidates
                .Where(s => order.RequiredTransport == null || s.Engineer.Transport == order.RequiredTransport)
                .ToList();

            if (transportCandidates.Count == 0)
            {
                unassignedOrders.Add(new UnassignedOrderDto(
                    order.ExternalId,
                    order.Address,
                    UnassignedReasonCode.NoTransportMatch,
                    $"Для заявки требуется транспорт '{order.RequiredTransport}', доступный транспорт инженеров с требуемой квалификацией не подходит"));
                continue;
            }

         
            var capacityCandidates = transportCandidates
                .Where(s => s.Stops.Count < s.Engineer.MaxOrdersPerShift)
                .ToList();

            if (capacityCandidates.Count == 0)
            {
                unassignedOrders.Add(new UnassignedOrderDto(
                    order.ExternalId,
                    order.Address,
                    UnassignedReasonCode.ShiftLimitExceeded,
                    "Все подходящие инженеры исчерпали дневной лимит заказов (MaxOrdersPerShift)"));
                continue;
            }

            
            CandidateEvaluation? bestCandidate = null;
            bool anyShiftLimitFailed = false;
            bool anyWindowMissed = false;

            foreach (var state in capacityCandidates)
            {
                var (distanceKm, travelMinutes) = CalculateTravel(
                    state.CurrentLat, state.CurrentLon, orderLat, orderLon, state.Engineer.Transport);

                var arrival = state.CurrentTime.AddMinutes(travelMinutes);
                var serviceStart = order.WindowStart.HasValue && arrival < order.WindowStart.Value
                    ? order.WindowStart.Value
                    : arrival;
                var serviceEnd = serviceStart.AddMinutes(order.DurationMinutes);

                bool windowOk = !order.WindowEnd.HasValue || serviceStart <= order.WindowEnd.Value;
                bool shiftOk = serviceEnd <= state.Engineer.ShiftEnd;

                if (!windowOk)
                {
                    anyWindowMissed = true;
                }

                if (!shiftOk)
                {
                    anyShiftLimitFailed = true;
                }

                if (windowOk && shiftOk)
                {
                    var evaluation = new CandidateEvaluation(state, distanceKm, travelMinutes, arrival, serviceStart, serviceEnd);
                    if (bestCandidate is null ||
                        evaluation.Arrival < bestCandidate.Arrival ||
                        (evaluation.Arrival == bestCandidate.Arrival && evaluation.DistanceKm < bestCandidate.DistanceKm))
                    {
                        bestCandidate = evaluation;
                    }
                }
            }

            if (bestCandidate is not null)
            {
                var c = bestCandidate;
                var state = c.State;

                var msk = TimeSpan.FromHours(3);
                string windowInfo = order.WindowStart.HasValue && order.WindowEnd.HasValue
                    ? $" (окно клиента {order.WindowStart.Value.ToOffset(msk):HH:mm}-{order.WindowEnd.Value.ToOffset(msk):HH:mm})"
                    : string.Empty;

                string reason = $"Назначен {state.Engineer.Name} ({state.Engineer.Transport}): навык '{order.RequiredSkill}' подтверждён, " +
                                $"прибытие в {c.Arrival.ToOffset(msk):HH:mm}{windowInfo}, окончание в {c.ServiceEnd.ToOffset(msk):HH:mm} (смена до {state.Engineer.ShiftEnd.ToOffset(msk):HH:mm})";

                state.Stops.Add(new RouteStopDto(
                    Sequence: state.Stops.Count + 1,
                    ExternalOrderId: order.ExternalId,
                    Address: order.Address,
                    Latitude: orderLat,
                    Longitude: orderLon,
                    EstimatedArrival: c.Arrival,
                    ServiceStart: c.ServiceStart,
                    ServiceEnd: c.ServiceEnd,
                    TravelDurationMinutes: c.TravelMinutes,
                    DistanceFromPrevKm: c.DistanceKm,
                    TaskType: order.TaskType,
                    Priority: order.Priority,
                    AssignmentReason: reason,
                    Status: OrderStatus.Dispatched,
                    OrderId: order.InternalId));

                state.CurrentLat = orderLat;
                state.CurrentLon = orderLon;
                state.CurrentTime = c.ServiceEnd;
                state.TotalDistanceKm += c.DistanceKm;
                state.TotalTravelMinutes += c.TravelMinutes;
                state.PolylinePoints.Add((orderLat, orderLon));
            }
            else
            {
                if (anyShiftLimitFailed && !anyWindowMissed)
                {
                    unassignedOrders.Add(new UnassignedOrderDto(
                        order.ExternalId,
                        order.Address,
                        UnassignedReasonCode.ShiftLimitExceeded,
                        "Исполнители не успевают выполнить заявку до окончания своей рабочей смены"));
                }
                else if (anyWindowMissed)
                {
                    var msk = TimeSpan.FromHours(3);
                    string deadlineText = order.WindowEnd.HasValue ? $" до {order.WindowEnd.Value.ToOffset(msk):HH:mm}" : string.Empty;
                    unassignedOrders.Add(new UnassignedOrderDto(
                        order.ExternalId,
                        order.Address,
                        UnassignedReasonCode.TimeWindowMissed,
                        $"Невозможно прибыть в интервал клиента{deadlineText} с учётом текущего графика инженеров"));
                }
                else
                {
                    unassignedOrders.Add(new UnassignedOrderDto(
                        order.ExternalId,
                        order.Address,
                        UnassignedReasonCode.CapacityOverload,
                        "Превышена нагрузка на доступных исполнителей"));
                }
            }
        }

        // 5. Формирование итоговых маршрутов
        double totalDistance = 0;
        int totalTravelMinutes = 0;
        int activeEngineers = 0;

        foreach (var state in engineerStates)
        {
            if (state.Stops.Count == 0)
            {
                continue;
            }

            activeEngineers++;
            string encodedPolyline = PolylineEncoder.Encode(state.PolylinePoints);
            double routeDistance = Math.Round(state.TotalDistanceKm, 2);

            routes.Add(new EngineerRouteDto(
                EngineerExternalId: state.Engineer.ExternalId,
                EngineerName: state.Engineer.Name,
                Transport: state.Engineer.Transport,
                TotalOrders: state.Stops.Count,
                ActiveOrdersCount: state.Stops.Count,
                CompletedOrdersCount: 0,
                CancelledOrdersCount: 0,
                TotalDistanceKm: routeDistance,
                BaselineDistanceKm: Math.Round(routeDistance * 1.25, 2),
                TotalTravelMinutes: state.TotalTravelMinutes,
                Stops: state.Stops,
                EncodedPolyline: encodedPolyline,
                StartAddress: state.Engineer.StartAddress,
                StartLatitude: state.Engineer.StartLatitude,
                StartLongitude: state.Engineer.StartLongitude,
                BreakStops: []));

            totalDistance += routeDistance;
            totalTravelMinutes += state.TotalTravelMinutes;
        }

        double totalDistRounded = Math.Round(totalDistance, 2);
        double baselineDistance = Math.Round(totalDistRounded * 1.25, 2);
        double economyPercent = baselineDistance > 0
            ? Math.Round((baselineDistance - totalDistRounded) / baselineDistance * 100, 2)
            : 0;

        int assignedOrdersCount = routes.Sum(r => r.TotalOrders);
        int savedEngineers = Math.Max(0, engineers.Count - activeEngineers);

        var response = new OptimizationResultDto(
            PlanId: Guid.NewGuid(),
            PlanDate: request.PlanDate,
            Metrics: new PlanComparisonMetricsDto(
                TotalOrders: orders.Count,
                AssignedOrders: assignedOrdersCount,
                UnassignedOrders: unassignedOrders.Count,
                OptimizedActiveEngineers: activeEngineers,
                BaselineActiveEngineers: engineers.Count,
                SavedEngineersCount: savedEngineers,
                OptimizedTotalDistanceKm: totalDistRounded,
                BaselineTotalDistanceKm: baselineDistance,
                DistanceEconomyPercent: economyPercent),
            Routes: routes,
            UnassignedOrders: unassignedOrders);

        return Task.FromResult(response);
    }

    private static (double DistanceKm, int TravelMinutes) CalculateTravel(
        double lat1, double lon1, double lat2, double lon2, TransportType transport)
    {
        const double R = 6371.0;
        double dLat = (lat2 - lat1) * Math.PI / 180.0;
        double dLon = (lon2 - lon1) * Math.PI / 180.0;

        double a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2) +
                   Math.Cos(lat1 * Math.PI / 180.0) * Math.Cos(lat2 * Math.PI / 180.0) *
                   Math.Sin(dLon / 2) * Math.Sin(dLon / 2);

        double straightKm = R * 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));

        var (tortuosity, speedKmH) = transport switch
        {
            TransportType.Pedestrian => (1.20, 5.0),
            TransportType.Bicycle => (1.25, 15.0),
            TransportType.PublicTransit => (1.35, 18.0),
            TransportType.Car or _ => (1.35, 30.0)
        };

        double realKm = Math.Round(straightKm * tortuosity, 2);
        int minutes = (int)Math.Ceiling((realKm / speedKmH) * 60.0) + 3;

        return (realKm, Math.Max(minutes, 3));
    }

    private sealed class EngineerState
    {
        public EngineerInputDto Engineer { get; }
        public double CurrentLat { get; set; }
        public double CurrentLon { get; set; }
        public DateTimeOffset CurrentTime { get; set; }
        public List<RouteStopDto> Stops { get; } = [];
        public double TotalDistanceKm { get; set; }
        public int TotalTravelMinutes { get; set; }
        public List<(double Lat, double Lon)> PolylinePoints { get; } = [];

        public EngineerState(EngineerInputDto engineer)
        {
            Engineer = engineer;
            CurrentLat = engineer.StartLatitude ?? 55.7558;
            CurrentLon = engineer.StartLongitude ?? 37.6173;
            CurrentTime = engineer.ShiftStart;
            PolylinePoints.Add((CurrentLat, CurrentLon));
        }
    }

    private sealed record CandidateEvaluation(
        EngineerState State,
        double DistanceKm,
        int TravelMinutes,
        DateTimeOffset Arrival,
        DateTimeOffset ServiceStart,
        DateTimeOffset ServiceEnd);
}
