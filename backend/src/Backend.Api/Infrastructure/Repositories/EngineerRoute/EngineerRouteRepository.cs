using Backend.Contracts.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Backend.Api.Infrastructure.Repositories;

public class EngineerRouteRepository(AppDbContext db) : IEngineerRouteRepository
{
    public async Task<List<EngineerRoute>> GetByEngineerAsync(Guid engineerId, CancellationToken ct = default)
        => await db.EngineerRoutes
            .AsNoTracking()
            .Include(r => r.Stops)
            .Where(r => r.EngineerId == engineerId)
            .ToListAsync(ct);
}