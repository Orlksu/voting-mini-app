using Backend.Contracts.Domain.Enums;
using Backend.Contracts.Requests;
using Backend.Contracts.Responses;
using Google.OrTools.ConstraintSolver;

namespace Backend.RoutingEngine.Solvers;

/// <summary>
/// Ядро VRPTW на OR-Tools (Google.OrTools.ConstraintSolver).
///
/// Доменные решения Фазы A:
///  — открытые маршруты: бригада заканчивает смену на последней заявке
///    (виртуальные концы с нулевыми дугами, транспорт/обслуживание на них не начисляется);
///  — окно заявки = «завершение работ в окне»: ServiceStart ≥ WindowStart, ServiceEnd ≤ WindowEnd;
///  — смена: время выезда и завершения — внутри [ShiftStart, ShiftEnd];
///  — обеды: обязательный виртуальный узел бригады (длительность 60 мин, окно = каскадное окно
///    бригады, VehicleVar жёстко привязан к своей бригаде). Старт обеда выбирает солвер внутри
///    окна; «пока одни едят — другие работают» гарантировано структурно.
///    В ответе обеды выносятся отдельным списком BreakStops и не смешиваются со Stops.
///    Аппроксимация Фазы A: место обеда не моделируется — узел обеда без координат,
///    транзит к/от него нулевой. Плечи в отчёте (транзит/километраж к заявке) считаются
///    от последней ФИЗИЧЕСКОЙ точки маршрута (дорога через обед учитывается один раз),
///    а времена прибытия/работ остаются модельными (не разъезжаются с окнами).
///  — жёсткие ограничения (навык/транспорт) — через сужение домена VehicleVar;
///  — мягкие — пропуск заявки с приоритетным штрафом (Авария ≫ Подключение ≫ Ремонт/Дозаказ).
///  — Фаза B: пер-бригадные матрицы (costing транспорта через Valhalla); транзит и арк-косты
///    задаются по-бригадно (SetArcCostEvaluatorOfVehicle + AddDimensionWithVehicleTransits).
///  — Фаза C: счётная дименсия «Count» — MaxOrdersPerShift (емкость по бригадам, перебор →
///    пропуск заявки с причиной ShiftLimitExceeded); оборудование как жёсткое ограничение
///    (у бригады должно быть ВСЁ требуемое заявкой); честный baseline через BaselinePlanner.
/// </summary>
public class OrToolsVrpSolver
{
    // Максимум выполненных заявок — строго первичен («больше заявок важнее, чем меньше бригад»):
    // пенальти за пропуск должна перевешивать любую сумму фикс-стоимостей бригад
    // (VehicleFixedCost × размер парка) и весь транзит. 5_000_000 ≫ 30 бригад × 50_000 = 1_500_000.
    // Минимум бригад (VehicleFixedCost) — tie-break только внутри равного числа выполненных заявок.
    private const long UnassignedPenaltyBase = 5_000_000;
    private const long VehicleFixedCost = 50_000;
    private const long MaxHorizonMinutes = 24 * 60;
    private const int DefaultTimeLimitSeconds = 15;

