using Backend.Contracts.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Backend.Api.Infrastructure.Repositories;

public class EngineerRepository(AppDbContext db) : IEngineerRepository
{
    public async Task<Engineer?> GetByIdAsync(Guid id, CancellationToken ct = default)
        => await db.Engineers.AsNoTracking().FirstOrDefaultAsync(e => e.Id == id, ct);

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

    public async Task UpdateAvailabilityAsync(Guid engineerId, bool isAvailable, CancellationToken ct = default)
        => await db.Engineers
            .Where(e => e.Id == engineerId)
            .ExecuteUpdateAsync(setters => setters.SetProperty(e => e.IsAvailable, isAvailable), ct);
}