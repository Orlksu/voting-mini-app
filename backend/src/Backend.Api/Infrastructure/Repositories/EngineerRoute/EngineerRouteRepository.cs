using Backend.Contracts.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Backend.Api.Infrastructure.Repositories;

public class EngineerRouteRepository(AppDbContext db) : IEngineerRouteRepository
{
    public async Task<List<EngineerRoute>> GetByEngineerAsync(Guid engineerId, DateTimeOffset? targetDate = null, CancellationToken ct = default)
    {
        var query = from r in db.EngineerRoutes.AsNoTracking().Include(x => x.Stops)
                    join op in db.OperationResults.AsNoTracking() on r.OperationResultId equals op.Id into opGroup
                    from op in opGroup.DefaultIfEmpty()
                    where r.EngineerId == engineerId
                    select new { Route = r, OperationResult = op };

        if (targetDate.HasValue)
        {
            var msk = targetDate.Value.ToOffset(TimeSpan.FromHours(3));
            var startUtc = new DateTimeOffset(msk.Year, msk.Month, msk.Day, 0, 0, 0, TimeSpan.FromHours(3)).ToUniversalTime();
            var endUtc = startUtc.AddDays(1);
            query = query.Where(x => x.OperationResult != null && x.OperationResult.PlanDate >= startUtc && x.OperationResult.PlanDate < endUtc);
        }

        var ordered = query.OrderByDescending(x => x.OperationResult != null ? x.OperationResult.CreatedAt : x.Route.CreatedAt)
                           .ThenByDescending(x => x.OperationResult != null ? x.OperationResult.PlanDate : DateTimeOffset.MinValue)
                           .Select(x => x.Route);

        return await ordered.ToListAsync(ct);
    }
}