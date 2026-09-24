using Backend.Api.Infrastructure.Repositories;
using Backend.Contracts.Domain.Entities;
using Backend.Contracts.Domain.Enums;
using Backend.Contracts.Domain.ValueObjects;
using Backend.Contracts.Requests;
using Backend.Contracts.Responses;
using Microsoft.Extensions.Logging;

namespace Backend.Api.Services.WorkOrderService;

public class WorkOrderService(IDepotRepository depots, IWorkOrderRepository workOrders, ILogger<WorkOrderService> logger) : IWorkOrderService
{
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