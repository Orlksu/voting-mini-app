using Backend.Api.Infrastructure.Repositories;
using Backend.Api.Services;
using Backend.Contracts.Domain.Entities;
using Backend.Contracts.Domain.Enums;
using Backend.Contracts.Domain.ValueObjects;
using Backend.Contracts.Interfaces;
using Backend.Contracts.Requests;
using Backend.Contracts.Responses;
using Microsoft.AspNetCore.Mvc;

namespace Backend.Api.Controllers;

[ApiController]
[Route("api/v1/optimization")]
public class OptimizationController : ControllerBase
{
    private readonly IRouteOptimizer _optimizer;
    private readonly OptimizationJobStore _jobStore;
    private readonly IEngineerRepository _engineers;
    private readonly IWorkOrderRepository _workOrders;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<OptimizationController> _logger;
    private readonly LivePlanStateBuilder _liveState;

    public OptimizationController(
        IRouteOptimizer optimizer,
        OptimizationJobStore jobStore,
        IEngineerRepository engineers,
        IWorkOrderRepository workOrders,
        IServiceScopeFactory scopeFactory,
        ILogger<OptimizationController> logger,
        LivePlanStateBuilder liveState)
    {
        _optimizer = optimizer;
        _jobStore = jobStore;
        _engineers = engineers;
        _workOrders = workOrders;
        _scopeFactory = scopeFactory;
        _logger = logger;
        _liveState = liveState;
    }