    public OptimizationResultDto Solve(
        IReadOnlyList<EngineerInputDto> engineers,
        IReadOnlyList<WorkOrderInputDto> orders,
        IReadOnlyList<LunchWindow?> lunchWindows,
        RouteMatrices matrices,
        IReadOnlyList<UnassignedOrderDto> preUnassigned,
        int timeLimitSeconds)
    {
        var planId = Guid.NewGuid();
        var planDate = DateTimeOffset.UtcNow;

        if (engineers.Count == 0 || orders.Count == 0)
        {
            return BuildEmptyResult(planDate, engineers, orders, preUnassigned);
        }

        int numVehicles = engineers.Count;
        int numOrders = orders.Count;

        // Обеды как обязательные стопы бригад (виртуальные узлы).
        var lunchVehicleByStop = new List<int>();
        var lunchDurationByStop = new List<int>();
        for (int v = 0; v < numVehicles; v++)
        {
            if (lunchWindows[v] is { } lunch)
            {
                lunchVehicleByStop.Add(v);
                lunchDurationByStop.Add(lunch.DurationMinutes);
            }
        }

        int numLunches = lunchVehicleByStop.Count;
        int numStops = numOrders + numLunches;          // стопы: заявки + обеды
        int numLocations = numVehicles + numStops;      // старты + стопы — размер матриц
        int numNodes = numLocations + numVehicles;      // + виртуальные концы (открытый маршрут)

        var baseTime = engineers.Min(e => e.ShiftStart);

        long[][,] timeMatrix = matrices.TimeMinutes;
        double[][,] distanceMatrix = matrices.DistanceKm;

        // Длительности обслуживания по стопам: заявки (норматив) затем обеды.
        var serviceByStop = new int[numStops];
        for (int j = 0; j < numOrders; j++) serviceByStop[j] = orders[j].DurationMinutes;
        for (int l = 0; l < numLunches; l++) serviceByStop[numOrders + l] = lunchDurationByStop[l];

        // Открытые маршруты: старт бригады i — узел i, конец — отдельный узел numLocations + i.
        int[] starts = Enumerable.Range(0, numVehicles).ToArray();
        int[] ends = Enumerable.Range(numLocations, numVehicles).ToArray();

        var manager = new RoutingIndexManager(numNodes, numVehicles, starts, ends);
        var routing = new RoutingModel(manager);

        // Transit = дорога (матрица) + обслуживание текущего стопа. Дуги в конец — 0 (без возврата).
        // Матрицы построены только для стартов+заявок (numMatrixPoints); узлы обедов и концы
        // физических дорог не имеют — транзит по ним нулевой.
        int numMatrixPoints = numVehicles + numOrders;

        // Фаза B: транзит по-бригадный — каждая бригада ездит в costing своего транспорта,
        // поэтому у каждой бригады своя матрица времени (RouteMatrices.TimeMinutes[vehicle]).
        var vehicleTransitCallbacks = new int[numVehicles];
        for (int v = 0; v < numVehicles; v++)
        {
            long[,] vTime = timeMatrix[v];
            int cb = routing.RegisterTransitCallback((long fromIndex, long toIndex) =>
            {
                int fromNode = manager.IndexToNode(fromIndex);
                int toNode = manager.IndexToNode(toIndex);

                long travelTime = fromNode >= numMatrixPoints || toNode >= numMatrixPoints
                    ? 0
                    : vTime[fromNode, toNode];

                long serviceTime = 0;
                if (fromNode >= numVehicles && fromNode < numLocations)
                {
                    serviceTime = serviceByStop[fromNode - numVehicles];
                }

                return travelTime + serviceTime;
            });
            vehicleTransitCallbacks[v] = cb;
            routing.SetArcCostEvaluatorOfVehicle(cb, v);
        }

        routing.SetFixedCostOfAllVehicles(VehicleFixedCost);

        // Фаза C: лимит заявок за смену (MaxOrdersPerShift) — счётная дименсия.
        // Инкремент 1 при переходе в узел заявки; обеды и виртуальные концы не считаются.
        // Емкость своя у каждой бригады; переполнение → заявка не вставляется и пропускается.
        int countCallback = routing.RegisterTransitCallback((long fromIndex, long toIndex) =>
        {
            int toNode = manager.IndexToNode(toIndex);
            return toNode >= numVehicles && toNode < numVehicles + numOrders ? 1 : 0;
        });
        var orderCapacities = new long[numVehicles];
        for (int v = 0; v < numVehicles; v++)
        {
            orderCapacities[v] = engineers[v].MaxOrdersPerShift > 0 ? engineers[v].MaxOrdersPerShift : long.MaxValue;
        }
        routing.AddDimensionWithVehicleCapacity(
            countCallback,
            slack_max: 0,
            vehicle_capacities: orderCapacities,
            fix_start_cumul_to_zero: true,
            name: "Count");

        // Временная дименсия: горизонт 24 ч от baseTime, ожидание (slack) до 24 ч на дуге.
        // Дименсия учитывает разный transit по бригадам (AddDimensionWithVehicleTransits).
        routing.AddDimensionWithVehicleTransits(
            vehicleTransitCallbacks,
            slack_max: MaxHorizonMinutes,
            capacity: MaxHorizonMinutes,
            fix_start_cumul_to_zero: false,
            name: "Time");

        var timeDimension = routing.GetMutableDimension("Time");

        // Смены бригад: выезд и завершение — внутри [ShiftStart, ShiftEnd].
        for (int i = 0; i < numVehicles; i++)
        {
            var eng = engineers[i];
            long shiftStart = (long)(eng.ShiftStart - baseTime).TotalMinutes;
            long shiftEnd = (long)(eng.ShiftEnd - baseTime).TotalMinutes;

            timeDimension.CumulVar(routing.Start(i)).SetRange(Math.Max(0, shiftStart), Math.Max(0, shiftEnd));
            timeDimension.CumulVar(routing.End(i)).SetRange(Math.Max(0, shiftStart), Math.Max(0, shiftEnd));
        }

        // Окна заявок: выполнение должно уложиться целиком — CumulVar(узла) ≤ WindowEnd − Duration.
        for (int j = 0; j < numOrders; j++)
        {
            var ord = orders[j];
            long index = manager.NodeToIndex(numVehicles + j);

            // Препроцессор отсеивает заявки без окна, но защищаемся и здесь:
            // без границ окна узел остаётся без ограничений по времени.
            if (!ord.WindowStart.HasValue || !ord.WindowEnd.HasValue)
            {
                continue;
            }

            long windowStart = (long)(ord.WindowStart.Value - baseTime).TotalMinutes;
            long windowEnd = (long)(ord.WindowEnd.Value - baseTime).TotalMinutes;
            long latestServiceStart = windowEnd - ord.DurationMinutes;

            timeDimension.CumulVar(index).SetRange(Math.Max(0, windowStart), Math.Max(0, latestServiceStart));
        }

        // Обеды: окно (старт выбирает солвер) + жёсткая привязка к своей бригаде.
        for (int l = 0; l < numLunches; l++)
        {
            int vehicle = lunchVehicleByStop[l];
            var lunch = lunchWindows[vehicle]!;
            int stopNode = numVehicles + numOrders + l;
            long index = manager.NodeToIndex(stopNode);

            long windowStart = (long)(lunch.Start - baseTime).TotalMinutes;
            long windowEnd = (long)(lunch.End - baseTime).TotalMinutes;
            long latestLunchStart = windowEnd - lunch.DurationMinutes;

            timeDimension.CumulVar(index).SetRange(Math.Max(0, windowStart), Math.Max(0, latestLunchStart));

            // Обед выполняется только своей бригадой.
            routing.VehicleVar(index).SetValues(new long[] { vehicle });
        }

        // Жёсткие ограничения: навыки, транспорт, оборудование — сужение домена VehicleVar заявки.
        for (int j = 0; j < numOrders; j++)
        {
            var ord = orders[j];
            long index = manager.NodeToIndex(numVehicles + j);

            for (int i = 0; i < numVehicles; i++)
            {
                var eng = engineers[i];

                bool hasSkill = eng.Skills != null && eng.Skills.Contains(ord.RequiredSkill);
                bool hasTransport = ord.RequiredTransport == null || eng.Transport == ord.RequiredTransport;

                // Фаза C: у бригады должно быть ВСЁ оборудование, требуемое заявкой.
                bool hasEquipment = ord.RequiredEquipment is not { Count: > 0 } ||
                                    (eng.Equipment != null && ord.RequiredEquipment.All(r => eng.Equipment.Contains(r)));

                if (!hasSkill || !hasTransport || !hasEquipment)
                {
                    routing.VehicleVar(index).RemoveValue(i);
                }
            }
        }

        // Мягкие ограничения: пропуск заявки с приоритетным штрафом (только заявки — обеды обязательны).
        for (int j = 0; j < numOrders; j++)
        {
            var ord = orders[j];
            long index = manager.NodeToIndex(numVehicles + j);

            long priorityWeight = ord.Priority switch
            {
                OrderPriority.Emergency => 4_000_000, // Авария
                OrderPriority.High => 800_000,        // Подключение
                OrderPriority.Normal => 150_000,      // Ремонт
                OrderPriority.Low => 40_000,          // Дозаказ
                _ => 40_000
            };

            routing.AddDisjunction(new[] { index }, UnassignedPenaltyBase + priorityWeight);
        }

        // Параметры поиска: лимит времени + эвристика первого решения и локальный поиск.
        RoutingSearchParameters searchParameters = operations_research_constraint_solver.DefaultRoutingSearchParameters();
        searchParameters.FirstSolutionStrategy = FirstSolutionStrategy.Types.Value.ParallelCheapestInsertion;
        searchParameters.LocalSearchMetaheuristic = LocalSearchMetaheuristic.Types.Value.GuidedLocalSearch;
        searchParameters.TimeLimit = new Google.Protobuf.WellKnownTypes.Duration
        {
            Seconds = Math.Max(1, timeLimitSeconds > 0 ? timeLimitSeconds : DefaultTimeLimitSeconds)
        };

        Assignment? solution = routing.SolveWithParameters(searchParameters);

        if (solution == null)
        {
            // Решения нет даже с пропусками — все оставшиеся заявки неназначены.
            var totalUnassigned = preUnassigned.ToList();
            foreach (var order in orders)
            {
                totalUnassigned.Add(new UnassignedOrderDto(
                    order.ExternalId,
                    order.Address,
                    UnassignedReasonCode.TimeWindowMissed,
                    "Не удалось составить маршрут из-за жестких ограничений"));
            }

            return new OptimizationResultDto(
                planId,
                planDate,
                new PlanComparisonMetricsDto(
                    TotalOrders: totalUnassigned.Count,
                    AssignedOrders: 0,
                    UnassignedOrders: totalUnassigned.Count,
                    OptimizedActiveEngineers: 0,
                    BaselineActiveEngineers: 0,
                    SavedEngineersCount: Math.Max(0, engineers.Count),
                    OptimizedTotalDistanceKm: 0,
                    BaselineTotalDistanceKm: 0,
                    DistanceEconomyPercent: 0),
                new List<EngineerRouteDto>(),
                totalUnassigned);
        }

        return BuildResponse(
            planId, planDate,
            engineers, orders, lunchVehicleByStop, serviceByStop,
            manager, routing, solution, timeDimension,
            distanceMatrix, timeMatrix, baseTime, preUnassigned);
    }

