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

    public OptimizationController(
        IRouteOptimizer optimizer,
        OptimizationJobStore jobStore,
        IEngineerRepository engineers,
        IWorkOrderRepository workOrders,
        IServiceScopeFactory scopeFactory,
        ILogger<OptimizationController> logger)
    {
        _optimizer = optimizer;
        _jobStore = jobStore;
        _engineers = engineers;
        _workOrders = workOrders;
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    /// <summary>
    /// Постановка задачи в очередь расчета на реальных данных из БД:
    /// все доступные инженеры и все нераспределённые заявки.
    /// </summary>
    [HttpPost("jobs")]
    [ProducesResponseType<JobStatusResponseDto>(StatusCodes.Status202Accepted)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<JobStatusResponseDto>> CreateJob(CancellationToken ct)
    {
        var engineers = (await _engineers.GetAllAsync(ct))
            .Where(e => e.IsAvailable)
            .Select(ToEngineerInputDto)
            .ToList();

        var orders = (await _workOrders.GetAllAsync(ct))
            .Where(o => o.Status == OrderStatus.NotDispatched)
            .Select(ToWorkOrderInputDto)
            .ToList();

        if (engineers.Count == 0)
            return BadRequest("Нет доступных инженеров для оптимизации (проверьте /api/v1/engineers).");
        if (orders.Count == 0)
            return BadRequest("Нет нераспределённых заявок (проверьте /api/v1/workorders).");

        var planDate = orders.Select(o => o.WindowStart).FirstOrDefault(w => w.HasValue) ?? DateTimeOffset.UtcNow;
        var alignedEngineers = engineers.Select(e =>
        {
            var shiftDuration = e.ShiftEnd - e.ShiftStart;
            if (shiftDuration <= TimeSpan.Zero)
            {
                shiftDuration += TimeSpan.FromDays(1);
            }

            var alignedStart = new DateTimeOffset(planDate.Year, planDate.Month, planDate.Day,
                e.ShiftStart.Hour, e.ShiftStart.Minute, e.ShiftStart.Second, e.ShiftStart.Offset);
            var alignedEnd = alignedStart + shiftDuration;
            if (alignedEnd <= alignedStart)
            {
                alignedEnd = alignedEnd.AddDays(1);
            }

            return e with { ShiftStart = alignedStart, ShiftEnd = alignedEnd };
        }).ToList();

        var request = new CreateOptimizationPlanRequest(planDate, alignedEngineers, orders);

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
    [HttpPost("replan")]
    [ProducesResponseType<JobStatusResponseDto>(StatusCodes.Status202Accepted)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<JobStatusResponseDto>> Replan(
        [FromQuery] Guid? depotId = null,
        [FromQuery] string? district = null,
        CancellationToken ct = default)
    {
        var engQuery = (await _engineers.GetAllAsync(ct)).Where(e => e.IsAvailable);
        if (depotId.HasValue)
        {
            engQuery = engQuery.Where(e => e.DepotId == depotId.Value);
        }
        if (!string.IsNullOrWhiteSpace(district))
        {
            engQuery = engQuery.Where(e => string.Equals(e.District, district.Trim(), StringComparison.OrdinalIgnoreCase));
        }

        var engineers = engQuery.Select(ToEngineerInputDto).ToList();
        if (engineers.Count == 0)
            return BadRequest("Нет доступных инженеров для перепланирования.");

        var woQuery = (await _workOrders.GetAllAsync(ct))
            .Where(o => o.Status is OrderStatus.NotDispatched or OrderStatus.Dispatched);

        if (depotId.HasValue)
        {
            woQuery = woQuery.Where(o => o.DepotId == depotId.Value);
        }
        if (!string.IsNullOrWhiteSpace(district))
        {
            woQuery = woQuery.Where(o => string.Equals(o.District, district.Trim(), StringComparison.OrdinalIgnoreCase));
        }

        var orders = woQuery.Select(ToWorkOrderInputDto).ToList();
        if (orders.Count == 0)
            return BadRequest("Нет незавершенных заявок для перепланирования.");

        var planDate = orders.Select(o => o.WindowStart).FirstOrDefault(w => w.HasValue) ?? DateTimeOffset.UtcNow;
        var alignedEngineers = engineers.Select(e =>
        {
            var shiftDuration = e.ShiftEnd - e.ShiftStart;
            if (shiftDuration <= TimeSpan.Zero)
            {
                shiftDuration += TimeSpan.FromDays(1);
            }

            var alignedStart = new DateTimeOffset(planDate.Year, planDate.Month, planDate.Day,
                e.ShiftStart.Hour, e.ShiftStart.Minute, e.ShiftStart.Second, e.ShiftStart.Offset);
            var alignedEnd = alignedStart + shiftDuration;
            if (alignedEnd <= alignedStart)
            {
                alignedEnd = alignedEnd.AddDays(1);
            }

            return e with { ShiftStart = alignedStart, ShiftEnd = alignedEnd };
        }).ToList();

        var request = new CreateOptimizationPlanRequest(planDate, alignedEngineers, orders);

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
    public ActionResult<JobStatusResponseDto> GetJobStatus(Guid jobId)
    {
        var job = _jobStore.GetStatus(jobId);
        return job == null ? NotFound() : Ok(job);
    }

    /// <summary>
    /// Получение рассчитанного плана
    /// </summary>
    [HttpGet("jobs/{jobId:guid}/result")]
    [ProducesResponseType<OptimizationResultDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public ActionResult<OptimizationResultDto> GetJobResult(Guid jobId)
    {
        if (!_jobStore.TryGetResult(jobId, out var result))
        {
            return NotFound();
        }

        return result == null ? NoContent() : Ok(result);
    }

    /// <summary>
    /// Расчет оперативного перепланирования
    /// </summary>
    [HttpPost("reschedule")]
    [ProducesResponseType<JobStatusResponseDto>(StatusCodes.Status202Accepted)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public ActionResult<JobStatusResponseDto> Reschedule([FromBody] ReschedulePlanRequest request)
    {
        var planRequest = BuildPlanRequest(request);
        if (planRequest == null)
        {
            return BadRequest("Перепланирование требует ранее рассчитанного плана (справочник инженеров).");
        }

        var job = _jobStore.Create();
        _logger.LogInformation("Job {JobId}: перепланирование ({EventType}) на {EventTime}",
            job.JobId, request.EventType, request.EventTime);

        RunJob(job.JobId, planRequest);

        return AcceptedAtAction(nameof(GetJobStatus), new { jobId = job.JobId }, job);
    }

    private void RunJob(Guid jobId, CreateOptimizationPlanRequest request)
    {
        _ = Task.Run(async () =>
        {
            _jobStore.MoveToProcessing(jobId);
            try
            {
                var result = await _optimizer.OptimizeAsync(request, CancellationToken.None);
                _jobStore.Complete(jobId, result);

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

                    var operationResult = new OperationResult
                    {
                        Id = result.PlanId,
                        JobId = jobId,
                        PlanDate = result.PlanDate.ToUniversalTime(),
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
                                BaselineDistanceKm = r.BaselineDistanceKm,
                                EncodedPolyline = r.EncodedPolyline,
                                Stops = r.Stops.Select(s => new RouteStop
                                {
                                    Id = Guid.NewGuid(),
                                    EngineerRouteId = routeId,
                                    Sequence = s.Sequence,
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

                    var assignedExternalIds = result.Routes
                        .SelectMany(r => r.Stops)
                        .Select(s => s.ExternalOrderId)
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .ToList();

                    if (assignedExternalIds.Count > 0)
                    {
                        var ordersToUpdate = await woRepo.GetByExternalIdsAsync(assignedExternalIds, CancellationToken.None);
                        await woRepo.UpdateStatusesAsync(ordersToUpdate.Select(o => o.Id), OrderStatus.Dispatched, CancellationToken.None);
                    }

                    var unassignedExternalIds = result.UnassignedOrders
                        .Select(u => u.ExternalOrderId)
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .ToList();
                    if (unassignedExternalIds.Count > 0)
                    {
                        var unassignedOrders = await woRepo.GetByExternalIdsAsync(unassignedExternalIds, CancellationToken.None);
                        await woRepo.UpdateStatusesAsync(unassignedOrders.Select(o => o.Id), OrderStatus.NotDispatched, CancellationToken.None);
                    }

                    _logger.LogInformation("Job {JobId}: маршруты успешно сохранены в БД ({RoutesCount} маршрутов, {OrdersCount} назначенных заявок обновлены на Dispatched, {UnassignedCount} неназначенных заявок возвращены в NotDispatched)",
                        jobId, operationResult.Routes.Count, assignedExternalIds.Count, unassignedExternalIds.Count);
                }
                catch (Exception persistEx)
                {
                    _logger.LogError(persistEx, "Job {JobId}: ошибка сохранения рассчитанного плана в БД", jobId);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Job {JobId}: ошибка расчёта", jobId);
                _jobStore.Fail(jobId, ex.Message);
            }
        });
    }

    private CreateOptimizationPlanRequest? BuildPlanRequest(ReschedulePlanRequest request)
    {
        if (_jobStore.LastEngineers is not { Count: > 0 } engineers)
        {
            return null;
        }

        // Исключаем недоступного инженера и обновляем live-состояние остальных
        var statesByEngineer = request.EngineersStates.ToDictionary(s => s.EngineerExternalId);
        var updatedEngineers = engineers
            .Where(e => e.ExternalId != request.UnavailableEngineerExternalId)
            .Select(e => statesByEngineer.TryGetValue(e.ExternalId, out var state)
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
            .Where(o => o.ExternalId != request.CancelledOrderExternalId)
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
        e.ExternalId,
        e.Name,
        e.StartLocation.RawAddress,
        e.StartLocation.Coordinates.Latitude,
        e.StartLocation.Coordinates.Longitude,
        e.Shift.Start,
        e.Shift.End,
        e.Transport,
        [.. e.Skills],
        e.MaxOrdersPerShift,
        e.District,
        e.DepotId);

    private static WorkOrderInputDto ToWorkOrderInputDto(WorkOrder o) => new()
    {
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
    };
}