using Backend.Api.Infrastructure.Repositories;
using Backend.Contracts.Responses;

namespace Backend.Api.Services.DepotService;

public class DepotService(IDepotRepository depotRepository) : IDepotService
{
    public async Task<List<DepotDto>> GetAllAsync(CancellationToken ct = default)
    {
        var depots = await depotRepository.GetAllAsync(ct);
        return depots.Select(d => new DepotDto(
            d.District,
            d.Location?.RawAddress ?? string.Empty,
            d.Location?.Coordinates.Latitude ?? 0.0,
            d.Location?.Coordinates.Longitude ?? 0.0,
            d.Id
        )).ToList();
    }
}
