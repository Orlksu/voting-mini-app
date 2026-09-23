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
    {
        var depot = await db.Depots.FirstOrDefaultAsync(d => d.District == district, ct);
        if (depot is null)
        {
            depot = new Depot
            {
                District = district,
                Location = new Location { RawAddress = address, Coordinates = new GeoCoordinate(lat, lon) },
            };
            await db.Depots.AddAsync(depot, ct);
        }
        else if (depot.Location is null
                 || !string.Equals(depot.Location.RawAddress, address, StringComparison.Ordinal)
                 || depot.Location.Coordinates != new GeoCoordinate(lat, lon))
        {
            depot.Location = new Location { RawAddress = address, Coordinates = new GeoCoordinate(lat, lon) };
        }

        await db.SaveChangesAsync(ct);
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