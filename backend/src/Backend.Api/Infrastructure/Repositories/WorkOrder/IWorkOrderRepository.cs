using Backend.Contracts.Domain.Entities;
using Backend.Contracts.Domain.Enums;

namespace Backend.Api.Infrastructure.Repositories;

public interface IWorkOrderRepository
{
    Task AddRangeAsync(IEnumerable<WorkOrder> orders, CancellationToken ct = default);
    Task<List<WorkOrder>> GetNotDispatchedByDepotIdAsync(Guid depotId, CancellationToken ct = default);
    Task<List<WorkOrder>> GetNotDispatchedByDistrictAsync(string district, CancellationToken ct = default);
    Task UpdateStatusesAsync(IEnumerable<Guid> orderIds, OrderStatus newStatus, CancellationToken ct = default);
}