    /// <summary>Пустой результат: все заявки помечаются неназначенными (при отсутствии бригад — NoQualifiedEngineer).</summary>
    public static OptimizationResultDto BuildEmptyResult(
        DateTimeOffset planDate,
        IReadOnlyList<EngineerInputDto> engineers,
        IReadOnlyList<WorkOrderInputDto> orders,
        IReadOnlyList<UnassignedOrderDto> preUnassigned)
    {
        var unassigned = preUnassigned.ToList();

        if (engineers.Count == 0)
        {
            foreach (var order in orders)
            {
                unassigned.Add(new UnassignedOrderDto(
                    order.ExternalId,
                    order.Address,
                    UnassignedReasonCode.NoQualifiedEngineer,
                    "Нет инженеров в смене."));
            }
        }

        return new OptimizationResultDto(
            Guid.NewGuid(),
            planDate,
            new PlanComparisonMetricsDto(
                TotalOrders: preUnassigned.Count + orders.Count,
                AssignedOrders: 0,
                UnassignedOrders: unassigned.Count,
                OptimizedActiveEngineers: 0,
                BaselineActiveEngineers: 0,
                SavedEngineersCount: Math.Max(0, engineers.Count),
                OptimizedTotalDistanceKm: 0,
                BaselineTotalDistanceKm: 0,
                DistanceEconomyPercent: 0),
            new List<EngineerRouteDto>(),
            unassigned);
    }

