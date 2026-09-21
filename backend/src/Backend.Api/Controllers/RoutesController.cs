using Backend.Contracts.Enums;
using Backend.Contracts.Interfaces;
using Backend.Contracts.Models;
using Backend.Contracts.Requests;
using Backend.Contracts.Responses;
using Microsoft.AspNetCore.Mvc;

namespace Backend.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class RoutesController : ControllerBase
{
    private readonly IRouteOptimizer _optimizer;
    private readonly ILogger<RoutesController> _logger;

    public RoutesController(IRouteOptimizer optimizer, ILogger<RoutesController> logger)
    {
        _optimizer = optimizer;
        _logger = logger;
    }

    /// <summary>
    /// Оптимизация маршрутов выездных инженеров
    /// </summary>
    [HttpPost("optimize")]
    [ProducesResponseType<OptimizeRoutesResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<OptimizeRoutesResponse>> Optimize(
        [FromBody] OptimizeRoutesRequest request,
        CancellationToken cancellationToken)
    {
        if (request.Engineers.Count == 0 && request.Orders.Count == 0)
        {
            return BadRequest("Список инженеров и заявок не может быть пустым.");
        }

        _logger.LogInformation("Запрос на оптимизацию: {Engineers} инженеров, {Orders} заявок",
            request.Engineers.Count, request.Orders.Count);

        var result = await _optimizer.OptimizeAsync(request, cancellationToken);
        return Ok(result);
    }

    /// <summary>
    /// Получить готовый пример входных данных для тестирования (Москва)
    /// </summary>
    [HttpGet("sample-request")]
    public ActionResult<OptimizeRoutesRequest> GetSampleRequest()
    {
        var sample = new OptimizeRoutesRequest(
            Date: DateOnly.FromDateTime(DateTime.Today),
            Engineers:
            [
                new EngineerDto(
                    Id: "eng-1",
                    Name: "Иван Смирнов (Связь / Оптика)",
                    Skills: ["FiberOptics", "GPON", "RouterSetup"],
                    Transport: TransportType.Car,
                    StartLocation: new LocationDto(55.7558, 37.6173, "Центральный хаб (Охотный ряд)"),
                    WorkHours: new TimeWindowDto(new TimeOnly(9, 0), new TimeOnly(18, 0))
                ),
                new EngineerDto(
                    Id: "eng-2",
                    Name: "Алексей Кузнецов (Монтаж / Медь)",
                    Skills: ["CopperCables", "SwitchSetup"],
                    Transport: TransportType.PublicTransport,
                    StartLocation: new LocationDto(55.7317, 37.6321, "Южный хаб (Павелецкая)"),
                    WorkHours: new TimeWindowDto(new TimeOnly(10, 0), new TimeOnly(19, 0))
                )
            ],
            Orders:
            [
                new OrderDto(
                    Id: "ord-101",
                    Title: "Подключение GPON в офисе",
                    Location: new LocationDto(55.7602, 37.6485, "ул. Покровка, 12"),
                    TimeWindow: new TimeWindowDto(new TimeOnly(9, 30), new TimeOnly(12, 0)),
                    DurationMinutes: 60,
                    RequiredSkills: ["GPON", "RouterSetup"],
                    Priority: OrderPriority.High
                ),
                new OrderDto(
                    Id: "ord-102",
                    Title: "Диагностика оптоволокна",
                    Location: new LocationDto(55.7712, 37.6015, "ул. Новослободская, 24"),
                    TimeWindow: new TimeWindowDto(new TimeOnly(11, 0), new TimeOnly(14, 0)),
                    DurationMinutes: 45,
                    RequiredSkills: ["FiberOptics"],
                    Priority: OrderPriority.Normal
                ),
                new OrderDto(
                    Id: "ord-103",
                    Title: "Замена коммутатора",
                    Location: new LocationDto(55.7420, 37.6530, "ул. Таганская, 5"),
                    TimeWindow: new TimeWindowDto(new TimeOnly(14, 0), new TimeOnly(17, 0)),
                    DurationMinutes: 90,
                    RequiredSkills: ["SwitchSetup"],
                    Priority: OrderPriority.Critical
                )
            ]
        );

        return Ok(sample);
    }
}
