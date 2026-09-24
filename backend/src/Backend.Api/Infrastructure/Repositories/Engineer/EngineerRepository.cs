using Backend.Contracts.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Backend.Api.Infrastructure.Repositories;

public class EngineerRepository(AppDbContext db) : IEngineerRepository
{
    public async Task<Engineer?> GetByIdAsync(Guid id, CancellationToken ct = default)
        => await db.Engineers.AsNoTracking().FirstOrDefaultAsync(e => e.Id == id, ct);

    public async Task<List<Engineer>> GetAllAsync(CancellationToken ct = default)
        => await db.Engineers.AsNoTracking().ToListAsync(ct);

    public async Task<List<Engineer>> GetAvailableByDepotIdAsync(Guid depotId, CancellationToken ct = default)
        => await db.Engineers
            .AsNoTracking()
            .Where(e => e.DepotId == depotId && e.IsAvailable)
            .ToListAsync(ct);

    public async Task<List<Engineer>> GetAvailableByDistrictAsync(string district, CancellationToken ct = default)
        => await db.Engineers
            .AsNoTracking()
            .Where(e => e.District == district && e.IsAvailable)
            .ToListAsync(ct);

    public async Task AddAsync(Engineer engineer, CancellationToken ct = default)
    {
        await db.Engineers.AddAsync(engineer, ct);
        await db.SaveChangesAsync(ct);
    }

    public async Task UpsertRangeAsync(IEnumerable<Engineer> engineers, CancellationToken ct = default)
    {
        var list = engineers.ToList();
        if (list.Count == 0)
            return;

        var existing = await db.Engineers
            .Where(e => list.Select(x => x.ExternalId).Contains(e.ExternalId))
            .ToListAsync(ct);
        var existingByExternalId = existing.ToDictionary(e => e.ExternalId);

        var processed = new HashSet<string>();
        foreach (var engineer in list)
        {
            if (!processed.Add(engineer.ExternalId))
                continue;

            if (existingByExternalId.TryGetValue(engineer.ExternalId, out var current))
            {
                current.Name = engineer.Name;
                current.District = engineer.District;
                current.DepotId = engineer.DepotId;
                current.StartLocation = engineer.StartLocation;
                current.Shift = engineer.Shift;
                current.Transport = engineer.Transport;
                current.Skills = engineer.Skills;
                current.IsAvailable = engineer.IsAvailable;
                current.MaxOrdersPerShift = engineer.MaxOrdersPerShift;
            }
            else
            {
                db.Engineers.Add(engineer);
            }
        }

        await db.SaveChangesAsync(ct);
    }

    public async Task UpdateAvailabilityAsync(Guid engineerId, bool isAvailable, CancellationToken ct = default)
        => await db.Engineers
            .Where(e => e.Id == engineerId)
            .ExecuteUpdateAsync(setters => setters.SetProperty(e => e.IsAvailable, isAvailable), ct);
}