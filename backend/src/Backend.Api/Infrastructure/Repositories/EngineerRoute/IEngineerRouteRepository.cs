using Backend.Contracts.Domain.Entities;

namespace Backend.Api.Infrastructure.Repositories;

public interface IEngineerRouteRepository
{
    Task<List<EngineerRoute>> GetByEngineerAsync(Guid engineerId, CancellationToken ct = default);
}