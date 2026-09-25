using Backend.Contracts.Domain.Entities;
using Backend.Contracts.Domain.Enums;

namespace Backend.Api.Infrastructure.Repositories;

public interface IWorkOrderRepository
{
    Task AddAsync(WorkOrder order, CancellationToken ct = default);
    Task AddRangeAsync(IEnumerable<WorkOrder> orders, CancellationToken ct = default);
    Task<List<WorkOrder>> GetAllAsync(CancellationToken ct = default);
    Task<WorkOrder?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<WorkOrder?> GetByExternalIdAsync(string externalId, CancellationToken ct = default);
    Task<WorkOrder?> GetByExternalIdAndDistrictAsync(string externalId, string district, CancellationToken ct = default);
    Task<WorkOrder?> GetByIdentifierAsync(string identifier, string? district = null, CancellationToken ct = default);
    Task<List<WorkOrder>> GetByExternalIdsAsync(IEnumerable<string> externalIds, CancellationToken ct = default);
    Task<List<WorkOrder>> GetNotDispatchedByDepotIdAsync(Guid depotId, CancellationToken ct = default);
    Task<List<WorkOrder>> GetNotDispatchedByDistrictAsync(string district, CancellationToken ct = default);
    Task UpdateStatusesAsync(IEnumerable<Guid> orderIds, OrderStatus newStatus, CancellationToken ct = default);
    Task<bool> DeleteByIdentifierAsync(string identifier, CancellationToken ct = default);
}