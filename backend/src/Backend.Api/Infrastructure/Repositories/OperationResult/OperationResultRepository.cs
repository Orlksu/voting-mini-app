using Backend.Contracts.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Backend.Api.Infrastructure.Repositories;

public class OperationResultRepository(AppDbContext db) : IOperationResultRepository
{
    public async Task SaveResultAsync(OperationResult result, CancellationToken ct = default)
    {
        await db.OperationResults.AddAsync(result, ct);
        await db.SaveChangesAsync(ct);
    }

    public async Task<OperationResult?> GetByIdWithDetailsAsync(Guid id, CancellationToken ct = default)
        => await db.OperationResults
            .AsNoTracking()
            .Include(x => x.Routes)
            .ThenInclude(r => r.Stops)
            .Include(x => x.UnassignedOrders)
            .FirstOrDefaultAsync(x => x.Id == id, ct);

    public async Task<OperationResult?> GetByJobIdWithDetailsAsync(Guid jobId, CancellationToken ct = default)
        => await db.OperationResults
            .AsNoTracking()
            .Include(x => x.Routes)
            .ThenInclude(r => r.Stops)
            .Include(x => x.UnassignedOrders)
            .FirstOrDefaultAsync(x => x.JobId == jobId, ct);

    public async Task<List<OperationResult>> GetHistoryByDateAsync(DateTimeOffset date, CancellationToken ct = default)
    {
        var start = new DateTimeOffset(date.Year, date.Month, date.Day, 0, 0, 0, date.Offset).ToUniversalTime();
        var end = start.AddDays(1);

        return await db.OperationResults
            .AsNoTracking()
            .Where(x => x.PlanDate >= start && x.PlanDate < end)
            .ToListAsync(ct);
    }
}