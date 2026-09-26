using Backend.Contracts.Domain.Entities;
using Backend.Contracts.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Backend.Api.Infrastructure.Repositories;

public class WorkOrderRepository(AppDbContext db) : IWorkOrderRepository
{
    public async Task AddAsync(WorkOrder order, CancellationToken ct = default)
    {
        try
        {
            await db.WorkOrders.AddAsync(order, ct);
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            throw new InvalidOperationException($"Заявка с таким идентификатором '{order.ExternalId}' в районе '{order.District}' уже существует.");
        }
    }

    public async Task AddRangeAsync(IEnumerable<WorkOrder> orders, CancellationToken ct = default)
    {
        var list = orders.ToList();
        if (list.Count == 0)
            return;

        var existing = await db.WorkOrders
            .Where(w => list.Select(o => o.ExternalId).Contains(w.ExternalId))
            .ToListAsync(ct);
        var existingByKey = existing
            .GroupBy(w => (w.ExternalId, w.District))
            .ToDictionary(g => g.Key, g => g.First());

        var processed = new HashSet<(string, string)>();
        foreach (var order in list)
        {
            var key = (order.ExternalId, order.District);
            if (!processed.Add(key))
                continue;

            if (existingByKey.TryGetValue(key, out var current))
            {
                current.Location = order.Location;
                current.PreferredWindow = order.PreferredWindow;
                current.ServiceDuration = order.ServiceDuration;
                current.RequiredSkill = order.RequiredSkill;
                current.RequiredTransport = order.RequiredTransport;
                current.Priority = order.Priority;
                current.TaskType = order.TaskType;
                current.DepotId = order.DepotId;
            }
            else
            {
                db.WorkOrders.Add(order);
            }
        }

        await db.SaveChangesAsync(ct);
    }

    public async Task<List<WorkOrder>> GetAllAsync(CancellationToken ct = default)
        => await db.WorkOrders.AsNoTracking().ToListAsync(ct);

    public async Task<WorkOrder?> GetByIdAsync(Guid id, CancellationToken ct = default)
        => await db.WorkOrders.AsNoTracking().FirstOrDefaultAsync(w => w.Id == id, ct);

    public async Task<WorkOrder?> GetByExternalIdAsync(string externalId, CancellationToken ct = default)
        => await db.WorkOrders.AsNoTracking().FirstOrDefaultAsync(w => w.ExternalId == externalId, ct);

    public async Task<WorkOrder?> GetByExternalIdAndDistrictAsync(string externalId, string district, CancellationToken ct = default)
        => await db.WorkOrders.AsNoTracking().FirstOrDefaultAsync(w => w.ExternalId == externalId && w.District == district, ct);

    public async Task<List<WorkOrder>> FindByIdentifierAsync(string identifier, string? district = null, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(identifier))
            return [];

        var trimmed = identifier.Trim();
        if (Guid.TryParse(trimmed, out var guid))
        {
            var byId = await db.WorkOrders.FirstOrDefaultAsync(w => w.Id == guid, ct);
            if (byId != null)
                return [byId];
        }

        var query = db.WorkOrders.Where(w => w.ExternalId == trimmed);
        if (!string.IsNullOrWhiteSpace(district))
        {
            var trimmedDistrict = district.Trim();
            query = query.Where(w => w.District == trimmedDistrict);
        }

        return await query.ToListAsync(ct);
    }

    public async Task<List<WorkOrder>> GetByExternalIdsAsync(IEnumerable<string> externalIds, CancellationToken ct = default)
    {
        var ids = externalIds.ToList();
        if (ids.Count == 0)
            return [];

        return await db.WorkOrders
            .AsNoTracking()
            .Where(w => ids.Contains(w.ExternalId))
            .ToListAsync(ct);
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

    public async Task UpdateAsync(WorkOrder order, CancellationToken ct = default)
    {
        if (db.Entry(order).State == EntityState.Detached)
        {
            db.WorkOrders.Update(order);
        }
        await db.SaveChangesAsync(ct);
    }

    public async Task<bool> DeleteAsync(WorkOrder order, CancellationToken ct = default)
    {
        try
        {
            db.WorkOrders.Remove(order);
            await db.SaveChangesAsync(ct);
            return true;
        }
        catch (DbUpdateConcurrencyException)
        {
            return false;
        }
    }
}