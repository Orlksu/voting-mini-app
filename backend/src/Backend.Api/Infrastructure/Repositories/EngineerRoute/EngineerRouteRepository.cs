using Backend.Contracts.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Backend.Api.Infrastructure.Repositories;

public class EngineerRouteRepository(AppDbContext db) : IEngineerRouteRepository
{
    public async Task<List<EngineerRoute>> GetByEngineerAsync(Guid engineerId, CancellationToken ct = default)
        => await (from r in db.EngineerRoutes.AsNoTracking().Include(x => x.Stops)
                  join op in db.OperationResults.AsNoTracking() on r.OperationResultId equals op.Id into opGroup
                  from op in opGroup.DefaultIfEmpty()
                  where r.EngineerId == engineerId
                  orderby op != null ? op.PlanDate : DateTimeOffset.MinValue descending
                  select r).ToListAsync(ct);
}