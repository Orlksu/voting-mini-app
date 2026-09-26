using Backend.Contracts.Responses;

namespace Backend.Api.Services.DepotService;

public interface IDepotService
{
    Task<List<DepotDto>> GetAllAsync(CancellationToken ct = default);
}
