using Backend.Api.Services;
using Backend.Api.Services.Common;
using Backend.Api.Services.WorkOrderService;
using Backend.Contracts.Requests;
using Backend.Contracts.Responses;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Backend.Api.Controllers;

[ApiController]
[Route("api/v1/workorders")]
public class WorkOrdersController(
    IWorkOrderService workOrderService,
    OrderImportJobStore jobStore,
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
            return CreatedAtAction(nameof(GetByIdentifier), new { identifier = created.Id }, created);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(ex.Message);
        }
        catch (InvalidOperationException ex)
        {
            return Conflict(ex.Message);
        }
        catch (DbUpdateException ex)
        {
            logger.LogWarning(ex, "Конфликт уникальности при сохранении заявки {ExternalId} в районе {District}",
                request.ExternalId, request.District);
            return Conflict("Заявка с таким идентификатором в указанном районе уже существует.");
        }
    }

    /// <summary>
    /// Получение информации о конкретной заявке по Id (Guid) или внешнему номеру (ExternalId).
    /// </summary>
    /// <param name="identifier">Внутренний Guid заявки или строковый ExternalId.</param>
    /// <param name="ct">Токен отмены запроса.</param>
    /// <response code="200">Информация о заявке найдена.</response>
    /// <response code="404">Заявка не найдена.</response>
    [HttpGet("{identifier}")]
    [ProducesResponseType<WorkOrderDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetByIdentifier(string identifier, CancellationToken ct)
    {
        var order = await workOrderService.GetByIdentifierAsync(identifier, ct);
        return order is null ? NotFound("Заявка не найдена.") : Ok(order);
    }

    /// <summary>
    /// Удаление заявки по Id (Guid) или внешнему номеру (ExternalId).
    /// Разрешено удалять только нераспределенные заявки (NotDispatched).
    /// </summary>
    /// <param name="identifier">Внутренний Guid заявки или строковый ExternalId.</param>
    /// <param name="ct">Токен отмены запроса.</param>
    /// <response code="204">Заявка успешно удалена.</response>
    /// <response code="400">Нельзя удалить распределенную заявку или найдено несколько заявок с таким ExternalId.</response>
    /// <response code="404">Заявка не найдена.</response>
    [HttpDelete("{identifier}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Delete(string identifier, CancellationToken ct)
    {
        try
        {
            var deleted = await workOrderService.DeleteByIdentifierAsync(identifier, ct);
            return deleted ? NoContent() : NotFound("Заявка не найдена.");
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