    /// <summary>
    /// Постановка задачи в очередь расчета на реальных данных из БД:
    /// доступные инженеры и нераспределённые заявки (NotDispatched).
    /// Опционально фильтрует по идентификатору депо (depotId), району (district) или дате (date).
    /// </summary>
    /// <param name="depotId">Опциональный идентификатор депо (DepotId) для ограничения расчета.</param>
    /// <param name="district">Опциональный район (District) для ограничения расчета.</param>
    /// <param name="date">Опциональная целевая дата оптимизации (фильтрует заявки строго на эту дату в MSK).</param>
    /// <param name="ct">Токен отмены запроса.</param>
    [HttpPost("jobs")]
    [ProducesResponseType<JobStatusResponseDto>(StatusCodes.Status202Accepted)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<JobStatusResponseDto>> CreateJob(
        [FromQuery] Guid? depotId = null,
        [FromQuery] string? district = null,
        [FromQuery] DateTimeOffset? date = null,
        CancellationToken ct = default)
    {
        var mskOffset = TimeSpan.FromHours(3);
        DateTimeOffset targetDate;
        if (date.HasValue)
        {
            targetDate = date.Value;
        }
        else
        {
            var nextOrder = (await _workOrders.GetPendingOrdersForPlanningAsync(depotId, district, null, OrderStatus.NotDispatched, ct))
                .OrderBy(o => o.PreferredWindow.Start)
                .FirstOrDefault();

            if (nextOrder != null)
            {
                var msk = nextOrder.PreferredWindow.Start.ToOffset(mskOffset);
                targetDate = new DateTimeOffset(msk.Year, msk.Month, msk.Day, 0, 0, 0, mskOffset);
            }
            else
            {
                var nowMsk = DateTimeOffset.UtcNow.ToOffset(mskOffset);
                targetDate = new DateTimeOffset(nowMsk.Year, nowMsk.Month, nowMsk.Day, 0, 0, 0, mskOffset);
            }
        }

        var engEntities = await _engineers.GetAvailableEngineersAsync(depotId, district, ct);
        var engineers = engEntities.Select(ToEngineerInputDto).ToList();

        var woEntities = await _workOrders.GetPendingOrdersForPlanningAsync(depotId, district, targetDate, OrderStatus.NotDispatched, ct);
        var orders = woEntities.Select(ToWorkOrderInputDto).ToList();

        if (engineers.Count == 0)
            return BadRequest("Нет доступных инженеров для оптимизации (проверьте /api/v1/engineers).");
        if (orders.Count == 0)
            return BadRequest($"Нет нераспределённых заявок для оптимизации на дату {targetDate:yyyy-MM-dd}.");

        var planDateUtc = targetDate.ToUniversalTime();
        var planDateMsk = targetDate.ToOffset(mskOffset);

        var alignedEngineers = engineers.Select(e =>
        {
            var shiftDuration = e.ShiftEnd - e.ShiftStart;
            if (shiftDuration <= TimeSpan.Zero)
            {
                shiftDuration += TimeSpan.FromDays(1);
            }

            var shiftMskStart = e.ShiftStart.ToOffset(mskOffset);
            var alignedStartMsk = new DateTimeOffset(
                planDateMsk.Year, planDateMsk.Month, planDateMsk.Day,
                shiftMskStart.Hour, shiftMskStart.Minute, shiftMskStart.Second,
                mskOffset);

            var alignedStartUtc = alignedStartMsk.ToUniversalTime();
            var alignedEndUtc = alignedStartUtc + shiftDuration;

            return e with { ShiftStart = alignedStartUtc, ShiftEnd = alignedEndUtc };
        }).ToList();

        var request = new CreateOptimizationPlanRequest(planDateUtc, alignedEngineers, orders);

        _jobStore.SetLastEngineers(request.Engineers);
        var job = _jobStore.Create();
        _logger.LogInformation("Job {JobId}: оптимизация на {PlanDate} ({Engineers} инженеров, {Orders} заявок)",
            job.JobId, request.PlanDate, request.Engineers.Count, request.Orders.Count);

        RunJob(job.JobId, request);

        return AcceptedAtAction(nameof(GetJobStatus), new { jobId = job.JobId }, job);
    }

    /// <summary>
    /// Оперативное перепланирование в один клик на основе текущего состояния БД:
    /// берет всех доступных инженеров и все актуальные незавершенные заявки (NotDispatched и Dispatched), исключая Cancelled и Completed.
    /// Опционально фильтрует по идентификатору депо (depotId) или району (district).
    /// </summary>
    /// <param name="depotId">Опциональный идентификатор депо (DepotId) для ограничения расчета.</param>
    /// <param name="district">Опциональный район (District) для ограничения расчета.</param>
    /// <param name="date">Опциональная целевая дата перепланирования (фильтрует заявки строго на эту дату в MSK).</param>
    /// <param name="ct">Токен отмены запроса.</param>
    [HttpPost("replan")]
    [ProducesResponseType<JobStatusResponseDto>(StatusCodes.Status202Accepted)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<JobStatusResponseDto>> Replan(
        [FromQuery] Guid? depotId = null,
        [FromQuery] string? district = null,
        [FromQuery] DateTimeOffset? date = null,
        [FromQuery] DateTimeOffset? currentTime = null,
        CancellationToken ct = default)
    {
        var mskOffset = TimeSpan.FromHours(3);
        DateTimeOffset targetDate;
        if (date.HasValue)
        {
            targetDate = date.Value;
        }
        else
        {
            var nextOrder = (await _workOrders.GetPendingOrdersForPlanningAsync(depotId, district, null, status: null, ct: ct))
                .OrderBy(o => o.PreferredWindow.Start)
                .FirstOrDefault();

            if (nextOrder != null)
            {
                var msk = nextOrder.PreferredWindow.Start.ToOffset(mskOffset);
                targetDate = new DateTimeOffset(msk.Year, msk.Month, msk.Day, 0, 0, 0, mskOffset);
            }
            else
            {
                var nowMsk = DateTimeOffset.UtcNow.ToOffset(mskOffset);
                targetDate = new DateTimeOffset(nowMsk.Year, nowMsk.Month, nowMsk.Day, 0, 0, 0, mskOffset);
            }
        }

        // Если передан currentTime (симуляция) — используем его, иначе реальное время сервера
        var nowUtc = currentTime ?? DateTimeOffset.UtcNow;
        var nowMskCurrent = nowUtc.ToOffset(mskOffset);
        // При симуляции считаем targetDate "сегодняшним" днём относительно переданного времени
        bool isToday = targetDate.Date == nowMskCurrent.Date;

        List<EngineerInputDto> finalEngineers;
        List<WorkOrderInputDto> finalOrders;

        // Пытаемся использовать Live State (учет реального/симулированного времени суток)
        var contexts = isToday ? await _liveState.BuildAsync(nowUtc, ct) : [];

        if (isToday && contexts.Count > 0)
        {
            _logger.LogInformation("Replan: используется Live State (от {Now})", nowUtc);
            
            var dbEngs = await _engineers.GetAvailableEngineersAsync(depotId, district, ct);
            var baseEngineers = dbEngs.Select(ToEngineerInputDto).ToList();
            var allEngineers = new List<EngineerInputDto>();
            
            foreach (var baseEng in baseEngineers)
            {
                var ctx = contexts.FirstOrDefault(c => string.Equals(c.Engineer.ExternalId, baseEng.ExternalId, StringComparison.OrdinalIgnoreCase));
                allEngineers.Add(ctx != null ? ctx.Engineer : baseEng);
            }
            
            var allRemaining = await _liveState.BuildRemainingOrdersAsync(contexts, targetDate, null, ct);

            finalEngineers = allEngineers;
            finalOrders = allRemaining; // Оптимизатор сам разберется с District Penalty
        }
        else
        {
            _logger.LogInformation("Replan: используется планирование с нуля (Live State недоступен или дата не сегодня)");
            var engEntities = await _engineers.GetAvailableEngineersAsync(depotId, district, ct);
            var engineers = engEntities.Select(ToEngineerInputDto).ToList();
            
            var woEntities = await _workOrders.GetPendingOrdersForPlanningAsync(depotId, district, targetDate, status: null, ct: ct);
            var orders = woEntities.Select(ToWorkOrderInputDto).ToList();
            
            var planDateMsk = targetDate.ToOffset(mskOffset);
            finalEngineers = engineers.Select(e =>
            {
                var shiftDuration = e.ShiftEnd - e.ShiftStart;
                if (shiftDuration <= TimeSpan.Zero) shiftDuration += TimeSpan.FromDays(1);
                var shiftMskStart = e.ShiftStart.ToOffset(mskOffset);
                var alignedStartMsk = new DateTimeOffset(planDateMsk.Year, planDateMsk.Month, planDateMsk.Day, shiftMskStart.Hour, shiftMskStart.Minute, shiftMskStart.Second, mskOffset);
                var alignedStartUtc = alignedStartMsk.ToUniversalTime();
                return e with { ShiftStart = alignedStartUtc, ShiftEnd = alignedStartUtc + shiftDuration };
            }).ToList();
            
            finalOrders = orders;
        }

        if (finalEngineers.Count == 0)
            return BadRequest("Нет доступных инженеров для перепланирования.");
        if (finalOrders.Count == 0)
            return BadRequest($"Нет незавершенных заявок для перепланирования на дату {targetDate:yyyy-MM-dd}.");

        var planDateUtc = targetDate.ToUniversalTime();
        var request = new CreateOptimizationPlanRequest(isToday && contexts.Count > 0 ? nowUtc : planDateUtc, finalEngineers, finalOrders);

        _jobStore.SetLastEngineers(request.Engineers);
        var job = _jobStore.Create();
        _logger.LogInformation("Job {JobId}: оперативное перепланирование на {PlanDate} ({Engineers} инженеров, {Orders} заявок)",
            job.JobId, request.PlanDate, request.Engineers.Count, request.Orders.Count);

        RunJob(job.JobId, request);

        return AcceptedAtAction(nameof(GetJobStatus), new { jobId = job.JobId }, job);
    }

    /// <summary>
    /// Проверка статуса задачи (polling)
    /// </summary>
    [HttpGet("jobs/{jobId:guid}")]
    [ProducesResponseType<JobStatusResponseDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<JobStatusResponseDto>> GetJobStatus(
        Guid jobId, 
        [FromServices] IOperationResultRepository opRepo, 
        CancellationToken ct = default)
    {
        var job = _jobStore.GetStatus(jobId);
        if (job != null)
            return Ok(job);

        var dbResult = await opRepo.GetByJobIdWithDetailsAsync(jobId, ct);
        if (dbResult != null)
        {
            return Ok(new JobStatusResponseDto(jobId, JobStatus.Completed, null, dbResult.CreatedAt));
        }

        return NotFound();
    }

    /// <summary>
    /// Получение рассчитанного плана
    /// </summary>
    [HttpGet("jobs/{jobId:guid}/result")]
    [ProducesResponseType<OptimizationResultDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<OptimizationResultDto>> GetJobResult(
        Guid jobId, 
        [FromServices] IOperationResultRepository opRepo, 
        CancellationToken ct = default)
    {
        if (_jobStore.TryGetResult(jobId, out var result) && result != null)
        {
            return Ok(result);
        }

        var dbResult = await opRepo.GetByJobIdWithDetailsAsync(jobId, ct);
        if (dbResult != null)
        {
            return Ok(MapToDto(dbResult));
        }

        return NotFound();
    }

    /// <summary>
    /// Расчет оперативного перепланирования
    /// </summary>
    [HttpPost("reschedule")]
    [ProducesResponseType<JobStatusResponseDto>(StatusCodes.Status202Accepted)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<JobStatusResponseDto>> Reschedule([FromBody] ReschedulePlanRequest request, CancellationToken ct = default)
    {
        var planRequest = await BuildPlanRequestAsync(request, ct);
        if (planRequest == null)
        {
            return BadRequest("Перепланирование требует доступных инженеров и активных заявок.");
        }

        var job = _jobStore.Create();
        _logger.LogInformation("Job {JobId}: перепланирование ({EventType}) на {EventTime}",
            job.JobId, request.EventType, request.EventTime);

        RunJob(job.JobId, planRequest, request.EventType);

        return AcceptedAtAction(nameof(GetJobStatus), new { jobId = job.JobId }, job);
    }

    /// <summary>
    /// Оперативное перепланирование при поступлении аварийной заявки.
    /// Система автоматически определяет текущее состояние каждого инженера по расписанию плана
    /// (time-based inference): кто завершил работу, кто сейчас на объекте, кто свободен.
    /// Никакого ручного ввода диспетчера не требуется.
    /// </summary>
    [HttpPost("emergency")]
    [ProducesResponseType<JobStatusResponseDto>(StatusCodes.Status202Accepted)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<JobStatusResponseDto>> Emergency(
        [FromBody] EmergencyOrderRequest request,
        CancellationToken ct = default)
    {
        var now = request.EventTime ?? DateTimeOffset.UtcNow;
        var mskOffset = TimeSpan.FromHours(3);

        // 1. Строим live-состояние каждого инженера по времени now
        var contexts = await _liveState.BuildAsync(now, ct);

        // 2. Берём всех инженеров из БД, накладываем на них Live State (если есть)
        var engEntities = await _engineers.GetAvailableEngineersAsync(ct: ct);
        var baseEngineers = engEntities.Select(e => new EngineerInputDto(
            ExternalId: e.ExternalId,
            Name: e.Name,
            StartAddress: e.StartLocation.RawAddress,
            StartLatitude: e.StartLocation.Coordinates.Latitude,
            StartLongitude: e.StartLocation.Coordinates.Longitude,
            ShiftStart: now,
            ShiftEnd: e.Shift.End,
            Transport: e.Transport,
            Skills: [.. e.Skills],
            MaxOrdersPerShift: e.MaxOrdersPerShift,
            District: e.District,
            DepotId: e.DepotId)).ToList();
            
        List<EngineerInputDto> engineers = new List<EngineerInputDto>();
        foreach (var baseEng in baseEngineers)
        {
            var ctx = contexts.FirstOrDefault(c => string.Equals(c.Engineer.ExternalId, baseEng.ExternalId, StringComparison.OrdinalIgnoreCase));
            engineers.Add(ctx != null ? ctx.Engineer : baseEng);
        }

        if (engineers.Count == 0)
            return BadRequest("Нет доступных инженеров для перепланирования.");

      
        var remainingOrders = await _liveState.BuildRemainingOrdersAsync(
            contexts,
            targetDate: now.ToOffset(mskOffset).Date == DateTimeOffset.UtcNow.ToOffset(mskOffset).Date
                ? null
                : now,
            excludeExternalId: null,
            ct);

        var urgentOrder = request.UrgentOrder;
        
        // Геокодируем ручную аварийную заявку, если координаты не переданы
        if ((!urgentOrder.Latitude.HasValue || !urgentOrder.Longitude.HasValue) && !string.IsNullOrWhiteSpace(urgentOrder.Address))
        {
            var geocoder = HttpContext.RequestServices.GetRequiredService<Backend.Api.Services.GeoCodingService.IGeocoder>();
            var geo = await geocoder.GeocodeAsync(urgentOrder.Address, urgentOrder.District, ct);
            if (geo != null)
            {
                urgentOrder = urgentOrder with
                {
                    Latitude = geo.Latitude,
                    Longitude = geo.Longitude,
                    Address = geo.DisplayName // обновляем адрес на отформатированный, если нужно
                };
            }
            else
            {
                return BadRequest($"Не удалось определить координаты для адреса аварии: {urgentOrder.Address}");
            }
        }

        urgentOrder = urgentOrder with
        {
            Priority = OrderPriority.Emergency,
            Status = OrderStatus.NotDispatched
        };

        var newOrderEntity = new Backend.Contracts.Domain.Entities.WorkOrder
        {
            Id = Guid.NewGuid(),
            ExternalId = urgentOrder.ExternalId,
            District = urgentOrder.District ?? string.Empty,
            Location = new Backend.Contracts.Domain.ValueObjects.Location
            {
                RawAddress = urgentOrder.Address ?? string.Empty,
                Coordinates = new Backend.Contracts.Domain.ValueObjects.GeoCoordinate(urgentOrder.Latitude ?? 0, urgentOrder.Longitude ?? 0)
            },
            PreferredWindow = new Backend.Contracts.Domain.ValueObjects.TimeWindow(
                urgentOrder.WindowStart ?? now,
                urgentOrder.WindowEnd ?? now.AddHours(2)
            ),
            ServiceDuration = TimeSpan.FromMinutes(urgentOrder.DurationMinutes),
            RequiredSkill = urgentOrder.RequiredSkill,
            RequiredTransport = urgentOrder.RequiredTransport,
            Priority = urgentOrder.Priority,
            Status = urgentOrder.Status,
            TaskType = urgentOrder.TaskType
        };
        
        await _workOrders.AddAsync(newOrderEntity, ct);
        urgentOrder = urgentOrder with { InternalId = newOrderEntity.Id };
        
        remainingOrders.Add(urgentOrder);

        if (remainingOrders.Count == 0)
            return BadRequest("Нет активных заявок для перепланирования.");

        var planRequest = new CreateOptimizationPlanRequest(now, engineers, remainingOrders);
        _jobStore.SetLastEngineers(engineers);

        var job = _jobStore.Create();
        _logger.LogInformation(
            "Job {JobId}: аварийное перепланирование на {Now} (инженеров: {Engineers}, заявок: {Orders}, аварийная: {UrgentId})",
            job.JobId, now, engineers.Count, remainingOrders.Count, request.UrgentOrder.ExternalId);

        RunJob(job.JobId, planRequest, RescheduleEventType.NewUrgentOrder);

        return AcceptedAtAction(nameof(GetJobStatus), new { jobId = job.JobId }, job);
    }

    private void RunJob(Guid jobId, CreateOptimizationPlanRequest request, RescheduleEventType? eventType = null)
    {
        _ = Task.Run(async () =>
        {
            _jobStore.MoveToProcessing(jobId);
            try
            {
                var result = await _optimizer.OptimizeAsync(request, CancellationToken.None);

                // Фаза D: при внезапной аварии (перепланирование) заявки, не вошедшие в новый план
                // из-за срочности, помечаем причиной «вытеснена срочной заявкой».
                if (eventType == RescheduleEventType.NewUrgentOrder)
                {
                    result = result with
                    {
                        UnassignedOrders = result.UnassignedOrders
                            .Select(u => u.ReasonCode == UnassignedReasonCode.TimeWindowMissed
                                ? u with
                                {
                                    ReasonCode = UnassignedReasonCode.EmergencyDisplaced,
                                    ReasonDescription = "Заявка вытеснена срочной (авария) при перепланировании."
                                }
                                : u)
                            .ToList()
                    };
                }

                // Персистим рассчитанный план в БД и переводим статус заявок в Dispatched
                try
                {
                    using var scope = _scopeFactory.CreateScope();
                    var opRepo = scope.ServiceProvider.GetRequiredService<IOperationResultRepository>();
                    var engRepo = scope.ServiceProvider.GetRequiredService<IEngineerRepository>();
                    var woRepo = scope.ServiceProvider.GetRequiredService<IWorkOrderRepository>();

                    var allEngineers = await engRepo.GetAllAsync(CancellationToken.None);
                    var engineersByExtId = allEngineers
                        .GroupBy(e => e.ExternalId, StringComparer.OrdinalIgnoreCase)
                        .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

                    var nowUtc = DateTimeOffset.UtcNow;
                    var operationResult = new OperationResult
                    {
                        Id = result.PlanId,
                        JobId = jobId,
                        PlanDate = result.PlanDate.ToUniversalTime(),
                        CreatedAt = nowUtc,
                        Metrics = new PlanMetrics(
                            result.Metrics.TotalOrders,
                            result.Metrics.AssignedOrders,
                            result.Metrics.UnassignedOrders,
                            result.Metrics.OptimizedActiveEngineers,
                            result.Metrics.BaselineActiveEngineers,
                            result.Metrics.SavedEngineersCount,
                            result.Metrics.OptimizedTotalDistanceKm,
                            result.Metrics.BaselineTotalDistanceKm,
                            result.Metrics.DistanceEconomyPercent),
                        Routes = result.Routes.Select(r =>
                        {
                            engineersByExtId.TryGetValue(r.EngineerExternalId, out var eng);
                            var routeId = Guid.NewGuid();
                            return new EngineerRoute
                            {
                                Id = routeId,
                                OperationResultId = result.PlanId,
                                EngineerId = eng?.Id ?? Guid.Empty,
                                EngineerExternalId = r.EngineerExternalId,
                                EngineerName = r.EngineerName,
                                Transport = r.Transport,
                                CreatedAt = nowUtc,
                                BaselineDistanceKm = r.BaselineDistanceKm,
                                EncodedPolyline = r.EncodedPolyline,
                                Stops = r.Stops.Select(s => new RouteStop
                                {
                                    Id = Guid.NewGuid(),
                                    EngineerRouteId = routeId,
                                    Sequence = s.Sequence,
                                    OrderId = s.OrderId,
                                    ExternalOrderId = s.ExternalOrderId,
                                    Location = new Location
                                    {
                                        RawAddress = s.Address ?? string.Empty,
                                        Coordinates = new GeoCoordinate(s.Latitude, s.Longitude)
                                    },
                                    EstimatedArrival = s.EstimatedArrival.ToUniversalTime(),
                                    ServiceStart = s.ServiceStart.ToUniversalTime(),
                                    ServiceEnd = s.ServiceEnd.ToUniversalTime(),
                                    DistanceFromPreviousKm = s.DistanceFromPrevKm,
                                    TravelTimeFromPrevious = TimeSpan.FromMinutes(s.TravelDurationMinutes),
                                    WaitingDuration = s.ServiceStart > s.EstimatedArrival
                                        ? s.ServiceStart - s.EstimatedArrival
                                        : TimeSpan.Zero,
                                    AssignmentReason = s.AssignmentReason
                                }).ToList()
                            };
                        }).ToList(),
                        UnassignedOrders = result.UnassignedOrders.Select(u => new UnassignedOrder
                        {
                            Id = Guid.NewGuid(),
                            OperationResultId = result.PlanId,
                            ExternalOrderId = u.ExternalOrderId,
                            ReasonCode = u.ReasonCode,
                            ReasonDescription = u.ReasonDescription
                        }).ToList()
                    };

                    await opRepo.SaveResultAsync(operationResult, CancellationToken.None);

                    // Назначенные заявки: берем точные InternalId (Guid) из остановок маршрутов
                    var assignedIds = result.Routes
                        .SelectMany(r => r.Stops)
                        .Select(s => s.OrderId)
                        .Where(id => id.HasValue)
                        .Select(id => id!.Value)
                        .Distinct()
                        .ToList();

                    // Резервное сопоставление по (ExternalId, District), если OrderId не был заполнен солвером
                    if (assignedIds.Count == 0 && result.Routes.Any(r => r.Stops.Count > 0))
                    {
                        var assignedExtStops = result.Routes
                            .SelectMany(r => r.Stops)
                            .Select(s => s.ExternalOrderId.Trim())
                            .ToHashSet(StringComparer.OrdinalIgnoreCase);

                        var fallbackOrders = request.Orders
                            .Where(o => assignedExtStops.Contains(o.ExternalId.Trim()))
                            .ToList();

                        assignedIds = fallbackOrders
                            .Where(o => o.InternalId.HasValue)
                            .Select(o => o.InternalId!.Value)
                            .Distinct()
                            .ToList();

                        var missingAssigned = fallbackOrders
                            .Where(o => !o.InternalId.HasValue)
                            .Select(o => (o.ExternalId, o.District))
                            .ToList();

                        if (missingAssigned.Count > 0)
                        {
                            var fetched = await woRepo.GetByExternalIdsAndDistrictsAsync(missingAssigned, CancellationToken.None);
                            assignedIds.AddRange(fetched.Select(f => f.Id));
                        }
                    }

                    if (assignedIds.Count > 0)
                    {
                        await woRepo.UpdateStatusesAsync(assignedIds.Distinct(), OrderStatus.Dispatched, CancellationToken.None);
                    }

                    // Неназначенные заявки: все заказы из запроса, не попавшие в назначенные
                    var assignedSet = assignedIds.ToHashSet();
                    var unassignedFromRequest = request.Orders
                        .Where(o => o.InternalId.HasValue && !assignedSet.Contains(o.InternalId.Value))
                        .ToList();

                    var unassignedIds = unassignedFromRequest
                        .Select(o => o.InternalId!.Value)
                        .Distinct()
                        .ToList();

                    var missingUnassigned = request.Orders
                        .Where(o => !o.InternalId.HasValue)
                        .Select(o => (o.ExternalId, o.District))
                        .ToList();

                    if (missingUnassigned.Count > 0)
                    {
                        var fetched = await woRepo.GetByExternalIdsAndDistrictsAsync(missingUnassigned, CancellationToken.None);
                        unassignedIds.AddRange(fetched.Where(f => !assignedSet.Contains(f.Id)).Select(f => f.Id));
                    }

                    if (unassignedIds.Count > 0)
                    {
                        await woRepo.UpdateStatusesAsync(unassignedIds.Distinct(), OrderStatus.NotDispatched, CancellationToken.None);
                    }

                    _jobStore.Complete(jobId, result);

                    _logger.LogInformation("Job {JobId}: маршруты успешно сохранены в БД ({RoutesCount} маршрутов, {OrdersCount} назначенных заявок обновлены на Dispatched, {UnassignedCount} неназначенных заявок возвращены в NotDispatched)",
                        jobId, operationResult.Routes.Count, assignedIds.Count, unassignedIds.Count);
                }
                catch (Exception persistEx)
                {
                    _logger.LogError(persistEx, "Job {JobId}: ошибка сохранения рассчитанного плана в БД", jobId);
                    _jobStore.Fail(jobId, $"Ошибка сохранения рассчитанного плана в БД: {persistEx.Message}");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Job {JobId}: ошибка расчёта", jobId);
                _jobStore.Fail(jobId, ex.Message);
            }
        });
    }

    private async Task<CreateOptimizationPlanRequest?> BuildPlanRequestAsync(ReschedulePlanRequest request, CancellationToken ct)
    {
        var engineers = _jobStore.LastEngineers;
        if (engineers is null || engineers.Count == 0)
        {
            // Fallback на базу данных, если после перезапуска сервиса in-memory кэш пуст
            var engEntities = await _engineers.GetAvailableEngineersAsync(ct: ct);
            engineers = engEntities.Select(ToEngineerInputDto).ToList();
            if (engineers.Count > 0)
            {
                _jobStore.SetLastEngineers(engineers);
            }
        }

        if (engineers.Count == 0)
        {
            return null;
        }

        // Исключаем недоступного инженера и обновляем live-состояние остальных
        var statesByEngineer = request.EngineersStates
            .GroupBy(s => s.EngineerExternalId.Trim(), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.Last(), StringComparer.OrdinalIgnoreCase);

        var updatedEngineers = engineers
            .Where(e => string.IsNullOrWhiteSpace(request.UnavailableEngineerExternalId) ||
                        !string.Equals(e.ExternalId.Trim(), request.UnavailableEngineerExternalId.Trim(), StringComparison.OrdinalIgnoreCase))
            .Select(e => statesByEngineer.TryGetValue(e.ExternalId.Trim(), out var state)
                ? e with
                {
                    StartLatitude = state.CurrentLatitude,
                    StartLongitude = state.CurrentLongitude,
                    ShiftStart = state.AvailableFromTime
                }
                : e)
            .ToList();

        // Оставшиеся заявки + новая срочная (если есть)
        var orders = request.RemainingOrders
            .Where(o => string.IsNullOrWhiteSpace(request.CancelledOrderExternalId) ||
                        !string.Equals(o.ExternalId.Trim(), request.CancelledOrderExternalId.Trim(), StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (request.NewUrgentOrder != null)
        {
            orders.Add(request.NewUrgentOrder);
        }

        if (updatedEngineers.Count == 0 || orders.Count == 0)
        {
            return null;
        }

        return new CreateOptimizationPlanRequest(request.EventTime, updatedEngineers, orders);
    }

    private static EngineerInputDto ToEngineerInputDto(Engineer e) => new(
        ExternalId: e.ExternalId,
        Name: e.Name,
        StartAddress: e.StartLocation.RawAddress,
        StartLatitude: e.StartLocation.Coordinates.Latitude,
        StartLongitude: e.StartLocation.Coordinates.Longitude,
        ShiftStart: e.Shift.Start,
        ShiftEnd: e.Shift.End,
        Transport: e.Transport,
        Skills: [.. e.Skills],
        MaxOrdersPerShift: e.MaxOrdersPerShift,
        District: e.District,
        DepotId: e.DepotId);

    private static WorkOrderInputDto ToWorkOrderInputDto(WorkOrder o) => new()
    {
        InternalId = o.Id,
        ExternalId = o.ExternalId,
        District = o.District,
        Depot = new DepotDto(o.District, o.Location?.RawAddress ?? string.Empty,
            o.Location?.Coordinates.Latitude ?? 0, o.Location?.Coordinates.Longitude ?? 0),
        Address = o.Location?.RawAddress ?? string.Empty,
        Latitude = o.Location?.Coordinates.Latitude,
        Longitude = o.Location?.Coordinates.Longitude,
        WindowStart = o.PreferredWindow.Start,
        WindowEnd = o.PreferredWindow.End,
        RequiredSkill = o.RequiredSkill,
        RequiredTransport = o.RequiredTransport,
        Priority = o.Priority,
        TaskType = o.TaskType,
        DurationMinutes = (int)o.ServiceDuration.TotalMinutes,
        Status = o.Status,
    };

    private static OptimizationResultDto MapToDto(OperationResult op) => new(
        op.Id,
        op.PlanDate,
        new PlanComparisonMetricsDto(
            op.Metrics.TotalOrders,
            op.Metrics.AssignedOrders,
            op.Metrics.UnassignedOrders,
            op.Metrics.OptimizedActiveEngineers,
            op.Metrics.BaselineActiveEngineers,
            op.Metrics.SavedEngineersCount,
            op.Metrics.OptimizedTotalDistanceKm,
            op.Metrics.BaselineTotalDistanceKm,
            op.Metrics.DistanceEconomyPercent),
        op.Routes.Select(r => new EngineerRouteDto(
            EngineerExternalId: r.EngineerExternalId,
            EngineerName: r.EngineerName,
            Transport: r.Transport,
            TotalOrders: r.Stops.Count,
            ActiveOrdersCount: r.Stops.Count,
            CompletedOrdersCount: 0,
            CancelledOrdersCount: 0,
            TotalDistanceKm: r.TotalDistanceKm,
            BaselineDistanceKm: r.BaselineDistanceKm ?? Math.Round(r.TotalDistanceKm * 1.25, 2),
            TotalTravelMinutes: (int)r.TotalTravelTime.TotalMinutes,
            Stops: r.Stops.OrderBy(s => s.Sequence).Select(s => new RouteStopDto(
                Sequence: s.Sequence,
                ExternalOrderId: s.ExternalOrderId,
                Address: s.Location?.RawAddress ?? string.Empty,
                Latitude: s.Location?.Coordinates.Latitude ?? 0.0,
                Longitude: s.Location?.Coordinates.Longitude ?? 0.0,
                EstimatedArrival: s.EstimatedArrival,
                ServiceStart: s.ServiceStart,
                ServiceEnd: s.ServiceEnd,
                TravelDurationMinutes: (int)s.TravelTimeFromPrevious.TotalMinutes,
                DistanceFromPrevKm: s.DistanceFromPreviousKm,
                TaskType: string.Empty,
                Priority: OrderPriority.Normal,
                AssignmentReason: s.AssignmentReason,
                Status: OrderStatus.Dispatched,
                OrderId: s.OrderId
            )).ToList(),
            EncodedPolyline: r.EncodedPolyline,
            BreakStops: []
        )).ToList(),
        op.UnassignedOrders.Select(u => new UnassignedOrderDto(
            u.ExternalOrderId,
            string.Empty,
            u.ReasonCode,
            u.ReasonDescription
        )).ToList());

    /// <summary>
    /// Сэмпл-запрос для быстрой проверки распределения без БД.
    /// </summary>
    [HttpGet("sample-request")]
    [ProducesResponseType<CreateOptimizationPlanRequest>(StatusCodes.Status200OK)]
    public ActionResult<CreateOptimizationPlanRequest> GetSampleRequest()
    {
        var date = DateTimeOffset.UtcNow.Date.AddHours(9);
        var engineers = new List<EngineerInputDto>
        {
            new(
                ExternalId: "ENG-01",
                Name: "Иван Смирнов",
                StartAddress: "г. Москва, ул. Тверская, 7",
                StartLatitude: 55.7576,
                StartLongitude: 37.6111,
                ShiftStart: date,
                ShiftEnd: date.AddHours(9),
                Transport: TransportType.Car,
                Skills: [SkillType.Installation, SkillType.LocalRepair],
                MaxOrdersPerShift: 10
            ),
            new(
                ExternalId: "ENG-02",
                Name: "Алексей Новиков",
                StartAddress: "г. Москва, пр-т Мира, 40",
                StartLatitude: 55.7820,
                StartLongitude: 37.6334,
                ShiftStart: date,
                ShiftEnd: date.AddHours(9),
                Transport: TransportType.PublicTransit,
                Skills: [SkillType.LocalRepair, SkillType.Emergency],
                MaxOrdersPerShift: 8
            )
        };

        var orders = new List<WorkOrderInputDto>
        {
            new WorkOrderInputDto
            {
                ExternalId = "ORD-101",
                Depot = new DepotDto("Диспетчерская", "г. Москва, ул. Тверская, 7", 55.7576, 37.6111),
                Address = "г. Москва, ул. Арбат, 15",
                Latitude = 55.7505,
                Longitude = 37.5942,
                WindowStart = date.AddHours(1),
                WindowEnd = date.AddHours(3),
                RequiredSkill = SkillType.Installation,
                RequiredTransport = TransportType.Car,
                Priority = OrderPriority.High,
                TaskType = "Подключение клиента",
                DurationMinutes = 70
            },
            new WorkOrderInputDto
            {
                ExternalId = "ORD-102",
                Depot = new DepotDto("Диспетчерская", "г. Москва, ул. Тверская, 7", 55.7576, 37.6111),
                Address = "г. Москва, ул. Сретенка, 24",
                Latitude = 55.7698,
                Longitude = 37.6331,
                WindowStart = date.AddHours(2),
                WindowEnd = date.AddHours(4),
                RequiredSkill = SkillType.LocalRepair,
                RequiredTransport = null,
                Priority = OrderPriority.Normal,
                TaskType = "Ремонт оборудования",
                DurationMinutes = 30
            }
        };
        return Ok(new CreateOptimizationPlanRequest(date, engineers, orders));
    }
}