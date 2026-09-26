using Backend.Contracts.Domain.Entities;

namespace Backend.Api.Infrastructure.Repositories;

public interface IEngineerRouteRepository
{
    Task<List<EngineerRoute>> GetByEngineerAsync(Guid engineerId, DateTimeOffset? targetDate = null, CancellationToken ct = default);
}