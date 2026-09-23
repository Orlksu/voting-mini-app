using Backend.Contracts.Domain.Entities;

namespace Backend.Api.Infrastructure.Repositories;

public interface IDepotRepository
{
    Task<Depot?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<Depot?> GetByDistrictAsync(string district, CancellationToken ct = default);
    Task<Depot> GetOrCreateAsync(string district, string address, double lat, double lon, CancellationToken ct = default);
} 