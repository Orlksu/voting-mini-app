using Backend.Api.Infrastructure.Repositories;
using Backend.Contracts.Domain.Entities;
using Backend.Contracts.Domain.Enums;
using Backend.Contracts.Requests;
using Backend.Contracts.Responses;

namespace Backend.Api.Services;

/// <summary>
/// Строит «живое» состояние плана на заданный момент времени (now),
/// используя time-based inference без ручного ввода диспетчера.
///
/// Правила вывода статуса для каждой остановки:
///   ServiceEnd   &lt;= now  → Completed (инженер уже ушёл с точки)
///   ServiceStart &lt;= now &lt; ServiceEnd → InProgress (инженер сейчас на объекте)
///   ServiceStart &gt;  now  → Pending (остановка ещё не началась)
///
/// Текущая позиция инженера = последняя Completed точка (или его депо).
/// Доступен с = ServiceEnd InProgress-остановки (или now, если свободен).
/// </summary>
public sealed class LivePlanStateBuilder(
    OptimizationJobStore jobStore,
    IEngineerRepository engineerRepo,
    IWorkOrderRepository workOrderRepo)
{
    /// <summary>
    /// Состояние инженера на момент now.
    /// </summary>
    public sealed record EngineerLiveContext(
        EngineerInputDto Engineer,
        /// <summary>Остановки плана, которые ещё не начаты (Pending) на момент now.</summary>
        List<RouteStopDto> PendingStops,
        /// <summary>ExternalId заявок, которые уже выполнены (Completed) на момент now.</summary>
        List<string> CompletedOrderIds
    );

    /// <summary>
    /// Строит live-контекст для всех инженеров из последнего плана.
    /// </summary>
    /// <param name="now">Момент времени, на который строится срез.</param>
    /// <param name="ct">Токен отмены.</param>
    public async Task<(List<EngineerLiveContext> Contexts, HashSet<string> OrphanedOrderIds)> BuildAsync(DateTimeOffset now, CancellationToken ct)
    {
        var lastResult = jobStore.LastResult;
        if (lastResult == null)
            return ([], new HashSet<string>(StringComparer.OrdinalIgnoreCase));

        // Загружаем всех инженеров из БД для получения координат депо
        var allEngineers = await engineerRepo.GetAvailableEngineersAsync(ct: ct);
        var engineerMap = allEngineers
            .GroupBy(e => e.ExternalId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

        var contexts = new List<EngineerLiveContext>();
        var orphanedOrderIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var route in lastResult.Routes)
        {
            if (!engineerMap.TryGetValue(route.EngineerExternalId, out var engEntity))
            {
                foreach (var stop in route.Stops)
                {
                    if (stop.ServiceEnd > now)
                    {
                        orphanedOrderIds.Add(stop.ExternalOrderId.Trim());
                    }
                }
                continue;
            }

            var completedIds = new List<string>();
            var pendingStops = new List<RouteStopDto>();

            // Текущая позиция: последняя completed точка или депо
            double currentLat = engEntity.StartLocation.Coordinates.Latitude;
            double currentLon = engEntity.StartLocation.Coordinates.Longitude;
            DateTimeOffset availableFrom = now;

            foreach (var stop in route.Stops.OrderBy(s => s.Sequence))
            {
                if (stop.ServiceEnd <= now)
                {
                    // Остановка завершена
                    completedIds.Add(stop.ExternalOrderId);
                    currentLat = stop.Latitude;
                    currentLon = stop.Longitude;
                }
                else if (stop.ServiceStart <= now)
                {
                    // Инженер сейчас на объекте — недоступен до конца работ
                    currentLat = stop.Latitude;
                    currentLon = stop.Longitude;
                    availableFrom = stop.ServiceEnd;
                    // Эту заявку НЕ добавляем в pending — она выполняется прямо сейчас
                }
                else
                {
                    // Остановка ещё впереди
                    pendingStops.Add(stop);
                }
            }

            var engineerDto = BuildEngineerDto(engEntity, currentLat, currentLon, availableFrom);
            contexts.Add(new EngineerLiveContext(engineerDto, pendingStops, completedIds));
        }

        return (contexts, orphanedOrderIds);
    }

    /// <summary>
    /// Собирает список оставшихся WorkOrderInputDto из pending-остановок всех инженеров,
    /// плюс все NotDispatched заявки из БД (не вошедшие в прошлый план).
    /// Исключает заявки с указанным ExternalId (например, отменённые).
    /// </summary>
    public async Task<List<WorkOrderInputDto>> BuildRemainingOrdersAsync(
        List<EngineerLiveContext> contexts,
        HashSet<string> orphanedOrderIds,
        DateTimeOffset? targetDate,
        string? excludeExternalId,
        CancellationToken ct)
    {
        var allPendingDb = await workOrderRepo
            .GetPendingOrdersForPlanningAsync(targetDate: targetDate, status: null, ct: ct);

        var pendingExtIds = contexts
            .SelectMany(c => c.PendingStops)
            .Select(s => s.ExternalOrderId.Trim())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
            
        foreach (var id in orphanedOrderIds)
        {
            pendingExtIds.Add(id);
        }

        var all = new List<WorkOrderInputDto>();

        foreach (var dbOrder in allPendingDb)
        {
            var extId = dbOrder.ExternalId.Trim();
            if (dbOrder.Status == OrderStatus.NotDispatched || pendingExtIds.Contains(extId))
            {
                all.Add(ToWorkOrderInputDto(dbOrder));
            }
        }

        if (!string.IsNullOrWhiteSpace(excludeExternalId))
        {
            all = all
                .Where(o => !string.Equals(o.ExternalId.Trim(), excludeExternalId.Trim(),
                    StringComparison.OrdinalIgnoreCase))
                .ToList();
        }

        return all;
    }

    private static EngineerInputDto BuildEngineerDto(
        Backend.Contracts.Domain.Entities.Engineer eng,
        double currentLat,
        double currentLon,
        DateTimeOffset availableFrom)
    {
        var mskOffset = TimeSpan.FromHours(3);
        var availMsk = availableFrom.ToOffset(mskOffset);
        var shiftMskStart = eng.Shift.Start.ToOffset(mskOffset);
        var shiftMskEnd = eng.Shift.End.ToOffset(mskOffset);

        var shiftDuration = shiftMskEnd - shiftMskStart;
        if (shiftDuration <= TimeSpan.Zero)
            shiftDuration += TimeSpan.FromDays(1);

        var alignedStartMsk = new DateTimeOffset(
            availMsk.Year, availMsk.Month, availMsk.Day,
            shiftMskStart.Hour, shiftMskStart.Minute, shiftMskStart.Second,
            mskOffset);

        var alignedEndMsk = alignedStartMsk + shiftDuration;
        if (alignedEndMsk <= availMsk)
        {
            alignedEndMsk = availMsk + shiftDuration;
        }

        return new(
            ExternalId: eng.ExternalId,
            Name: eng.Name,
            StartAddress: eng.StartLocation.RawAddress,
            StartLatitude: currentLat,
            StartLongitude: currentLon,
            ShiftStart: availableFrom,
            ShiftEnd: alignedEndMsk.ToUniversalTime(),
            Transport: eng.Transport,
            Skills: [.. eng.Skills],
            MaxOrdersPerShift: eng.MaxOrdersPerShift,
            District: eng.District,
            DepotId: eng.DepotId);
    }

    private static WorkOrderInputDto ToWorkOrderInputDto(WorkOrder o) => new()
    {
        InternalId = o.Id,
        ExternalId = o.ExternalId,
        District = o.District,
        Address = o.Location?.RawAddress ?? string.Empty,
        Latitude = o.Location?.Coordinates.Latitude,
        Longitude = o.Location?.Coordinates.Longitude,
        WindowStart = o.PreferredWindow.Start,
        WindowEnd = o.PreferredWindow.End,
        RequiredSkill = o.RequiredSkill,
        RequiredTransport = o.RequiredTransport,
        Priority = o.Priority,
        TaskType = o.TaskType,
        DurationMinutes = (int)o.ServiceDuration.TotalMinutes,
        Status = o.Status,
    };
}