    private OptimizationResultDto BuildResponse(
        Guid planId,
        DateTimeOffset planDate,
        IReadOnlyList<EngineerInputDto> engineers,
        IReadOnlyList<WorkOrderInputDto> orders,
        IReadOnlyList<int> lunchVehicleByStop,
        int[] serviceByStop,
        RoutingIndexManager manager,
        RoutingModel routing,
        Assignment solution,
        RoutingDimension timeDimension,
        double[][,] distanceMatrix,
        long[][,] timeMatrix,
        DateTimeOffset baseTime,
        IReadOnlyList<UnassignedOrderDto> preUnassigned)
    {
        var routes = new List<EngineerRouteDto>();
        var assignedOrderIndexes = new HashSet<int>();

        int numVehicles = engineers.Count;
        int numOrders = orders.Count;
        int numLunches = lunchVehicleByStop.Count;
        int numLocations = numVehicles + numOrders + numLunches;

        // Точки, для которых есть матрицы (старты + заявки). Обеды и концы — без дорог.
        int numMatrixPoints = numVehicles + numOrders;

        // Фаза C: честный baseline — конструктивный (жадный) план на тех же матрицах.
        var globalBaseline = BaselinePlanner.ComputeGlobal(engineers, orders, distanceMatrix, timeMatrix);
        var ordersVisitedByEngineer = new int[numVehicles];

        // Выезд бригады (смена может начинаться позже начала; фиксируем по cumul стартового узла).
        // Прибытие в стоп считаем инкрементально: cumul предыдущего стопа + его обслуживание + дорога.
        // (SlackVar в возвращаемом Assignment не сохраняется — прибытие вычисляем, а не читаем.)

        double totalDistance = 0;
        int totalAssignedOrders = 0;
        int activeEngineersWithOrders = 0;

        for (int i = 0; i < numVehicles; i++)
        {
            var eng = engineers[i];
            double[,] vDistance = distanceMatrix[i];
            long[,] vTime = timeMatrix[i];

            long TravelMinutes(int from, int to) =>
                from >= numMatrixPoints || to >= numMatrixPoints ? 0 : vTime[from, to];

            double DistanceKm(int from, int to) =>
                from >= numMatrixPoints || to >= numMatrixPoints ? 0 : vDistance[from, to];

            var stops = new List<RouteStopDto>();
            var breaks = new List<BreakStopDto>();
            var routeOrderIndexes = new List<int>();

            long prevCumul = solution.Min(timeDimension.CumulVar(routing.Start(i)));
            // Последняя ФИЗИЧЕСКАЯ точка маршрута (старт или заявка). Узел обеда — виртуальный,
            // без координат: плечи считаем от реальных точек, чтобы обед не «съедал» километраж.
            int lastPhysicalNode = manager.IndexToNode(routing.Start(i));
            int previousNode = lastPhysicalNode;
            double routeDist = 0;
            long routeTravelMinutes = 0;
            int orderSequence = 1;

            long index = solution.Value(routing.NextVar(routing.Start(i)));

            while (!routing.IsEnd(index))
            {
                int currentNode = manager.IndexToNode(index);

                // Прибытие — по модельной цепочке (предыдущий стоп + его плечо): не разъезжается с cumul.
                long travel = TravelMinutes(previousNode, currentNode);
                long arrival = prevCumul + travel;
                long cumul = solution.Min(timeDimension.CumulVar(index));
                int serviceDuration = serviceByStop[currentNode - numVehicles];

                // Геометрическое плечо от последней реальной точки — для отчёта и общего километража.
                long physicalTravel = TravelMinutes(lastPhysicalNode, currentNode);
                double legKm = DistanceKm(lastPhysicalNode, currentNode);
                routeDist += legKm;
                routeTravelMinutes += physicalTravel;

                // Обед бригады — отдельный BreakStop, в Stops не попадает; точкой маршрута не является.
                int lunchIdx = currentNode - numVehicles - numOrders;
                bool isLunch = currentNode >= numVehicles + numOrders && lunchIdx < numLunches;
                if (isLunch)
                {
                    var lunchStart = baseTime.AddMinutes(cumul);
                    breaks.Add(new BreakStopDto(
                        lunchStart,
                        lunchStart.AddMinutes(serviceDuration),
                        serviceDuration));
                }
                else
                {
                    int orderIndex = currentNode - numVehicles;
                    var order = orders[orderIndex];
                    assignedOrderIndexes.Add(orderIndex);
                    routeOrderIndexes.Add(orderIndex);

                    DateTimeOffset arriveTime = baseTime.AddMinutes(arrival);
                    DateTimeOffset serviceStart = baseTime.AddMinutes(cumul);
                    DateTimeOffset serviceEnd = serviceStart.AddMinutes(order.DurationMinutes);

                    stops.Add(new RouteStopDto(
                        Sequence: orderSequence++,
                        ExternalOrderId: order.ExternalId,
                        Address: order.Address,
                        Latitude: order.Latitude ?? 0.0,
                        Longitude: order.Longitude ?? 0.0,
                        EstimatedArrival: arriveTime,
                        ServiceStart: serviceStart,
                        ServiceEnd: serviceEnd,
                        TravelDurationMinutes: (int)physicalTravel,
                        DistanceFromPrevKm: Math.Round(legKm, 2),
                        TaskType: order.TaskType ?? "Maintenance",
                        Priority: order.Priority,
                        AssignmentReason: "Оптимальное назначение по навыкам/маршруту"));

                    lastPhysicalNode = currentNode;
                }

                prevCumul = cumul + serviceDuration;
                previousNode = currentNode;
                index = solution.Value(routing.NextVar(index));
            }

            bool hasOrders = stops.Count > 0;

            if (hasOrders || breaks.Count > 0)
            {
                totalDistance += routeDist;
                totalAssignedOrders += stops.Count;
                ordersVisitedByEngineer[i] = stops.Count;
                if (hasOrders) activeEngineersWithOrders++;

                routes.Add(new EngineerRouteDto(
                    EngineerExternalId: eng.ExternalId,
                    EngineerName: eng.Name,
                    Transport: eng.Transport,
                    TotalOrders: stops.Count,
                    TotalDistanceKm: Math.Round(routeDist, 2),
                    BaselineDistanceKm: Math.Round(
                        BaselinePlanner.ComputeRouteBaseline(i, orders, routeOrderIndexes, numVehicles, vDistance), 2),
                    TotalTravelMinutes: (int)routeTravelMinutes,
                    Stops: stops,
                    EncodedPolyline: null,
                    BreakStops: breaks));
            }
        }

        // Неназначенные заявки: предварительно отсеянные + те, что солвер пропустил.
        var unassigned = preUnassigned.ToList();
        for (int j = 0; j < numOrders; j++)
        {
            if (assignedOrderIndexes.Contains(j)) continue;

            var order = orders[j];
            var (reasonCode, reasonDescription) = DetermineUnassignedReason(order, engineers, ordersVisitedByEngineer);
            unassigned.Add(new UnassignedOrderDto(order.ExternalId, order.Address, reasonCode, reasonDescription));
        }

        double baselineDistance = globalBaseline.TotalDistanceKm;
        double savedPercent = baselineDistance > 0 ? (baselineDistance - totalDistance) / baselineDistance * 100 : 0;

        var metrics = new PlanComparisonMetricsDto(
            TotalOrders: preUnassigned.Count + numOrders,
            AssignedOrders: totalAssignedOrders,
            UnassignedOrders: unassigned.Count,
            OptimizedActiveEngineers: activeEngineersWithOrders,
            BaselineActiveEngineers: globalBaseline.ActiveEngineers,
            SavedEngineersCount: Math.Max(0, globalBaseline.ActiveEngineers - activeEngineersWithOrders),
            OptimizedTotalDistanceKm: Math.Round(totalDistance, 2),
            BaselineTotalDistanceKm: Math.Round(baselineDistance, 2),
            DistanceEconomyPercent: Math.Round(savedPercent, 2));

        return new OptimizationResultDto(planId, planDate, metrics, routes, unassigned);
    }

