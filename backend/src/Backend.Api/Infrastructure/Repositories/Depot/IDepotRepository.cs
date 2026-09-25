using Backend.Contracts.Domain.Entities;

namespace Backend.Api.Infrastructure.Repositories;

public interface IDepotRepository
{
    Task<List<Depot>> GetAllAsync(CancellationToken ct = default);
    Task<Depot?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<Depot?> GetByDistrictAsync(string district, CancellationToken ct = default);
    Task<Depot> GetOrCreateAsync(string district, string address, double lat, double lon, CancellationToken ct = default);
    Task<Depot> GetOrCreateByLocationAsync(string address, double lat, double lon, string fallbackDistrict, CancellationToken ct = default);
    Task<Depot> EnsureExistsAsync(string district, CancellationToken ct = default);
}