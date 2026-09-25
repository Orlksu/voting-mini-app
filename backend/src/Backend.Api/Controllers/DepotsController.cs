using Backend.Api.Infrastructure.Repositories;
using Backend.Contracts.Responses;
using Microsoft.AspNetCore.Mvc;

namespace Backend.Api.Controllers;

[ApiController]
[Route("api/v1/depots")]
public class DepotsController(IDepotRepository depotRepository) : ControllerBase
{
    /// <summary>
    /// Получение справочника всех доступных депо/офисов (для выбора диспетчером в выпадающем списке).
    /// </summary>
    [HttpGet]
    [ProducesResponseType<List<DepotDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetAll(CancellationToken ct)
    {
        var depots = await depotRepository.GetAllAsync(ct);
        var result = depots.Select(d => new DepotDto(
            d.District,
            d.Location?.RawAddress ?? string.Empty,
            d.Location?.Coordinates.Latitude ?? 0.0,
            d.Location?.Coordinates.Longitude ?? 0.0,
            d.Id
        )).ToList();

        return Ok(result);
    }
}
