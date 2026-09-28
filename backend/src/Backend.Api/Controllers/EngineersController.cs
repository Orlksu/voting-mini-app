using Backend.Api.Services;
using Backend.Api.Services.CsvParserService;
using Backend.Api.Services.EngineerService;
using Backend.Api.Services.WorkOrderService;
using Backend.Contracts.Responses;
using Microsoft.AspNetCore.Mvc;

namespace Backend.Api.Controllers;

[ApiController]
[Route("api/v1/engineers")]
public class EngineersController(
    IEngineerService engineerService,
    EngineerImportJobStore jobStore,
    IServiceScopeFactory scopeFactory,
    ILogger<EngineersController> logger) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<List<EngineerDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetAll(CancellationToken ct)
        => Ok(await engineerService.GetAllAsync(ct));

    /// <summary>
    /// Загрузка инженеров из CSV-файла.
    /// Поддерживает два режима:
    /// - false (по умолчанию): асинхронная фоновая обработка (202 Accepted + заголовок Location для поллинга статуса).
    /// - true: блокирующий синхронный режим (200 OK, рекомендуется для интеграционных тестов или небольших файлов).
    /// </summary>
    [HttpPost("upload-csv")]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(10 * 1024 * 1024)]
    [ProducesResponseType<EngineerImportJobStatusDto>(StatusCodes.Status202Accepted)]
    [ProducesResponseType<EngineerImportResultDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> UploadEngineersCsv(
        [FromForm] IFormFile? file,
        [FromQuery] bool sync = false,
        CancellationToken ct = default)
    {
        if (file is null || file.Length == 0)
            return BadRequest("Файл не получен. Отправьте multipart/form-data с полем 'file', содержащим CSV-файл.");

        var ext = Path.GetExtension(file.FileName);
        if (!string.Equals(ext, ".csv", StringComparison.OrdinalIgnoreCase))
            return BadRequest("Неверный формат файла. Поддерживаются только файлы .csv");

        byte[] content;
        await using (var ms = new MemoryStream())
        {
            await file.CopyToAsync(ms, ct);
            content = ms.ToArray();
        }

        if (content.Length == 0)
            return BadRequest("Загруженный файл пуст.");

        if (sync)
        {
            var result = await engineerService.ImportEngineersFromPayloadAsync(content, file.FileName, null, ct);
            return Ok(result);
        }

        var job = jobStore.Create();

        _ = Task.Run(async () =>
        {
            using var scope = scopeFactory.CreateScope();
            var scopedService = scope.ServiceProvider.GetRequiredService<IEngineerService>();
            var progress = new Progress<(int Processed, int Total, string Stage)>(p =>
            {
                jobStore.UpdateProgress(job.JobId, p.Processed, p.Total, p.Stage);
            });

            try
            {
                var result = await scopedService.ImportEngineersFromPayloadAsync(content, file.FileName, progress, CancellationToken.None);
                jobStore.Complete(job.JobId, result);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Ошибка выполнения фонового импорта инженеров для задачи {JobId}", job.JobId);
                jobStore.Fail(job.JobId, ex.Message);
            }
        });

        return AcceptedAtAction(nameof(GetImportJobStatus), new { jobId = job.JobId }, job);
    }

    /// <summary>
    /// Проверка статуса фонового импорта инженеров (polling)
    /// </summary>
    [HttpGet("import-jobs/{jobId:guid}")]
    [ProducesResponseType<EngineerImportJobStatusDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public ActionResult<EngineerImportJobStatusDto> GetImportJobStatus(Guid jobId)
    {
        var status = jobStore.GetStatus(jobId);
        return status is null ? NotFound("Задача импорта не найдена.") : Ok(status);
    }

    [HttpDelete("{id}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Delete(string id, CancellationToken ct)
    {
        var deleted = await engineerService.DeleteEngineerByIdentifierAsync(id, ct);
        return deleted ? NoContent() : NotFound("Инженер не найден.");
    }

    [HttpGet("{id}")]
    [ProducesResponseType<EngineerDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetEngineerById(string id, CancellationToken ct)
    {
        var engineer = await engineerService.GetEngineerByIdentifierAsync(id, ct);
        return engineer is null ? NotFound("Инженер не найден.") : Ok(engineer);
    }

    /// <summary>
    /// Получение рассчитанного маршрута конкретного инженера с полилинией (EncodedPolyline).
    /// Опционально фильтрует по целевой дате плана (date).
    /// </summary>
    /// <param name="id">Идентификатор инженера (Guid или ExternalId).</param>
    /// <param name="date">Опциональная дата плана для получения маршрута на конкретный день.</param>
    /// <param name="workOrderService">Сервис работы с заявками и маршрутами.</param>
    /// <param name="ct">Токен отмены запроса.</param>
    [HttpGet("{id}/route")]
    [ProducesResponseType<EngineerRouteDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetEngineerRoute(
        string id, 
        [FromQuery] DateTimeOffset? date = null, 
        [FromServices] IWorkOrderService workOrderService = null!, 
        CancellationToken ct = default)
    {
        var route = await workOrderService.GetRouteByEngineerAsync(id, date, ct);
        return route is null ? NotFound($"Маршрут для инженера '{id}' не найден.") : Ok(route);
    }
}
