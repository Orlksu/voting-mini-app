using System.Globalization;
using Backend.Api.Infrastructure.Repositories;
using Backend.Api.Services.GeoCodingService;
using Backend.Contracts.Domain.Entities;
using Backend.Contracts.Domain.Enums;
using Backend.Contracts.Domain.ValueObjects;
using Backend.Contracts.Requests;
using Backend.Contracts.Responses;

namespace Backend.Api.Services.EngineerService;

public class EngineerService(IDepotRepository depots, IEngineerRepository engineers, IGeocoder geocoder) : IEngineerService
{
    public async Task<List<EngineerDto>> GetAllAsync(CancellationToken ct = default)
    {
        var list = await engineers.GetAllAsync(ct);
        return list.Select(ToDto).ToList();
    }

    private static EngineerDto ToDto(Engineer e) => new(
        e.Id,
        e.ExternalId,
        e.Name,
        e.District,
        e.DepotId,
        e.StartLocation?.RawAddress ?? string.Empty,
        e.StartLocation?.Coordinates.Latitude ?? 0.0,
        e.StartLocation?.Coordinates.Longitude ?? 0.0,
        e.Shift.Start,
        e.Shift.End,
        e.Transport,
        e.Skills,
        e.IsAvailable,
        e.MaxOrdersPerShift);

    public async Task<Engineer> AddAsync(string district, EngineerInputDto engineer, CancellationToken ct = default)
    {
        var depot = await depots.GetOrCreateAsync(
            district,
            engineer.StartAddress,
            engineer.StartLatitude ?? 0.0,
            engineer.StartLongitude ?? 0.0,
            ct);

        var entity = new Engineer
        {
            ExternalId = engineer.ExternalId,
            Name = engineer.Name,
            District = district,
            DepotId = depot.Id,
            StartLocation = depot.Location ?? new Location
            {
                RawAddress = engineer.StartAddress,
                Coordinates = new GeoCoordinate(engineer.StartLatitude ?? 0.0, engineer.StartLongitude ?? 0.0),
            },
            Shift = new TimeWindow(engineer.ShiftStart.ToUniversalTime(), engineer.ShiftEnd.ToUniversalTime()),
            Transport = engineer.Transport,
            Skills = engineer.Skills.ToHashSet(),
            IsAvailable = true,
            MaxOrdersPerShift = engineer.MaxOrdersPerShift,
        };

        await engineers.AddAsync(entity, ct);
        return entity;
    }

    public async Task<List<Engineer>> AddRangeAsync(string district, IEnumerable<EngineerInputDto> engineers, CancellationToken ct = default)
    {
        var result = new List<Engineer>();
        foreach (var engineer in engineers)
            result.Add(await AddAsync(district, engineer, ct));
        return result;
    }

    public async Task<EngineerImportResultDto> ImportEngineersAsync(List<EngineerCsvRowDto> rawEngineers, CancellationToken ct = default)
    {
        var warnings = new List<string>();
        var imported = 0;
        var skipped = 0;
        var unresolved = 0;
        var depotsCreated = 0;

        var todayUtc = DateTimeOffset.UtcNow;
        var seenExternalIds = new HashSet<string>();
        var districtDepots = new Dictionary<string, Depot>();
        var entities = new List<Engineer>();

        foreach (var raw in rawEngineers)
        {
            var externalId = raw.ExternalId.Trim();
            if (string.IsNullOrWhiteSpace(externalId))
            {
                skipped++;
                continue;
            }

            if (!seenExternalIds.Add(externalId))
            {
                warnings.Add($"Дубликат ExternalId '{externalId}' в файле — строка пропущена.");
                skipped++;
                continue;
            }

            var district = string.IsNullOrWhiteSpace(raw.District) ? "Неизвестный район" : raw.District.Trim();

            if (!districtDepots.TryGetValue(district, out var depot))
            {
                depot = await depots.GetByDistrictAsync(district, ct);
                if (depot is null)
                {
                    depot = await depots.EnsureExistsAsync(district, ct);
                    depotsCreated++;
                }
                districtDepots[district] = depot;
            }

            var startLocation = await ResolveStartLocationAsync(raw, depot, ct);
            if (startLocation is null)
            {
                warnings.Add($"Не удалось определить координаты старта для инженера '{externalId}' — пропущено.");
                unresolved++;
                continue;
            }

            var shiftStart = ParseShift(raw.ShiftStart, todayUtc);
            var shiftEnd = ParseShift(raw.ShiftEnd, todayUtc);
            if (shiftStart is null || shiftEnd is null)
            {
                warnings.Add($"Невалидный формат времени смены у инженера '{externalId}' — пропущено.");
                skipped++;
                continue;
            }

            if (shiftEnd.Value <= shiftStart.Value)
                shiftEnd = shiftStart.Value.AddDays(1);

            var transport = ParseTransport(raw.Transport);
            if (transport is null)
            {
                warnings.Add($"Не удалось распознать транспорт '{raw.Transport}' у инженера '{externalId}' — пропущено.");
                skipped++;
                continue;
            }

            entities.Add(new Engineer
            {
                ExternalId = externalId,
                Name = string.IsNullOrWhiteSpace(raw.Name) ? "Без имени" : raw.Name.Trim(),
                District = district,
                DepotId = depot.Id,
                StartLocation = startLocation,
                Shift = new TimeWindow(shiftStart.Value, shiftEnd.Value),
                Transport = transport.Value,
                Skills = ParseSkills(raw.Skills),
                IsAvailable = true,
                MaxOrdersPerShift = ParseMaxOrders(raw.MaxOrdersPerShift),
            });

            imported++;
        }

        if (entities.Count > 0)
            await engineers.UpsertRangeAsync(entities, ct);

        return new EngineerImportResultDto(imported, skipped, unresolved, depotsCreated, warnings);
    }

