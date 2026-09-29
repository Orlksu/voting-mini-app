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

        var externalIds = list
            .Select(o => o.ExternalId.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var existing = await db.WorkOrders
            .Where(w => externalIds.Contains(w.ExternalId))
            .ToListAsync(ct);

        static string MakeKey(string extId, string district) =>
            $"{extId.Trim()}|{district.Trim()}";

        var existingByKey = existing
            .GroupBy(w => MakeKey(w.ExternalId, w.District), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

        var processed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var order in list)
        {
            var key = MakeKey(order.ExternalId, order.District);
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

    public async Task<List<WorkOrder>> GetByIdsAsync(IEnumerable<Guid> ids, CancellationToken ct = default)
    {
        var idList = ids.ToList();
        if (idList.Count == 0)
            return [];

        return await db.WorkOrders
            .AsNoTracking()
            .Where(w => idList.Contains(w.Id))
            .ToListAsync(ct);
    }

    public async Task<WorkOrder?> GetByExternalIdAsync(string externalId, CancellationToken ct = default)
        => await db.WorkOrders.AsNoTracking().FirstOrDefaultAsync(w => w.ExternalId == externalId, ct);

    public async Task<WorkOrder?> GetByExternalIdAndDistrictAsync(string externalId, string district, CancellationToken ct = default)
        => await db.WorkOrders.AsNoTracking().FirstOrDefaultAsync(w => EF.Functions.ILike(w.ExternalId, externalId.Trim()) && EF.Functions.ILike(w.District, district.Trim()), ct);

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

        var query = db.WorkOrders.Where(w => EF.Functions.ILike(w.ExternalId, trimmed));
        if (!string.IsNullOrWhiteSpace(district))
        {
            var trimmedDistrict = district.Trim();
            query = query.Where(w => EF.Functions.ILike(w.District, trimmedDistrict));
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

    public async Task<List<WorkOrder>> GetByExternalIdsAndDistrictsAsync(IEnumerable<(string ExternalId, string? District)> orders, CancellationToken ct = default)
    {
        var list = orders.ToList();
        if (list.Count == 0)
            return [];

        var extIds = list.Select(o => o.ExternalId).Distinct().ToList();
        var candidates = await db.WorkOrders
            .AsNoTracking()
            .Where(w => extIds.Contains(w.ExternalId))
            .ToListAsync(ct);

        var result = new List<WorkOrder>();
        foreach (var (extId, district) in list)
        {
            var match = candidates.FirstOrDefault(w =>
                string.Equals(w.ExternalId, extId, StringComparison.OrdinalIgnoreCase) &&
                (string.IsNullOrWhiteSpace(district) || string.Equals(w.District, district.Trim(), StringComparison.OrdinalIgnoreCase)));
            if (match != null && !result.Any(r => r.Id == match.Id))
            {
                result.Add(match);
            }
        }
        return result;
    }

    public async Task<List<WorkOrder>> GetPendingOrdersForPlanningAsync(
        Guid? depotId = null, 
        string? district = null, 
        DateTimeOffset? targetDate = null, 
        OrderStatus? status = null, 
        CancellationToken ct = default)
    {
        var query = db.WorkOrders
            .AsNoTracking()
            .Include(w => w.Depot)
            .AsQueryable();

        if (status.HasValue)
        {
            query = query.Where(w => w.Status == status.Value);
        }
        else
        {
            query = query.Where(w => w.Status == OrderStatus.NotDispatched || w.Status == OrderStatus.Dispatched);
        }

        if (depotId.HasValue)
        {
            query = query.Where(w => w.DepotId == depotId.Value);
        }

        if (!string.IsNullOrWhiteSpace(district))
        {
            var trimmed = district.Trim();
            query = query.Where(w => EF.Functions.ILike(w.District, trimmed));
        }

        if (targetDate.HasValue)
        {
            var msk = targetDate.Value.ToOffset(TimeSpan.FromHours(3));
            var startUtc = new DateTimeOffset(msk.Year, msk.Month, msk.Day, 0, 0, 0, TimeSpan.FromHours(3)).ToUniversalTime();
            var endUtc = startUtc.AddDays(1);

            query = query.Where(w => w.PreferredWindow.Start >= startUtc && w.PreferredWindow.Start < endUtc);
        }

        return await query.ToListAsync(ct);
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
        if (ids.Count == 0)
            return;

        // Защита от затирания терминальных статусов (Cancelled / Completed) при гонке фонового расчета оптимизации
        var query = db.WorkOrders.Where(w => ids.Contains(w.Id));
        if (newStatus == OrderStatus.Dispatched)
        {
            query = query.Where(w => w.Status == OrderStatus.NotDispatched);
        }
        else if (newStatus == OrderStatus.NotDispatched)
        {
            query = query.Where(w => w.Status != OrderStatus.Cancelled 
                                  && w.Status != OrderStatus.Completed 
                                  && w.Status != OrderStatus.InProgress);
        }

        await query.ExecuteUpdateAsync(setters => setters.SetProperty(w => w.Status, newStatus), ct);
    }

    public async Task ResetOrphanedOrdersAsync(Guid engineerId, DateTimeOffset now, CancellationToken ct = default)
    {
        var activeRoute = await db.EngineerRoutes
            .Where(r => r.EngineerId == engineerId)
            .OrderByDescending(r => r.CreatedAt)
            .FirstOrDefaultAsync(ct);

        if (activeRoute == null) return;

        var stopOrderIds = await db.RouteStops
            .Where(s => s.EngineerRouteId == activeRoute.Id && s.ServiceEnd > now && s.OrderId != null)
            .Select(s => s.OrderId!.Value)
            .ToListAsync(ct);

        if (stopOrderIds.Count > 0)
        {
            await db.WorkOrders
                .Where(w => stopOrderIds.Contains(w.Id)
                         && w.Status != OrderStatus.Cancelled
                         && w.Status != OrderStatus.Completed)
                .ExecuteUpdateAsync(s => s.SetProperty(w => w.Status, OrderStatus.NotDispatched), ct);
        }
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