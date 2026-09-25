using Backend.Api.Infrastructure.Repositories;
using Backend.Api.Services;
using Backend.Api.Services.Common;
using Backend.Api.Services.CsvParserService;
using Backend.Api.Services.GeoCodingService;
using Backend.Contracts.Domain.Entities;
using Backend.Contracts.Domain.Enums;
using Backend.Contracts.Domain.ValueObjects;
using Backend.Contracts.Requests;
using Backend.Contracts.Responses;
using Backend.RoutingEngine.Helpers;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace Backend.Api.Services.WorkOrderService;

public class WorkOrderService(
    IDepotRepository depots, 
    IWorkOrderRepository workOrders, 
    IEngineerRepository engineers, 
    IEngineerRouteRepository routes, 
    OptimizationJobStore jobStore,
    ICsvParserService csvParserService,
    IGeocoder geocoder,
    ILogger<WorkOrderService> logger) : IWorkOrderService
{
    public async Task<EngineerRouteDto?> GetRouteByEngineerAsync(string identifier, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(identifier))
            return null;

        var trimmed = identifier.Trim();
        Engineer? engineer = null;
        if (Guid.TryParse(trimmed, out var guid))
        {
            engineer = await engineers.GetByIdAsync(guid, ct);
        }

        engineer ??= await engineers.GetByExternalIdAsync(trimmed, ct);
        if (engineer is null)
            return null;

        // 1. Поиск сохранённого маршрута в БД (сортируем хронологически по времени первого визита)
        var engineerRoutes = await routes.GetByEngineerAsync(engineer.Id, ct);
        if (engineerRoutes.Count > 0)
        {
            var route = engineerRoutes
                .OrderByDescending(r => r.Stops.Count > 0 ? r.Stops.Min(s => s.EstimatedArrival) : DateTimeOffset.MinValue)
                .First();
            var polyline = !string.IsNullOrEmpty(route.EncodedPolyline)
                ? route.EncodedPolyline
                : (route.Stops.Count > 0
                    ? PolylineEncoder.Encode(
                        (engineer.StartLocation != null
                            ? [(engineer.StartLocation.Coordinates.Latitude, engineer.StartLocation.Coordinates.Longitude)]
                            : Enumerable.Empty<(double, double)>())
                        .Concat(route.Stops.OrderBy(s => s.Sequence).Select(s => (s.Location.Coordinates.Latitude, s.Location.Coordinates.Longitude))))
                    : null);

            var stops = route.Stops.OrderBy(s => s.Sequence).Select(s => new RouteStopDto(
                s.Sequence,
                s.ExternalOrderId,
                s.Location?.RawAddress ?? string.Empty,
                s.Location?.Coordinates.Latitude ?? 0.0,
                s.Location?.Coordinates.Longitude ?? 0.0,
                s.EstimatedArrival,
                s.ServiceStart,
                s.ServiceEnd,
                (int)s.TravelTimeFromPrevious.TotalMinutes,
                s.DistanceFromPreviousKm,
                string.Empty,
                OrderPriority.Normal,
                s.AssignmentReason
            )).ToList();

            return new EngineerRouteDto(
                route.EngineerExternalId,
                route.EngineerName,
                route.Transport,
                route.Stops.Count,
                Math.Round(route.TotalDistanceKm, 2),
                Math.Round(route.BaselineDistanceKm ?? (route.TotalDistanceKm * 1.25), 2),
                (int)route.TotalTravelTime.TotalMinutes,
                stops,
                polyline,
                engineer.StartLocation?.RawAddress,
                engineer.StartLocation?.Coordinates.Latitude,
                engineer.StartLocation?.Coordinates.Longitude);
        }

        // 2. Fallback на in-memory план
        var lastPlan = jobStore.LastResult;
        if (lastPlan != null)
        {
            var memoryRoute = lastPlan.Routes.FirstOrDefault(r =>
                string.Equals(r.EngineerExternalId, engineer.ExternalId, StringComparison.OrdinalIgnoreCase));
            if (memoryRoute != null)
            {
                return memoryRoute;
            }
        }

        // 3. Инженер найден в базе, но на смену у него 0 назначенных заявок -> возвращаем пустой маршрут (200 OK)
        return new EngineerRouteDto(
            engineer.ExternalId,
            engineer.Name,
            engineer.Transport,
            0,
            0.0,
            0.0,
            0,
            [],
            null,
            engineer.StartLocation?.RawAddress,
            engineer.StartLocation?.Coordinates.Latitude,
            engineer.StartLocation?.Coordinates.Longitude);
    }

    public async Task<List<WorkOrderDto>> GetAllAsync(CancellationToken ct = default)
    {
        var orders = await workOrders.GetAllAsync(ct);
        return orders.Select(ToDto).ToList();
    }

    public async Task<WorkOrderDto?> GetByIdentifierAsync(string identifier, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(identifier))
            return null;

        var order = await workOrders.GetByIdentifierAsync(identifier, ct);
        return order is null ? null : ToDto(order);
    }

    public async Task<bool> DeleteByIdentifierAsync(string identifier, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(identifier))
            return false;

        return await workOrders.DeleteByIdentifierAsync(identifier, ct);
    }

    public async Task<WorkOrderDto> CreateAsync(CreateWorkOrderRequest request, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(request.Address))
            throw new ArgumentException("Адрес заявки (Address) обязателен для заполнения.");

        double lat;
        double lon;

        if (request.Latitude.HasValue && request.Longitude.HasValue)
        {
            lat = request.Latitude.Value;
            lon = request.Longitude.Value;
        }
        else
        {
            var geo = await geocoder.GeocodeAsync(request.Address.Trim(), request.District?.Trim(), ct);
            if (geo is null)
                throw new ArgumentException($"Не удалось определить координаты для адреса '{request.Address}'. Пожалуйста, укажите координаты вручную.");

            lat = geo.Latitude;
            lon = geo.Longitude;
        }

        Guid finalDepotId;
        string finalDistrict;

        // 1. Приоритет: явный выбор диспетчером офиса/депо (request.DepotId)
        if (request.DepotId.HasValue)
        {
            var depot = await depots.GetByIdAsync(request.DepotId.Value, ct);
            if (depot is null)
                throw new ArgumentException($"Депо с идентификатором '{request.DepotId.Value}' не найдено.");

            finalDepotId = depot.Id;
            finalDistrict = !string.IsNullOrWhiteSpace(request.District) ? request.District.Trim() : depot.District;
        }
        // 2. Приоритет: явное указание района заявки (request.District)
        else if (!string.IsNullOrWhiteSpace(request.District))
        {
            var district = request.District.Trim();
            var depot = await depots.GetByDistrictAsync(district, ct) ?? await depots.EnsureExistsAsync(district, ct);
            finalDepotId = depot.Id;
            finalDistrict = district;
        }
        // 3. Приоритет: автоматический подбор ближайшего депо по формуле Haversine
        else
        {
            var allDepots = await depots.GetAllAsync(ct);
            var depotsWithCoords = allDepots
                .Where(d => d.Location != null && (Math.Abs(d.Location.Coordinates.Latitude) > 0.0001 || Math.Abs(d.Location.Coordinates.Longitude) > 0.0001))
                .ToList();

            if (depotsWithCoords.Count > 0)
            {
                var nearest = depotsWithCoords.MinBy(d => CalculateHaversineDistanceKm(lat, lon, d.Location!.Coordinates.Latitude, d.Location!.Coordinates.Longitude))!;
                finalDepotId = nearest.Id;
                finalDistrict = nearest.District;
            }
            else if (allDepots.Count > 0)
            {
                finalDepotId = allDepots[0].Id;
                finalDistrict = allDepots[0].District;
            }
            else
            {
                var fallbackDepot = await depots.EnsureExistsAsync("Центральный район", ct);
                finalDepotId = fallbackDepot.Id;
                finalDistrict = fallbackDepot.District;
            }
        }

        var durationMinutes = request.DurationMinutes > 0 ? request.DurationMinutes : 30;
        var windowStart = request.WindowStart ?? DateTimeOffset.UtcNow;
        var windowEnd = request.WindowEnd ?? windowStart.AddMinutes(durationMinutes);

        if (windowEnd < windowStart)
            throw new ArgumentException("Время окончания предпочтительного окна (WindowEnd) не может быть раньше времени начала (WindowStart).");

        var externalId = !string.IsNullOrWhiteSpace(request.ExternalId)
            ? request.ExternalId.Trim()
            : $"WO-{Guid.NewGuid().ToString("N")[..8].ToUpperInvariant()}";

        var existing = await workOrders.GetByExternalIdAndDistrictAsync(externalId, finalDistrict, ct);
        if (existing != null)
            throw new InvalidOperationException($"Заявка с внешним номером '{externalId}' в районе '{finalDistrict}' уже существует.");

        var entity = new WorkOrder
        {
            ExternalId = externalId,
            District = finalDistrict,
            DepotId = finalDepotId,
            Location = new Location
            {
                RawAddress = request.Address.Trim(),
                Coordinates = new GeoCoordinate(lat, lon),
            },
            PreferredWindow = new TimeWindow(windowStart.ToUniversalTime(), windowEnd.ToUniversalTime()),
            ServiceDuration = TimeSpan.FromMinutes(durationMinutes),
            RequiredSkill = request.RequiredSkill,
            RequiredTransport = request.RequiredTransport,
            Priority = request.Priority,
            Status = OrderStatus.NotDispatched,
            TaskType = request.TaskType?.Trim() ?? string.Empty,
        };

        await workOrders.AddAsync(entity, ct);
        logger.LogInformation("Создана заявка {ExternalId} (ID: {Id}) в районе {District} (Депо ID: {DepotId})",
            entity.ExternalId, entity.Id, entity.District, entity.DepotId);

        return ToDto(entity);
    }

    private static double CalculateHaversineDistanceKm(double lat1, double lon1, double lat2, double lon2)
    {
        const double R = 6371.0;
        var dLat = (lat2 - lat1) * Math.PI / 180.0;
        var dLon = (lon2 - lon1) * Math.PI / 180.0;

        var a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2) +
                Math.Cos(lat1 * Math.PI / 180.0) * Math.Cos(lat2 * Math.PI / 180.0) *
                Math.Sin(dLon / 2) * Math.Sin(dLon / 2);

        var c = 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));
        return R * c;
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
        var centralDepot = await depots.GetOrCreateByLocationAsync(
            depotDto.Address, depotDto.Latitude, depotDto.Longitude, fallbackDistrict, ct);

        var depotLatitude = centralDepot.Location?.Coordinates.Latitude;
        var depotLongitude = centralDepot.Location?.Coordinates.Longitude;

        var ordersByDistrict = orders
            .GroupBy(o => string.IsNullOrWhiteSpace(o.District) ? fallbackDistrict : o.District.Trim())
            .ToList();

        var entities = new List<WorkOrder>();

        foreach (var districtGroup in ordersByDistrict)
        {
            var district = districtGroup.Key;

            foreach (var o in districtGroup)
            {
                var latitude = o.Latitude ?? depotLatitude;
                var longitude = o.Longitude ?? depotLongitude;
                if (latitude is null || longitude is null)
                {
                    logger.LogWarning(
                        "Заявка {ExternalId} пропущена: нет координат, а у депо {DepotId} ({District}) отсутствует локация.",
                        o.ExternalId, centralDepot.Id, district);
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
                    DepotId = centralDepot.Id,
                });
            }
        }

        await workOrders.AddRangeAsync(entities, ct);
        return entities.Count;
    }

    public async Task<OrderImportResultDto> ImportOrdersCsvAsync(List<IFormFile> files, CancellationToken ct = default)
    {
        var payloads = await FormFileHelper.ReadPayloadsAsync(files, ct);
        return await ImportOrdersFromPayloadsAsync(payloads, null, ct);
    }

    public async Task<OrderImportResultDto> ImportOrdersFromPayloadsAsync(
        List<(string FileName, byte[] Content)> files,
        IProgress<(int Processed, int Total, string Stage)>? progress = null,
        CancellationToken ct = default)
    {
        var totalUnresolvedCount = 0;
        var processedFilesCount = 0;
        var totalImportedCount = 0;
        var warnings = new List<string>();

        progress?.Report((0, 0, "Чтение и парсинг CSV файлов..."));

        var parsedFiles = new List<(string FileName, ParseImportDto ParseData)>();
        foreach (var (fileName, content) in files)
        {
            if (content.Length == 0)
                continue;

            var ext = Path.GetExtension(fileName);
            if (!string.Equals(ext, ".csv", StringComparison.OrdinalIgnoreCase))
            {
                warnings.Add($"Файл '{fileName}' пропущен: поддерживается только формат .csv");
                continue;
            }

            using var stream = new MemoryStream(content);
            var parseData = await csvParserService.ParseOrderAsync(stream, ct);

            if (parseData.Orders.Count == 0)
            {
                warnings.Add($"Файл '{fileName}' не содержит строк с заявками.");
                continue;
            }

            parsedFiles.Add((fileName, parseData));
        }

        if (parsedFiles.Count == 0)
        {
            return new OrderImportResultDto(0, 0, 0, warnings);
        }

        // Общее число всех уникальных адресов для геокодинга по всем файлам
        var ordersNeedingGeo = parsedFiles
            .SelectMany(f => f.ParseData.Orders)
            .Where(o => (o.Latitude == null || o.Longitude == null) && !string.IsNullOrWhiteSpace(o.Address))
            .Select(o => (Address: o.Address, District: o.District))
            .Distinct()
            .ToList();

        progress?.Report((0, ordersNeedingGeo.Count, $"Геокодирование адресов (0 из {ordersNeedingGeo.Count})..."));

        var geocodedMap = await geocoder.GeocodeBatchAsync(ordersNeedingGeo, progress, ct);

        for (int i = 0; i < parsedFiles.Count; i++)
        {
            var (fileName, parseData) = parsedFiles[i];

            progress?.Report((ordersNeedingGeo.Count, ordersNeedingGeo.Count, $"Сохранение заявок файла {i + 1} из {parsedFiles.Count} ({fileName})..."));

            var fallbackDistrict = parseData.Orders
                .Select(o => o.District)
                .FirstOrDefault(d => !string.IsNullOrWhiteSpace(d)) ?? "Центральный район";

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

            // Определение депо кластера: точный адрес офиса -> центроид заявок файла -> центр Москвы
            DepotDto finalDepot;
            if (parseData.RawDepot != null && !string.IsNullOrWhiteSpace(parseData.RawDepot.Address))
            {
                var geoOffice = await geocoder.GeocodeAsync(parseData.RawDepot.Address, fallbackDistrict, ct);
                if (geoOffice != null)
                {
                    finalDepot = new DepotDto(fallbackDistrict, parseData.RawDepot.Address, geoOffice.Latitude, geoOffice.Longitude);
                }
                else if (validOrders.Count > 0)
                {
                    var avgLat = Math.Round(validOrders.Average(o => o.Latitude!.Value), 6);
                    var avgLon = Math.Round(validOrders.Average(o => o.Longitude!.Value), 6);
                    warnings.Add($"Не удалось точно определить координаты депо '{parseData.RawDepot.Address}' для файла {fileName}, вычислен центроид координат заявок ({avgLat:F4}, {avgLon:F4}).");
                    finalDepot = new DepotDto(fallbackDistrict, parseData.RawDepot.Address, avgLat, avgLon);
                }
                else
                {
                    warnings.Add($"Не удалось точно определить координаты депо '{parseData.RawDepot.Address}' для файла {fileName}, использованы координаты центра Москвы.");
                    finalDepot = new DepotDto(fallbackDistrict, parseData.RawDepot.Address, 55.7558, 37.6173);
                }
            }
            else if (validOrders.Count > 0)
            {
                var avgLat = Math.Round(validOrders.Average(o => o.Latitude!.Value), 6);
                var avgLon = Math.Round(validOrders.Average(o => o.Longitude!.Value), 6);
                finalDepot = new DepotDto(fallbackDistrict, "Главное депо", avgLat, avgLon);
            }
            else
            {
                finalDepot = new DepotDto(fallbackDistrict, "Главное депо", 55.7558, 37.6173);
            }

            var imported = await AddFromImportAsync(finalDepot, validOrders, ct);
            totalImportedCount += imported;
            processedFilesCount++;
        }

        progress?.Report((ordersNeedingGeo.Count, ordersNeedingGeo.Count, "Импорт успешно завершён."));

        return new OrderImportResultDto(processedFilesCount, totalImportedCount, totalUnresolvedCount, warnings);
    }
}