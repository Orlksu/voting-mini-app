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
    public async Task<EngineerRouteDto?> GetRouteByEngineerAsync(string identifier, DateTimeOffset? date = null, CancellationToken ct = default)
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

        // 1. Поиск сохранённого маршрута в БД (с опциональной фильтрацией по целевой дате)
        var engineerRoutes = await routes.GetByEngineerAsync(engineer.Id, date, ct);
        if (engineerRoutes.Count > 0)
        {
            var route = engineerRoutes.First();
            var polyline = !string.IsNullOrEmpty(route.EncodedPolyline)
                ? route.EncodedPolyline
                : (route.Stops.Count > 0
                    ? PolylineEncoder.Encode(
                        (engineer.StartLocation != null
                            ? [(engineer.StartLocation.Coordinates.Latitude, engineer.StartLocation.Coordinates.Longitude)]
                            : Enumerable.Empty<(double, double)>())
                        .Concat(route.Stops.OrderBy(s => s.Sequence).Select(s => (s.Location.Coordinates.Latitude, s.Location.Coordinates.Longitude))))
                    : null);

            var orderGuids = route.Stops
                .Select(s => s.OrderId)
                .Where(id => id.HasValue)
                .Select(id => id!.Value)
                .Distinct()
                .ToList();

            var matchedByIds = orderGuids.Count > 0
                ? await workOrders.GetByIdsAsync(orderGuids, ct)
                : [];
            var orderByIdMap = matchedByIds.ToDictionary(o => o.Id);

            var fallbackExternalIds = route.Stops
                .Where(s => !s.OrderId.HasValue || !orderByIdMap.ContainsKey(s.OrderId.Value))
                .Select(s => s.ExternalOrderId)
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .Distinct()
                .ToList();

            var fallbackOrderMap = fallbackExternalIds.Count > 0
                ? (await workOrders.GetByExternalIdsAsync(fallbackExternalIds, ct))
                    .GroupBy(o => o.ExternalId, StringComparer.OrdinalIgnoreCase)
                    .ToDictionary(
                        g => g.Key, 
                        g => g.FirstOrDefault(o => 
                            (engineer.DepotId.HasValue && o.DepotId == engineer.DepotId) ||
                            (!string.IsNullOrEmpty(engineer.District) && string.Equals(o.District, engineer.District, StringComparison.OrdinalIgnoreCase))) ?? g.First(), 
                        StringComparer.OrdinalIgnoreCase)
                : new Dictionary<string, WorkOrder>(StringComparer.OrdinalIgnoreCase);

            var stops = route.Stops.OrderBy(s => s.Sequence).Select(s =>
            {
                WorkOrder? wo = null;
                if (s.OrderId.HasValue && orderByIdMap.TryGetValue(s.OrderId.Value, out var matched))
                {
                    wo = matched;
                }
                else if (!string.IsNullOrWhiteSpace(s.ExternalOrderId) && fallbackOrderMap.TryGetValue(s.ExternalOrderId, out var fallback))
                {
                    wo = fallback;
                }

                return new RouteStopDto(
                    s.Sequence,
                    s.ExternalOrderId,
                    s.Location?.RawAddress ?? (wo?.Location?.RawAddress ?? string.Empty),
                    s.Location?.Coordinates.Latitude ?? (wo?.Location?.Coordinates.Latitude ?? 0.0),
                    s.Location?.Coordinates.Longitude ?? (wo?.Location?.Coordinates.Longitude ?? 0.0),
                    s.EstimatedArrival,
                    s.ServiceStart,
                    s.ServiceEnd,
                    (int)s.TravelTimeFromPrevious.TotalMinutes,
                    s.DistanceFromPreviousKm,
                    wo?.TaskType ?? string.Empty,
                    wo?.Priority ?? OrderPriority.Normal,
                    s.AssignmentReason,
                    wo?.Status ?? OrderStatus.Cancelled,
                    wo?.Id ?? s.OrderId
                );
            }).ToList();

            var activeCount = stops.Count(s => s.Status is OrderStatus.Dispatched or OrderStatus.InProgress);
            var completedCount = stops.Count(s => s.Status == OrderStatus.Completed);
            var cancelledCount = stops.Count(s => s.Status == OrderStatus.Cancelled);

            return new EngineerRouteDto(
                route.EngineerExternalId,
                route.EngineerName,
                route.Transport,
                route.Stops.Count,
                activeCount,
                completedCount,
                cancelledCount,
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
            bool dateMatches = true;
            if (date.HasValue)
            {
                var msk = TimeSpan.FromHours(3);
                var targetMsk = date.Value.ToOffset(msk).Date;
                var planMsk = lastPlan.PlanDate.ToOffset(msk).Date;
                dateMatches = targetMsk == planMsk;
            }

            if (dateMatches)
            {
                var memoryRoute = lastPlan.Routes.FirstOrDefault(r =>
                    string.Equals(r.EngineerExternalId, engineer.ExternalId, StringComparison.OrdinalIgnoreCase));
                if (memoryRoute != null)
                {
                    var externalIds = memoryRoute.Stops
                        .Select(s => s.ExternalOrderId)
                        .Where(id => !string.IsNullOrWhiteSpace(id))
                        .Distinct()
                        .ToList();
                    var matchedOrders = await workOrders.GetByExternalIdsAsync(externalIds, ct);
                    if (matchedOrders.Count > 0)
                    {
                        var orderMap = matchedOrders
                            .GroupBy(o => o.ExternalId, StringComparer.OrdinalIgnoreCase)
                            .ToDictionary(
                                g => g.Key, 
                                g => g.FirstOrDefault(o => 
                            (engineer.DepotId.HasValue && o.DepotId == engineer.DepotId) ||
                            (!string.IsNullOrEmpty(engineer.District) && string.Equals(o.District, engineer.District, StringComparison.OrdinalIgnoreCase))) ?? g.First(), 
                                StringComparer.OrdinalIgnoreCase);

                        var enrichedStops = memoryRoute.Stops.Select(s =>
                        {
                            if (orderMap.TryGetValue(s.ExternalOrderId, out var wo))
                            {
                                return s with
                                {
                                    Status = wo.Status,
                                    TaskType = string.IsNullOrEmpty(s.TaskType) ? wo.TaskType : s.TaskType,
                                    Priority = wo.Priority,
                                    OrderId = wo.Id
                                };
                            }
                            return s with { Status = OrderStatus.Cancelled };
                        }).ToList();

                        var memoryActiveCount = enrichedStops.Count(s => s.Status is OrderStatus.Dispatched or OrderStatus.InProgress);
                        var memoryCompletedCount = enrichedStops.Count(s => s.Status == OrderStatus.Completed);
                        var memoryCancelledCount = enrichedStops.Count(s => s.Status == OrderStatus.Cancelled);

                        return memoryRoute with
                        {
                            Stops = enrichedStops,
                            ActiveOrdersCount = memoryActiveCount,
                            CompletedOrdersCount = memoryCompletedCount,
                            CancelledOrdersCount = memoryCancelledCount
                        };
                    }

                    return memoryRoute;
                }
            }
        }

        // 3. Инженер найден в базе, но на смену у него 0 назначенных заявок -> возвращаем пустой маршрут (200 OK)
        return new EngineerRouteDto(
            engineer.ExternalId,
            engineer.Name,
            engineer.Transport,
            0,
            0,
            0,
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

    public async Task<WorkOrderDto?> GetByIdentifierAsync(string identifier, string? district = null, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(identifier))
            return null;

        var matches = await workOrders.FindByIdentifierAsync(identifier, district, ct);
        if (matches.Count > 1)
        {
            var districts = string.Join(", ", matches.Select(o => $"'{o.District}'"));
            throw new InvalidOperationException(
                $"Найдено несколько заявок ({matches.Count}) с номером '{identifier.Trim()}' в разных районах ({districts}). " +
                $"Укажите параметр запроса 'district' или точный уникальный Id (Guid) заявки.");
        }

        return matches.Count == 1 ? ToDto(matches[0]) : null;
    }

    public async Task<bool> DeleteByIdentifierAsync(string identifier, string? district = null, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(identifier))
            return false;

        var matches = await workOrders.FindByIdentifierAsync(identifier, district, ct);
        if (matches.Count > 1)
        {
            var districts = string.Join(", ", matches.Select(o => $"'{o.District}'"));
            throw new InvalidOperationException(
                $"Найдено несколько заявок ({matches.Count}) с номером '{identifier.Trim()}' в разных районах ({districts}). " +
                $"Для безопасного удаления укажите район (параметр 'district') или точный уникальный Id (Guid) заявки.");
        }

        if (matches.Count == 0)
            return false;

        var targetOrder = matches[0];
        if (!targetOrder.CanBeDeleted())
        {
            throw new InvalidOperationException(
                $"Нельзя удалить заявку '{targetOrder.ExternalId}' со статусом '{targetOrder.Status}'. " +
                $"Удаление разрешено только для нераспределенных (NotDispatched) или отмененных заявок.");
        }

        return await workOrders.DeleteAsync(targetOrder, ct);
    }

    public async Task<WorkOrderDto> CancelAsync(string identifier, string? district = null, string? reason = null, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(identifier))
            throw new KeyNotFoundException("Идентификатор заявки не может быть пустым.");

        if (reason != null && reason.Trim().Length > 500)
            throw new ArgumentException("Длина причины отмены (Reason) не должна превышать 500 символов.");

        var matches = await workOrders.FindByIdentifierAsync(identifier, district, ct);
        if (matches.Count == 0)
            throw new KeyNotFoundException($"Заявка с идентификатором '{identifier.Trim()}' не найдена.");

        if (matches.Count > 1)
        {
            var districts = string.Join(", ", matches.Select(o => $"'{o.District}'"));
            throw new InvalidOperationException(
                $"Найдено несколько заявок ({matches.Count}) с номером '{identifier.Trim()}' в разных районах ({districts}). " +
                $"Для отмены заявки укажите точный уникальный Id (Guid) заявки или передайте параметр district.");
        }

        var order = matches[0];
        order.Cancel(reason);
        await workOrders.UpdateAsync(order, ct);
        jobStore.UpdateOrderStatus(order.ExternalId, order.Status);

        logger.LogInformation("Заявка {ExternalId} (ID: {Id}) успешно отменена. Причина: {Reason}",
            order.ExternalId, order.Id, order.CancellationReason ?? "не указана");

        return ToDto(order);
    }

    public async Task<WorkOrderDto> CompleteAsync(string identifier, string? district = null, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(identifier))
            throw new KeyNotFoundException("Идентификатор заявки не может быть пустым.");

        var matches = await workOrders.FindByIdentifierAsync(identifier, district, ct);
        if (matches.Count == 0)
            throw new KeyNotFoundException($"Заявка с идентификатором '{identifier.Trim()}' не найдена.");

        if (matches.Count > 1)
        {
            var districts = string.Join(", ", matches.Select(o => $"'{o.District}'"));
            throw new InvalidOperationException(
                $"Найдено несколько заявок ({matches.Count}) с номером '{identifier.Trim()}' в разных районах ({districts}). " +
                $"Для завершения заявки укажите точный уникальный Id (Guid) заявки или передайте параметр district.");
        }

        var order = matches[0];
        order.Complete();
        await workOrders.UpdateAsync(order, ct);
        jobStore.UpdateOrderStatus(order.ExternalId, order.Status);

        logger.LogInformation("Заявка {ExternalId} (ID: {Id}) успешно завершена.",
            order.ExternalId, order.Id);

        return ToDto(order);
    }

    public async Task<WorkOrderDto> CreateAsync(CreateWorkOrderRequest request, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(request.Address))
            throw new ArgumentException("Адрес заявки (Address) обязателен для заполнения.");

        if (request.Address.Trim().Length > 500)
            throw new ArgumentException("Длина адреса заявки (Address) не должна превышать 500 символов.");

        if (request.TaskType != null && request.TaskType.Trim().Length > 100)
            throw new ArgumentException("Длина типа задачи (TaskType) не должна превышать 100 символов.");

        if (request.District != null && request.District.Trim().Length > 100)
            throw new ArgumentException("Длина названия района (District) не должна превышать 100 символов.");

        if (request.ExternalId != null && request.ExternalId.Trim().Length > 100)
            throw new ArgumentException("Длина внешнего идентификатора (ExternalId) не должна превышать 100 символов.");

        if (request.DurationMinutes <= 0 || request.DurationMinutes > 720)
            throw new ArgumentException("Длительность выполнения заявки (DurationMinutes) должна быть в диапазоне от 1 до 720 минут (до 12 часов).");

        if (!Enum.IsDefined(typeof(SkillType), request.RequiredSkill) || (int)request.RequiredSkill == 0)
            throw new ArgumentException("Квалификация (RequiredSkill) обязательна и должна принимать одно из значений: LocalRepair, Installation, Emergency.");

        if (!Enum.IsDefined(typeof(OrderPriority), request.Priority) || (int)request.Priority == 0)
            throw new ArgumentException("Приоритет (Priority) должен принимать одно из значений: Low, Normal, High, Emergency.");

        if (request.RequiredTransport.HasValue && (!Enum.IsDefined(typeof(TransportType), request.RequiredTransport.Value) || (int)request.RequiredTransport.Value == 0))
            throw new ArgumentException("Тип транспорта (RequiredTransport) должен принимать одно из значений: Car, PublicTransit, Bicycle, Pedestrian.");

        double lat;
        double lon;

        if (request.Latitude.HasValue != request.Longitude.HasValue)
            throw new ArgumentException("Широта (Latitude) и долгота (Longitude) должны быть переданы парой либо опущены для автогеокодирования.");

        if (request.Latitude.HasValue && request.Longitude.HasValue)
        {
            lat = request.Latitude.Value;
            lon = request.Longitude.Value;

            if (double.IsNaN(lat) || double.IsInfinity(lat) || lat is < -90.0 or > 90.0)
                throw new ArgumentException($"Недопустимое значение широты (Latitude): {lat}. Допустимый диапазон: от -90 до 90.");

            if (double.IsNaN(lon) || double.IsInfinity(lon) || lon is < -180.0 or > 180.0)
                throw new ArgumentException($"Недопустимое значение долготы (Longitude): {lon}. Допустимый диапазон: от -180 до 180.");

            if (Math.Abs(lat) < 0.0001 && Math.Abs(lon) < 0.0001)
                throw new ArgumentException("Координаты (0, 0) недопустимы. Укажите реальные географические координаты.");
        }
        else
        {
            var geo = await geocoder.GeocodeAsync(request.Address.Trim(), request.District?.Trim(), ct);
            if (geo is null)
                throw new ArgumentException($"Не удалось определить координаты для адреса '{request.Address}'. Пожалуйста, укажите координаты вручную.");

            lat = geo.Latitude;
            lon = geo.Longitude;

            if (lat is < -90.0 or > 90.0 || lon is < -180.0 or > 180.0 || (Math.Abs(lat) < 0.0001 && Math.Abs(lon) < 0.0001))
                throw new ArgumentException($"Геокодер вернул некорректные координаты ({lat}, {lon}) для адреса '{request.Address}'.");
        }

        Guid finalDepotId;
        string finalDistrict;

        // 1. Приоритет: явный выбор диспетчером офиса/депо (request.DepotId)
        if (request.DepotId.HasValue)
        {
            var depot = await depots.GetByIdAsync(request.DepotId.Value, ct);
            if (depot is null)
                throw new ArgumentException($"Депо с идентификатором '{request.DepotId.Value}' не найдено.");

            if (!string.IsNullOrWhiteSpace(request.District) &&
                !string.Equals(request.District.Trim(), depot.District.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                throw new ArgumentException(
                    $"Указанный район '{request.District.Trim()}' не соответствует району выбранного депо '{depot.District}'. " +
                    $"Для привязки к депо '{depot.District}' оставьте поле района пустым либо укажите район депо.");
            }

            finalDepotId = depot.Id;
            finalDistrict = depot.District;
        }
        // 2. Приоритет: явное указание района заявки (request.District)
        else if (!string.IsNullOrWhiteSpace(request.District))
        {
            var district = request.District.Trim();
            var depot = await depots.GetByDistrictAsync(district, ct);
            if (depot is null)
            {
                depot = await depots.GetOrCreateByLocationAsync(request.Address.Trim(), lat, lon, district, ct);
            }

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
                var orderCoord = new GeoCoordinate(lat, lon);
                var nearest = depotsWithCoords.MinBy(d => orderCoord.DistanceToKm(d.Location!.Coordinates))!;
                finalDepotId = nearest.Id;
                finalDistrict = nearest.District;
            }
            else if (allDepots.Count > 0)
            {
                var fallbackChoice = allDepots.OrderBy(d => d.District).ThenBy(d => d.Id).First();
                finalDepotId = fallbackChoice.Id;
                finalDistrict = fallbackChoice.District;
            }
            else
            {
                var fallbackDepot = await depots.GetOrCreateByLocationAsync(request.Address.Trim(), lat, lon, "Центральный район", ct);
                finalDepotId = fallbackDepot.Id;
                finalDistrict = fallbackDepot.District;
            }
        }

        var durationMinutes = request.DurationMinutes;
        if (durationMinutes <= 0)
            throw new ArgumentException("Длительность обслуживания (DurationMinutes) должна быть строго больше нуля.");

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
        o.DepotId,
        o.CancellationReason);

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

                var extId = !string.IsNullOrWhiteSpace(o.ExternalId)
                    ? o.ExternalId.Trim()
                    : $"WO-IMP-{Guid.NewGuid():N}"[..15].ToUpperInvariant();

                entities.Add(new WorkOrder
                {
                    ExternalId = extId,
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

        // Общее число всех уникальных адресов для геокодинга по всем файлам (включая заявки без координат или с фиктивными 0,0)
        var ordersNeedingGeo = parsedFiles
            .SelectMany(f => f.ParseData.Orders)
            .Where(o => (o.Latitude == null || o.Longitude == null || (Math.Abs(o.Latitude.Value) < 0.0001 && Math.Abs(o.Longitude.Value) < 0.0001)) 
                     && !string.IsNullOrWhiteSpace(o.Address))
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
                bool hasValidCoords = order.Latitude.HasValue && order.Longitude.HasValue
                    && order.Latitude is >= -90.0 and <= 90.0
                    && order.Longitude is >= -180.0 and <= 180.0
                    && (Math.Abs(order.Latitude.Value) > 0.0001 || Math.Abs(order.Longitude.Value) > 0.0001);

                if (hasValidCoords)
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