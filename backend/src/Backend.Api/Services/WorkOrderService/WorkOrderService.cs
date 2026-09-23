using Backend.Api.Infrastructure.Repositories;
using Backend.Contracts.Domain.Entities;
using Backend.Contracts.Domain.Enums;
using Backend.Contracts.Domain.ValueObjects;
using Backend.Contracts.Requests;
using Backend.Contracts.Responses;

namespace Backend.Api.Services.WorkOrderService;

public class WorkOrderService(IDepotRepository depots, IWorkOrderRepository workOrders) : IWorkOrderService
{
    public async Task AddFromImportAsync(DepotDto depotDto, List<WorkOrderInputDto> orders, CancellationToken ct = default)
    {
        var district = orders
            .Select(o => o.District)
            .FirstOrDefault(d => !string.IsNullOrWhiteSpace(d))
            ?? depotDto.Name;

        var depot = await depots.GetOrCreateAsync(district, depotDto.Address, depotDto.Latitude, depotDto.Longitude, ct);

        var entities = orders.Select(o => new WorkOrder
        {
            ExternalId = o.ExternalId,
            District = string.IsNullOrWhiteSpace(o.District) ? district : o.District.Trim(),
            Location = new Location
            {
                RawAddress = o.Address,
                Coordinates = new GeoCoordinate(
                    o.Latitude ?? depot.Location?.Coordinates.Latitude ?? 0.0,
                    o.Longitude ?? depot.Location?.Coordinates.Longitude ?? 0.0),
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

        await workOrders.AddRangeAsync(entities, ct);
    }
}