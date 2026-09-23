using Backend.Contracts.Domain.Entities;
using Backend.Contracts.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Backend.Api.Infrastructure.Repositories;

public class WorkOrderRepository(AppDbContext db) : IWorkOrderRepository
{
    public async Task AddRangeAsync(IEnumerable<WorkOrder> orders, CancellationToken ct = default)
    {
        await db.WorkOrders.AddRangeAsync(orders, ct);
        await db.SaveChangesAsync(ct);
    }

    public async Task<List<WorkOrder>> GetNotDispatchedByDepotIdAsync(Guid depotId, CancellationToken ct = default)
        => await db.WorkOrders
            .AsNoTracking()
            .Where(w => w.DepotId == depotId && w.Status == OrderStatus.NotDispatched)
            .ToListAsync(ct);

    public async Task<List<WorkOrder>> GetNotDispatchedByDistrictAsync(string district, CancellationToken ct = default)
        => await db.WorkOrders
            .AsNoTracking()
            .Where(w => w.District == district && w.Status == OrderStatus.NotDispatched)
            .ToListAsync(ct);

    public async Task UpdateStatusesAsync(IEnumerable<Guid> orderIds, OrderStatus newStatus, CancellationToken ct = default)
    {
        var ids = orderIds.ToList();
        await db.WorkOrders
            .Where(w => ids.Contains(w.Id))
            .ExecuteUpdateAsync(setters => setters.SetProperty(w => w.Status, newStatus), ct);
    }
}