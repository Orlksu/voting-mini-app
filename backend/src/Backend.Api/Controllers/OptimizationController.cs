using Backend.Api.Services;
using Backend.Contracts.Domain.Enums;
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
    private readonly ILogger<OptimizationController> _logger;

    public OptimizationController(
        IRouteOptimizer optimizer,
        OptimizationJobStore jobStore,
        ILogger<OptimizationController> logger)
    {
        _optimizer = optimizer;
        _jobStore = jobStore;
        _logger = logger;
    }

    /// <summary>
    /// Постановка задачи в очередь расчета
    /// </summary>
    [HttpPost("jobs")]
    [ProducesResponseType<JobStatusResponseDto>(StatusCodes.Status202Accepted)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public ActionResult<JobStatusResponseDto> CreateJob([FromBody] CreateOptimizationPlanRequest request)
    {
        if (request.Engineers.Count == 0 || request.Orders.Count == 0)
        {
            return BadRequest("Список инженеров и заявок не может быть пустым.");
        }

        _jobStore.SetLastEngineers(request.Engineers);
        var job = _jobStore.Create();
        _logger.LogInformation("Job {JobId}: оптимизация на {PlanDate} ({Engineers} инженеров, {Orders} заявок)",
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