    public async Task<bool> DeleteEngineerAsync(string externalId, CancellationToken ct = default)
        => await engineers.DeleteEngineerAsync(externalId, ct) > 0;

    public async Task<EngineerDto?> GetEngineerByExternalIdAsync(string externalId, CancellationToken ct = default)
    {
        var engineer = await engineers.GetByExternalIdAsync(externalId, ct);
        return engineer is null ? null : ToDto(engineer);
    }
    private async Task<Location?> ResolveStartLocationAsync(EngineerCsvRowDto raw, Depot depot, CancellationToken ct)
    {
        var lat = ParseCoordinate(raw.StartLatitude);
        var lon = ParseCoordinate(raw.StartLongitude);
        if (lat.HasValue && lon.HasValue)
        {
            return new Location
            {
                RawAddress = string.IsNullOrWhiteSpace(raw.StartAddress) ? depot.Location?.RawAddress ?? string.Empty : raw.StartAddress.Trim(),
                Coordinates = new GeoCoordinate(lat.Value, lon.Value),
            };
        }

        if (!string.IsNullOrWhiteSpace(raw.StartAddress))
        {
            var geo = await geocoder.GeocodeAsync(raw.StartAddress.Trim(), raw.District, ct);
            if (geo != null)
            {
                return new Location
                {
                    RawAddress = raw.StartAddress.Trim(),
                    Coordinates = new GeoCoordinate(geo.Latitude, geo.Longitude),
                };
            }

            return null;
        }

        return depot.Location is null
            ? null
            : new Location
            {
                RawAddress = depot.Location.RawAddress,
                Coordinates = depot.Location.Coordinates,
            };
    }

    private static DateTimeOffset? ParseShift(string value, DateTimeOffset todayUtc)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        var text = value.Trim();

        if (TimeSpan.TryParse(text, CultureInfo.InvariantCulture, out var time))
        {
            var utcMidnight = new DateTime(
                todayUtc.Year, todayUtc.Month, todayUtc.Day,
                0, 0, 0, DateTimeKind.Utc);
            return new DateTimeOffset(utcMidnight + time, TimeSpan.Zero);
        }

        if (DateTimeOffset.TryParse(text, CultureInfo.GetCultureInfo("ru-RU"), DateTimeStyles.None, out var dto))
            return dto.ToUniversalTime();

        if (DateTime.TryParse(text, CultureInfo.GetCultureInfo("ru-RU"), DateTimeStyles.None, out var dt))
            return new DateTimeOffset(dt, TimeSpan.Zero);

        return null;
    }

    private static TransportType? ParseTransport(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        var text = value.Trim();
        if (Enum.TryParse<TransportType>(text, ignoreCase: true, out var direct))
            return direct;

        var lower = text.ToLowerInvariant();
        if (lower.Contains("car") || lower.Contains("авто") || lower.Contains("машин"))
            return TransportType.Car;
        if (lower.Contains("public") || lower.Contains("обществ") || lower.Contains("автобус") || lower.Contains("трамва"))
            return TransportType.PublicTransit;
        if (lower.Contains("bicycle") || lower.Contains("вело"))
            return TransportType.Bicycle;
        if (lower.Contains("pedest") || lower.Contains("foot") || lower.Contains("пеш"))
            return TransportType.Pedestrian;

        return null;
    }

    private static HashSet<SkillType> ParseSkills(string value)
    {
        var result = new HashSet<SkillType>();
        if (!string.IsNullOrWhiteSpace(value))
        {
            foreach (var token in value.Split([',', '|', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                if (TryMapSkill(token, out var skill))
                    result.Add(skill);
            }
        }

        return result.Count > 0 ? result : [SkillType.LocalRepair];
    }

    private static bool TryMapSkill(string token, out SkillType skill)
    {
        if (Enum.TryParse<SkillType>(token, ignoreCase: true, out skill))
            return true;

        var lower = token.ToLowerInvariant();
        if (lower.Contains("монтаж") || lower.Contains("подключ") || lower.Contains("установк") || lower.Contains("install"))
        {
            skill = SkillType.Installation;
            return true;
        }

        if (lower.Contains("авар") || lower.Contains("срочн") || lower.Contains("emergency"))
        {
            skill = SkillType.Emergency;
            return true;
        }

        if (lower.Contains("ремонт") || lower.Contains("локальн") || lower.Contains("local") || lower.Contains("repair"))
        {
            skill = SkillType.LocalRepair;
            return true;
        }

        return false;
    }

    private static double? ParseCoordinate(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        value = value.Replace(',', '.').Trim();
        return double.TryParse(value, NumberStyles.Any, CultureInfo.InvariantCulture, out var result) ? result : null;
    }

    private static int ParseMaxOrders(string value)
        => int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var result) && result > 0
            ? result
            : 15;
}