    private (UnassignedReasonCode code, string description) DetermineUnassignedReason(
        WorkOrderInputDto order,
        IReadOnlyList<EngineerInputDto> engineers,
        int[] ordersVisitedByEngineer)
    {
        // Подходящие по жёстким ограничениям бригады (навык + транспорт + оборудование).
        var addressed = engineers
            .Select((engineer, index) => (engineer, index))
            .Where(x => x.engineer.Skills != null && x.engineer.Skills.Contains(order.RequiredSkill))
            .Where(x => order.RequiredTransport == null || x.engineer.Transport == order.RequiredTransport)
            .Where(x => order.RequiredEquipment is not { Count: > 0 } ||
                        (x.engineer.Equipment != null && order.RequiredEquipment.All(r => x.engineer.Equipment.Contains(r))))
            .ToList();

        if (addressed.Count == 0)
        {
            if (order.RequiredEquipment is { Count: > 0 } &&
                !engineers.Any(e => e.Equipment != null && order.RequiredEquipment.All(r => e.Equipment.Contains(r))))
            {
                return (UnassignedReasonCode.NoQualifiedEngineer,
                    $"Нет инженера со всем необходимым оборудованием: {string.Join(", ", order.RequiredEquipment)}.");
            }

            if (!engineers.Any(e => e.Skills != null && e.Skills.Contains(order.RequiredSkill)))
            {
                return (UnassignedReasonCode.NoQualifiedEngineer, $"Нет квалифицированного инженера с навыком '{order.RequiredSkill}'.");
            }

            if (order.RequiredTransport != null)
            {
                bool transportOk = engineers.Any(e => e.Transport == order.RequiredTransport);
                if (!transportOk)
                {
                    return (UnassignedReasonCode.NoTransportMatch, $"Нет инженера с подходящим транспортом '{order.RequiredTransport}'.");
                }
            }
        }

        // Фаза C: если все подходящие бригады уже исчерпали лимит смены — ShiftLimitExceeded.
        if (addressed.Count > 0 &&
            addressed.All(x => x.engineer.MaxOrdersPerShift > 0 &&
                               ordersVisitedByEngineer[x.index] >= x.engineer.MaxOrdersPerShift))
        {
            return (UnassignedReasonCode.ShiftLimitExceeded,
                "Все подходящие инженеры исчерпали дневной лимит заказов (MaxOrdersPerShift).");
        }

        return (UnassignedReasonCode.TimeWindowMissed, "Заявка превышает временное окно или доступную смену инженеров.");
    }
}