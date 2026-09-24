using Backend.Api.Services.CsvParserService;
using Backend.Api.Services.GeoCodingService;
using Backend.Api.Services.WorkOrderService;
using Backend.Contracts.Requests;
using Backend.Contracts.Responses;
using Microsoft.AspNetCore.Mvc;

namespace Backend.Api.Controllers;

[ApiController]
[Route("api/v1/workorders")]
public class WorkOrdersController(ICsvParserService csvParserService, IGeocoder geocoder, IWorkOrderService workOrderService) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<List<WorkOrderDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetAll(CancellationToken ct)
        => Ok(await workOrderService.GetAllAsync(ct));

    [HttpGet("by-engineer/{engineerId:guid}")]
    [ProducesResponseType<List<EngineerOrderDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetByEngineer(Guid engineerId, CancellationToken ct)
    {
        var orders = await workOrderService.GetByEngineerAsync(engineerId, ct);
        if (orders is null)
            return NotFound($"Инженер с id {engineerId} не найден.");

        return Ok(orders);
    }

    [HttpPost("upload-csv")]
    [Consumes("multipart/form-data")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> UploadOrdersCsv([FromForm] List<IFormFile>? files, CancellationToken ct)
    {
        if (files == null || files.Count == 0)
            return BadRequest("Файл не получен. Отправьте multipart/form-data с полем 'files', содержащим CSV-файл(ы).");

        var totalUnresolvedCount = 0;
        var processedFilesCount = 0;

        foreach (var file in files)
        {
            if (file.Length == 0)
                continue;

            await using var stream = file.OpenReadStream();
            var parseData = await csvParserService.ParseOrderAsync(stream, ct);

            if (parseData.Orders.Count == 0)
                continue;

            DepotDto? finalDepot = null;
            if (parseData.RawDepot != null && !string.IsNullOrWhiteSpace(parseData.RawDepot.Address))
            {
                var geoOffice = await geocoder.GeocodeAsync(parseData.RawDepot.Address, null, ct);
                if (geoOffice != null)
                {
                    var district = parseData.Orders[0].District ?? "Неизвестный район";
                    finalDepot = new DepotDto(district, parseData.RawDepot.Address, geoOffice.Latitude, geoOffice.Longitude);
                }
            }

            if (finalDepot == null)
            {
                return BadRequest($"Не удалось определить координаты офиса для файла {file.FileName}.");
            }

            var validOrders = new List<WorkOrderInputDto>();

            foreach (var order in parseData.Orders)
            {
                if ((order.Latitude == null || order.Longitude == null) && !string.IsNullOrWhiteSpace(order.Address))
                {
                    var geo = await geocoder.GeocodeAsync(order.Address, order.District, ct);
                    if (geo != null)
                    {
                        order.Latitude = geo.Latitude;
                        order.Longitude = geo.Longitude;
                        validOrders.Add(order);
                    }
                    else
                    {
                        totalUnresolvedCount++;
                    }
                }
                else if (order.Latitude != null && order.Longitude != null)
                {
                    validOrders.Add(order);
                }
                else
                {
                    totalUnresolvedCount++;
                }
            }

            await workOrderService.AddFromImportAsync(finalDepot, validOrders, ct);
            processedFilesCount++;
        }

        return Ok(new
        {
            ProcessedFiles = processedFilesCount,
            UnresolvedAddressesCount = totalUnresolvedCount
        });
    }
}