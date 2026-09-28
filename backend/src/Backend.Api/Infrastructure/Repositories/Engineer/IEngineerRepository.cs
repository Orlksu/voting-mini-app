using Backend.Contracts.Domain.Entities;

namespace Backend.Api.Infrastructure.Repositories;

public interface IEngineerRepository
{
    Task<Engineer?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<Engineer?> GetByExternalIdAsync(string id, CancellationToken ct = default);
    Task<List<Engineer>> GetAllAsync(CancellationToken ct = default);
    Task<List<Engineer>> GetAvailableByDepotIdAsync(Guid depotId, CancellationToken ct = default);
    Task<List<Engineer>> GetAvailableByDistrictAsync(string district, CancellationToken ct = default);
    Task<List<Engineer>> GetAvailableEngineersAsync(Guid? depotId = null, string? district = null, CancellationToken ct = default);
    Task AddAsync(Engineer engineer, CancellationToken ct = default);
    Task UpsertRangeAsync(IEnumerable<Engineer> engineers, CancellationToken ct = default);
    Task UpdateAvailabilityAsync(Guid engineerId, bool isAvailable, CancellationToken ct = default);
    Task<int> DeleteEngineerAsync(string externalId, CancellationToken ct = default);
}