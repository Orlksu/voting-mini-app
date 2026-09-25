using Backend.Api.Infrastructure.Repositories;
using Backend.Api.Services.CsvParserService;
using Backend.Api.Services.GeoCodingService;
using Backend.Contracts.Domain.Entities;
using Backend.Contracts.Domain.Enums;
using Backend.Contracts.Domain.ValueObjects;
using Backend.Contracts.Requests;
using Backend.Contracts.Responses;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace Backend.Api.Services.WorkOrderService;

public class WorkOrderService(
    IDepotRepository depots, 
    IWorkOrderRepository workOrders, 
    IEngineerRepository engineers, 
    IEngineerRouteRepository routes, 
    ICsvParserService csvParserService,
    IGeocoder geocoder,
    ILogger<WorkOrderService> logger) : IWorkOrderService
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
        if (orders.Count == 0)
            return 0;

        var fallbackDistrict = string.IsNullOrWhiteSpace(depotDto.Name) ? "Основной район" : depotDto.Name.Trim();
        var ordersByDistrict = orders
            .GroupBy(o => string.IsNullOrWhiteSpace(o.District) ? fallbackDistrict : o.District.Trim())
            .ToList();

        var entities = new List<WorkOrder>();

        foreach (var districtGroup in ordersByDistrict)
        {
            var district = districtGroup.Key;

            var depot = await depots.GetOrCreateAsync(district, depotDto.Address, depotDto.Latitude, depotDto.Longitude, ct);

            var depotLatitude = depot.Location?.Coordinates.Latitude;
            var depotLongitude = depot.Location?.Coordinates.Longitude;

            foreach (var o in districtGroup)
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

                var windowStart = o.WindowStart ?? DateTimeOffset.UtcNow;
                var windowEnd = o.WindowEnd ?? windowStart.AddMinutes(o.DurationMinutes);

                entities.Add(new WorkOrder
                {
                    ExternalId = o.ExternalId,
                    District = district,
                    Location = new Location
                    {
                        RawAddress = o.Address,
                        Coordinates = new GeoCoordinate(latitude.Value, longitude.Value),
                    },
                    PreferredWindow = new TimeWindow(windowStart.ToUniversalTime(), windowEnd.ToUniversalTime()),
                    ServiceDuration = TimeSpan.FromMinutes(o.DurationMinutes),
                    RequiredSkill = o.RequiredSkill,
                    RequiredTransport = o.RequiredTransport,
                    Priority = o.Priority,
                    Status = OrderStatus.NotDispatched,
                    TaskType = o.TaskType,
                    DepotId = depot.Id,
                });
            }
        }

        await workOrders.AddRangeAsync(entities, ct);
        return entities.Count;
    }

    public async Task<OrderImportResultDto> ImportOrdersCsvAsync(List<IFormFile> files, CancellationToken ct = default)
    {
        var totalUnresolvedCount = 0;
        var processedFilesCount = 0;
        var totalImportedCount = 0;
        var warnings = new List<string>();

        foreach (var file in files)
        {
            if (file.Length == 0)
                continue;

            var ext = Path.GetExtension(file.FileName);
            if (!string.Equals(ext, ".csv", StringComparison.OrdinalIgnoreCase))
            {
                warnings.Add($"Файл '{file.FileName}' пропущен: поддерживается только формат .csv");
                continue;
            }

            await using var stream = file.OpenReadStream();
            var parseData = await csvParserService.ParseOrderAsync(stream, ct);

            if (parseData.Orders.Count == 0)
            {
                warnings.Add($"Файл '{file.FileName}' не содержит строк с заявками.");
                continue;
            }

            var fallbackDistrict = parseData.Orders
                .Select(o => o.District)
                .FirstOrDefault(d => !string.IsNullOrWhiteSpace(d)) ?? "Центральный район";

            DepotDto finalDepot;
            if (parseData.RawDepot != null && !string.IsNullOrWhiteSpace(parseData.RawDepot.Address))
            {
                var geoOffice = await geocoder.GeocodeAsync(parseData.RawDepot.Address, fallbackDistrict, ct);
                if (geoOffice != null)
                {
                    finalDepot = new DepotDto(fallbackDistrict, parseData.RawDepot.Address, geoOffice.Latitude, geoOffice.Longitude);
                }
                else
                {
                    warnings.Add($"Не удалось точно определить координаты депо '{parseData.RawDepot.Address}' для файла {file.FileName}, использованы координаты центра Москвы.");
                    finalDepot = new DepotDto(fallbackDistrict, parseData.RawDepot.Address, 55.7558, 37.6173);
                }
            }
            else
            {
                finalDepot = new DepotDto(fallbackDistrict, "Главное депо", 55.7558, 37.6173);
            }

            // Пакетное предвычисление координат для всех адресов файла без координат
            var ordersNeedingGeo = parseData.Orders
                .Where(o => (o.Latitude == null || o.Longitude == null) && !string.IsNullOrWhiteSpace(o.Address))
                .Select(o => (Address: o.Address, District: o.District))
                .Distinct()
                .ToList();

            var geocodedMap = await geocoder.GeocodeBatchAsync(ordersNeedingGeo, ct);

            var validOrders = new List<WorkOrderInputDto>();
            foreach (var order in parseData.Orders)
            {
                if (order.Latitude != null && order.Longitude != null)
                {
                    validOrders.Add(order);
                    continue;
                }

                if (!string.IsNullOrWhiteSpace(order.Address))
                {
                    var key = GeocoderService.QueryKey(order.Address, order.District);
                    if (geocodedMap.TryGetValue(key, out var geo) && geo != null)
                    {
                        order.Latitude = geo.Latitude;
                        order.Longitude = geo.Longitude;
                        validOrders.Add(order);
                        continue;
                    }
                }

                totalUnresolvedCount++;
            }

            var imported = await AddFromImportAsync(finalDepot, validOrders, ct);
            totalImportedCount += imported;
            processedFilesCount++;
        }

        return new OrderImportResultDto(processedFilesCount, totalImportedCount, totalUnresolvedCount, warnings);
    }
}