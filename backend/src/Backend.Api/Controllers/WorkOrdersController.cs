using Backend.Api.Services;
using Backend.Api.Services.Common;
using Backend.Api.Services.WorkOrderService;
using Backend.Contracts.Domain.Enums;
using Backend.Contracts.Interfaces;
using Backend.Contracts.Requests;
using Backend.Contracts.Responses;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace Backend.Api.Controllers;

[ApiController]
[Route("api/v1/workorders")]
public class WorkOrdersController(
    IWorkOrderService workOrderService,
    OrderImportJobStore jobStore,
    OptimizationJobStore optimizationJobStore,
    LivePlanStateBuilder liveState,
    IServiceScopeFactory scopeFactory,
    ILogger<WorkOrdersController> logger) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<List<WorkOrderDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetAll(CancellationToken ct)
        => Ok(await workOrderService.GetAllAsync(ct));

    /// <summary>
    /// Создание новой заявки на обслуживание вручную через JSON с автоматическим геокодированием и привязкой к офису/депо.
    /// </summary>
    /// <param name="request">Параметры создаваемой заявки.</param>
    /// <param name="ct">Токен отмены запроса.</param>
    /// <response code="201">Заявка успешно создана.</response>
    /// <response code="400">Ошибка валидации входных данных или невозможность определить координаты.</response>
    /// <response code="409">Конфликт: заявка с таким идентификатором в указанном районе уже существует.</response>
    [HttpPost]
    [ProducesResponseType<WorkOrderDto>(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Create([FromBody] CreateWorkOrderRequest request, CancellationToken ct)
    {
        try
        {
            var created = await workOrderService.CreateAsync(request, ct);
            return CreatedAtAction(nameof(GetByIdentifier), new { id = created.Id }, created);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(ex.Message);
        }
        catch (InvalidOperationException ex)
        {
            return Conflict(ex.Message);
        }
    }

    /// <summary>
    /// Получение информации о конкретной заявке по Id (Guid) или внешнему номеру (ExternalId).
    /// </summary>
    /// <param name="id">Внутренний Guid заявки или строковый ExternalId.</param>
    /// <param name="district">Опциональный район (District) для однозначной идентификации при поиске по ExternalId.</param>
    /// <param name="ct">Токен отмены запроса.</param>
    /// <response code="200">Информация о заявке найдена.</response>
    /// <response code="400">Найдено несколько заявок с таким ExternalId в разных районах (требуется указать district).</response>
    /// <response code="404">Заявка не найдена.</response>
    [HttpGet("{id}")]
    [ProducesResponseType<WorkOrderDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetByIdentifier(string id, [FromQuery] string? district, CancellationToken ct)
    {
        try
        {
            var order = await workOrderService.GetByIdentifierAsync(id, district, ct);
            return order is null ? NotFound("Заявка не найдена.") : Ok(order);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ex.Message);
        }
    }

    /// <summary>
    /// Удаление заявки по Id (Guid) или внешнему номеру (ExternalId).
    /// Разрешено удалять только нераспределенные заявки (NotDispatched).
    /// </summary>
    /// <param name="id">Внутренний Guid заявки или строковый ExternalId.</param>
    /// <param name="district">Опциональный район (District) для точной идентификации при удалении по ExternalId.</param>
    /// <param name="ct">Токен отмены запроса.</param>
    /// <response code="204">Заявка успешно удалена.</response>
    /// <response code="400">Нельзя удалить распределенную заявку или найдено несколько заявок с таким ExternalId.</response>
    /// <response code="404">Заявка не найдена.</response>
    [HttpDelete("{id}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Delete(string id, [FromQuery] string? district, CancellationToken ct)
    {
        try
        {
            var deleted = await workOrderService.DeleteByIdentifierAsync(id, district, ct);
            return deleted ? NoContent() : NotFound("Заявка не найдена.");
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ex.Message);
        }
    }

    /// <summary>
    /// Отмена заявки на обслуживание с указанием причины отмены.
    /// Завершенные (Completed) заявки отменить нельзя (400 Bad Request).
    /// Если заявка была распределена (Dispatched), автоматически запускается перепланирование:
    /// высвободившееся время инженера может быть заполнено заявкой из очереди NotDispatched.
    /// </summary>
    /// <param name="id">Уникальный идентификатор заявки (Guid или уникальный ExternalId).</param>
    /// <param name="district">Опциональный район (District) для однозначной идентификации при отмене по ExternalId.</param>
    /// <param name="request">Тело запроса с опциональной причиной отмены.</param>
    /// <param name="ct">Токен отмены запроса.</param>
    /// <response code="200">Заявка успешно отменена. Поле replanJobId содержит ID задачи перепланирования (если была запущена).</response>
    /// <response code="400">Ошибка валидации причины или попытка отменить завершенную заявку.</response>
    /// <response code="404">Заявка не найдена.</response>
    [HttpPost("{id}/cancel")]
    [ProducesResponseType<WorkOrderCancelResultDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Cancel(
        string id, 
        [FromQuery] string? district = null,
        [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] CancelWorkOrderRequest? request = null, 
        CancellationToken ct = default)
    {
        try
        {
            // Получаем текущий статус до отмены, чтобы решить нужно ли перепланирование
            var orderBefore = await workOrderService.GetByIdentifierAsync(id, district, ct);
            bool wasDispatched = orderBefore?.Status == OrderStatus.Dispatched;

            var result = await workOrderService.CancelAsync(id, district, request?.Reason, ct);

            Guid? replanJobId = null;

            // Автоматическое перепланирование: заявка была в маршруте инженера
            if (wasDispatched && optimizationJobStore.LastResult != null)
            {
                try
                {
                    var now = DateTimeOffset.UtcNow;
                    var liveResult = await liveState.BuildAsync(now, ct);
                    var contexts = liveResult.Contexts;
                    var orphaned = liveResult.OrphanedOrderIds;
                    
                    var engineers = contexts.Count > 0
                        ? contexts.Select(c => c.Engineer).ToList()
                        : optimizationJobStore.LastEngineers ?? [];

                    if (engineers.Count > 0)
                    {
                        // Собираем оставшиеся заявки, исключая отменённую
                        var remainingOrders = await liveState.BuildRemainingOrdersAsync(
                            contexts,
                            orphaned,
                            targetDate: null,
                            excludeExternalId: result.ExternalId,
                            ct);

                        if (remainingOrders.Count > 0)
                        {
                            var planRequest = new CreateOptimizationPlanRequest(now, engineers, remainingOrders);
                            var job = optimizationJobStore.Create();
                            replanJobId = job.JobId;

                            logger.LogInformation(
                                "Job {JobId}: перепланирование после отмены заявки {OrderId} (была Dispatched)",
                                job.JobId, result.ExternalId);

                            // Запускаем перепланирование в фоне через scoped-зависимости
                            _ = Task.Run(async () =>
                            {
                                optimizationJobStore.MoveToProcessing(job.JobId);
                                try
                                {
                                    using var scope = scopeFactory.CreateScope();
                                    var scopedOptimizer = scope.ServiceProvider.GetRequiredService<IRouteOptimizer>();
                                    var replanResult = await scopedOptimizer.OptimizeAsync(planRequest, CancellationToken.None);
                                    optimizationJobStore.Complete(job.JobId, replanResult);

                                    logger.LogInformation(
                                        "Job {JobId}: перепланирование после отмены завершено ({Routes} маршрутов)",
                                        job.JobId, replanResult.Routes.Count);
                                }
                                catch (Exception ex)
                                {
                                    logger.LogError(ex, "Job {JobId}: ошибка перепланирования после отмены", job.JobId);
                                    optimizationJobStore.Fail(job.JobId, ex.Message);
                                }
                            });
                        }
                    }
                }
                catch (Exception ex)
                {
                    // Не даём ошибке перепланирования прервать успешную отмену заявки
                    logger.LogWarning(ex, "Не удалось запустить автоперепланирование после отмены заявки {OrderId}", id);
                }
            }

            return Ok(new WorkOrderCancelResultDto(result, replanJobId));
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(ex.Message);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(ex.Message);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ex.Message);
        }
    }

    /// <summary>
    /// Завершение выполнения заявки на обслуживание.
    /// Отмененные (Cancelled) заявки завершить нельзя (400 Bad Request).
    /// </summary>
    /// <param name="id">Уникальный идентификатор заявки (Guid или уникальный ExternalId).</param>
    /// <param name="district">Опциональный район (District) для однозначной идентификации при завершении по ExternalId.</param>
    /// <param name="ct">Токен отмены запроса.</param>
    /// <response code="200">Заявка успешно переведена в статус завершенной.</response>
    /// <response code="400">Попытка завершить отмененную заявку или неоднозначный идентификатор.</response>
    /// <response code="404">Заявка не найдена.</response>
    [HttpPost("{id}/complete")]
    [ProducesResponseType<WorkOrderDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Complete(
        string id, 
        [FromQuery] string? district = null, 
        CancellationToken ct = default)
    {
        try
        {
            var result = await workOrderService.CompleteAsync(id, district, ct);
            return Ok(result);
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(ex.Message);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(ex.Message);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ex.Message);
        }
    }

    /// <summary>
    /// Загрузка CSV-файлов с заявками на обслуживание и геокодирование адресов.
    /// </summary>
    /// <param name="files">Список CSV-файлов в формате multipart/form-data (до 20 МБ).</param>
    /// <param name="sync">
    /// Режим выполнения импорта:
    /// - false (по умолчанию): асинхронная фоновая обработка (202 Accepted + заголовок Location для поллинга статуса).
    /// - true: блокирующий синхронный режим (200 OK, рекомендуется для интеграционных тестов или небольших файлов).
    /// </param>
    /// <param name="ct">Токен отмены HTTP-запроса.</param>
    /// <response code="202">Фоновая задача успешно создана. Статус отслеживается через эндпоинт import-jobs/{jobId}.</response>
    /// <response code="200">Синхронный импорт успешно завершён (при sync=true).</response>
    /// <response code="400">Файлы не переданы или имеют некорректный формат/пусты.</response>
    [HttpPost("upload-csv")]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(20 * 1024 * 1024)]
    [ProducesResponseType<OrderImportJobStatusDto>(StatusCodes.Status202Accepted)]
    [ProducesResponseType<OrderImportResultDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> UploadOrdersCsv(
        [FromForm] List<IFormFile>? files,
        [FromQuery] bool sync = false,
        CancellationToken ct = default)
    {
        if (files == null || files.Count == 0)
            return BadRequest("Файл не получен. Отправьте multipart/form-data с полем 'files', содержащим CSV-файл(ы).");

        // Вычитываем байты файлов до завершения HTTP-запроса, чтобы избежать ObjectDisposedException
        var payloads = await FormFileHelper.ReadPayloadsAsync(files, ct);
        if (payloads.Count == 0)
            return BadRequest("Загруженные файлы пусты.");

        // Синхронный режим по явному запросу (?sync=true) для тестов или маленьких файлов
        if (sync)
        {
            var result = await workOrderService.ImportOrdersFromPayloadsAsync(payloads, null, ct);
            return Ok(result);
        }

        // Асинхронный фоновый режим по умолчанию (202 Accepted + Polling)
        var job = jobStore.Create();

        _ = Task.Run(async () =>
        {
            using var scope = scopeFactory.CreateScope();
            var scopedService = scope.ServiceProvider.GetRequiredService<IWorkOrderService>();
            var progress = new Progress<(int Processed, int Total, string Stage)>(p =>
            {
                jobStore.UpdateProgress(job.JobId, p.Processed, p.Total, p.Stage);
            });

            try
            {
                var result = await scopedService.ImportOrdersFromPayloadsAsync(payloads, progress, CancellationToken.None);
                jobStore.Complete(job.JobId, result);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Ошибка выполнения фонового импорта заказов для задачи {JobId}", job.JobId);
                jobStore.Fail(job.JobId, ex.Message);
            }
        });

        return AcceptedAtAction(nameof(GetImportJobStatus), new { jobId = job.JobId }, job);
    }

    /// <summary>
    /// Проверка статуса фонового импорта заказов (polling)
    /// </summary>
    [HttpGet("import-jobs/{jobId:guid}")]
    [ProducesResponseType<OrderImportJobStatusDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public ActionResult<OrderImportJobStatusDto> GetImportJobStatus(Guid jobId)
    {
        var status = jobStore.GetStatus(jobId);
        return status is null ? NotFound("Задача импорта не найдена.") : Ok(status);
    }
}