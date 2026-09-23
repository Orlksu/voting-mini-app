using Backend.Api.Services.CsvParserService;
using Backend.Api.Services.EngineerService;
using Backend.Contracts.Responses;
using Microsoft.AspNetCore.Mvc;

namespace Backend.Api.Controllers;

[ApiController]
[Route("api/v1/engineers")]
public class EngineersController(IEngineerCsvParser csvParser, IEngineerService engineerService) : ControllerBase
{
    [HttpPost("upload-csv")]
    [Consumes("multipart/form-data")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> UploadEngineersCsv([FromForm] IFormFile? file, CancellationToken ct)
    {
        if (file is null || file.Length == 0)
            return BadRequest("Файл не получен. Отправьте multipart/form-data с полем 'file', содержащим CSV-файл.");

        await using var stream = file.OpenReadStream();
        var rows = await csvParser.ParseEngineersAsync(stream, ct);
        if (rows.Count == 0)
            return BadRequest("Не удалось распарсить строки инженеров из файла.");

        var result = await engineerService.ImportEngineersAsync(rows, ct);
        return Ok(result);
    }
}