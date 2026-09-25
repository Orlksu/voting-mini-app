using Backend.Api.Services.WorkOrderService;
using Backend.Contracts.Responses;
using Microsoft.AspNetCore.Mvc;

namespace Backend.Api.Controllers;

[ApiController]
[Route("api/v1/workorders")]
public class WorkOrdersController(IWorkOrderService workOrderService) : ControllerBase
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
    [ProducesResponseType<OrderImportResultDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> UploadOrdersCsv([FromForm] List<IFormFile>? files, CancellationToken ct)
    {
        if (files == null || files.Count == 0)
            return BadRequest("Файл не получен. Отправьте multipart/form-data с полем 'files', содержащим CSV-файл(ы).");

        var result = await workOrderService.ImportOrdersCsvAsync(files, ct);
        return Ok(result);
    }
}