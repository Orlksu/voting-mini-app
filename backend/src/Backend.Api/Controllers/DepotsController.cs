using Backend.Api.Services.DepotService;
using Backend.Contracts.Responses;
using Microsoft.AspNetCore.Mvc;

namespace Backend.Api.Controllers;

[ApiController]
[Route("api/v1/depots")]
public class DepotsController(IDepotService depotService) : ControllerBase
{
    /// <summary>
    /// Получение справочника всех доступных депо/офисов (для выбора диспетчером в выпадающем списке).
    /// </summary>
    [HttpGet]
    [ProducesResponseType<List<DepotDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetAll(CancellationToken ct)
        => Ok(await depotService.GetAllAsync(ct));
}
