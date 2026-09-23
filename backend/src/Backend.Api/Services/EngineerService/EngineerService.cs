using Backend.Api.Infrastructure.Repositories;
using Backend.Contracts.Domain.Entities;
using Backend.Contracts.Domain.ValueObjects;
using Backend.Contracts.Requests;

namespace Backend.Api.Services.EngineerService;

public class EngineerService(IDepotRepository depots, IEngineerRepository engineers) : IEngineerService
{
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
}