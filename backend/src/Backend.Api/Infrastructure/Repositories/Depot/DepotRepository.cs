using Backend.Contracts.Domain.Entities;
using Backend.Contracts.Domain.ValueObjects;
using Microsoft.EntityFrameworkCore;

namespace Backend.Api.Infrastructure.Repositories;

public class DepotRepository(AppDbContext db) : IDepotRepository
{
    public async Task<Depot?> GetByIdAsync(Guid id, CancellationToken ct = default)
        => await db.Depots.AsNoTracking().FirstOrDefaultAsync(d => d.Id == id, ct);

    public async Task<Depot?> GetByDistrictAsync(string district, CancellationToken ct = default)
        => await db.Depots.AsNoTracking().FirstOrDefaultAsync(d => d.District == district, ct);

    public async Task<Depot> GetOrCreateAsync(string district, string address, double lat, double lon, CancellationToken ct = default)
        => await GetOrCreateByLocationAsync(address, lat, lon, district, ct);

    public async Task<Depot> GetOrCreateByLocationAsync(
        string address,
        double lat,
        double lon,
        string fallbackDistrict,
        CancellationToken ct = default)
    {
        var hasCoords = Math.Abs(lat) > 0.0001 || Math.Abs(lon) > 0.0001;
        var hasAddress = !string.IsNullOrWhiteSpace(address);
        
        var depot = await db.Depots.FirstOrDefaultAsync(d =>
            (hasAddress && d.Location != null && d.Location.RawAddress == address) ||
            (hasCoords && d.Location != null && Math.Abs(d.Location.Coordinates.Latitude - lat) < 0.001
                                             && Math.Abs(d.Location.Coordinates.Longitude - lon) < 0.001), ct);

        if (depot is null)
        {
            // Если в БД есть запись по fallbackDistrict — обновляем её
            depot = await db.Depots.FirstOrDefaultAsync(d => d.District == fallbackDistrict, ct);
            if (depot != null)
            {
                if (hasAddress || hasCoords)
                {
                    depot.Location = new Location { RawAddress = address, Coordinates = new GeoCoordinate(lat, lon) };
                }
            }
            else
            {
                depot = new Depot
                {
                    District = fallbackDistrict,
                    Location = (hasAddress || hasCoords)
                        ? new Location { RawAddress = address, Coordinates = new GeoCoordinate(lat, lon) }
                        : null
                };
                await db.Depots.AddAsync(depot, ct);
            }
            await db.SaveChangesAsync(ct);
        }

        return depot;
    }

    public async Task<Depot> EnsureExistsAsync(string district, CancellationToken ct = default)
    {
        var depot = await db.Depots.FirstOrDefaultAsync(d => d.District == district, ct);
        if (depot is null)
        {
            depot = new Depot { District = district };
            await db.Depots.AddAsync(depot, ct);
            await db.SaveChangesAsync(ct);
        }

        return depot;
    }
}