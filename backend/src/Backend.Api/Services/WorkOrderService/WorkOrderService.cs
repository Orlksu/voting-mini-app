using Backend.Api.Infrastructure.Repositories;
using Backend.Contracts.Domain.Entities;
using Backend.Contracts.Domain.Enums;
using Backend.Contracts.Domain.ValueObjects;
using Backend.Contracts.Requests;
using Backend.Contracts.Responses;
using Microsoft.Extensions.Logging;

namespace Backend.Api.Services.WorkOrderService;

public class WorkOrderService(IDepotRepository depots, IWorkOrderRepository workOrders, IEngineerRepository engineers, IEngineerRouteRepository routes, ILogger<WorkOrderService> logger) : IWorkOrderService
{
    public async Task<List<EngineerOrderDto>?> GetByEngineerAsync(Guid engineerId, CancellationToken ct = default)
    {
        var engineer = await engineers.GetByIdAsync(engineerId, ct);
        if (engineer is null)
            return null;

        var engineerRoutes = await routes.GetByEngineerAsync(engineerId, ct);
        if (engineerRoutes.Count == 0)
            return [];

        var externalOrderIds = engineerRoutes
            .SelectMany(r => r.Stops)
            .Select(s => s.ExternalOrderId)
            .Distinct();
        var ordersByExternalId = (await workOrders.GetByExternalIdsAsync(externalOrderIds, ct))
            .GroupBy(w => w.ExternalId)
            .ToDictionary(g => g.Key, g => g.First());

        var result = new List<EngineerOrderDto>();
        foreach (var route in engineerRoutes.OrderBy(r => r.Id))
        {
            foreach (var stop in route.Stops.OrderBy(s => s.Sequence))
            {
                if (!ordersByExternalId.TryGetValue(stop.ExternalOrderId, out var order))
                {
                    logger.LogWarning(
                        "Заявка {ExternalOrderId} из маршрута {RouteId} инженера {EngineerId} не найдена в БД.",
                        stop.ExternalOrderId, route.Id, engineerId);
                    continue;
                }

                result.Add(new EngineerOrderDto(
                    route.Id,
                    stop.Sequence,
                    ToDto(order),
                    stop.EstimatedArrival,
                    stop.ServiceStart,
                    stop.ServiceEnd,
                    stop.AssignmentReason));
            }
        }

        return result;
    }

    public async Task<List<WorkOrderDto>> GetAllAsync(CancellationToken ct = default)
    {
        var orders = await workOrders.GetAllAsync(ct);
        return orders.Select(ToDto).ToList();
    }

    private static WorkOrderDto ToDto(WorkOrder o) => new(
        o.Id,
        o.ExternalId,
        o.District,
        o.Location?.RawAddress ?? string.Empty,
        o.Location?.Coordinates.Latitude ?? 0.0,
        o.Location?.Coordinates.Longitude ?? 0.0,
        o.PreferredWindow.Start,
        o.PreferredWindow.End,
        (int)o.ServiceDuration.TotalMinutes,
        o.RequiredSkill,
        o.RequiredTransport,
        o.Priority,
        o.Status,
        o.TaskType,
        o.DepotId);

    public async Task<int> AddFromImportAsync(DepotDto depotDto, List<WorkOrderInputDto> orders, CancellationToken ct = default)
    {
        var district = orders
            .Select(o => o.District)
            .FirstOrDefault(d => !string.IsNullOrWhiteSpace(d))
            ?? depotDto.Name;

        var depot = await depots.GetOrCreateAsync(district, depotDto.Address, depotDto.Latitude, depotDto.Longitude, ct);

        var depotLatitude = depot.Location?.Coordinates.Latitude;
        var depotLongitude = depot.Location?.Coordinates.Longitude;

        var entities = new List<WorkOrder>();
        foreach (var o in orders)
        {
            var latitude = o.Latitude ?? depotLatitude;
            var longitude = o.Longitude ?? depotLongitude;
            if (latitude is null || longitude is null)
            {
                logger.LogWarning(
                    "Заявка {ExternalId} пропущена: нет координат, а у депо {DepotId} ({District}) отсутствует локация.",
                    o.ExternalId, depot.Id, district);
                continue;
            }

            entities.Add(new WorkOrder
            {
                ExternalId = o.ExternalId,
                District = string.IsNullOrWhiteSpace(o.District) ? district : o.District.Trim(),
                Location = new Location
                {
                    RawAddress = o.Address,
                    Coordinates = new GeoCoordinate(latitude.Value, longitude.Value),
                },
                PreferredWindow = new TimeWindow(o.WindowStart.ToUniversalTime(), o.WindowEnd.ToUniversalTime()),
                ServiceDuration = TimeSpan.FromMinutes(o.DurationMinutes),
                RequiredSkill = o.RequiredSkill,
                RequiredTransport = o.RequiredTransport,
                Priority = o.Priority,
                Status = OrderStatus.NotDispatched,
                TaskType = o.TaskType,
                DepotId = depot.Id,
            });
        }

        await workOrders.AddRangeAsync(entities, ct);
        return entities.Count;
    }
}