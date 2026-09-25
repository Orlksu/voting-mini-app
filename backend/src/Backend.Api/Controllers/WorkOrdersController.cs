using Backend.Api.Services;
using Backend.Api.Services.WorkOrderService;
using Backend.Contracts.Responses;
using Microsoft.AspNetCore.Mvc;

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

    [HttpGet("by-engineer/{identifier}")]
    [ProducesResponseType<List<EngineerOrderDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetByEngineer(string identifier, CancellationToken ct)
    {
        var orders = await workOrderService.GetByEngineerAsync(identifier, ct);
        if (orders is null)
            return NotFound($"Инженер '{identifier}' не найден.");

        return Ok(orders);
    }

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
        var payloads = new List<(string FileName, byte[] Content)>();
        foreach (var file in files)
        {
            if (file.Length == 0) continue;
            using var ms = new MemoryStream();
            await file.CopyToAsync(ms, ct);
            payloads.Add((file.FileName, ms.ToArray()));
        }

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