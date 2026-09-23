using Backend.Contracts.Domain.Entities;
using Backend.Contracts.Requests;

namespace Backend.Api.Services.EngineerService;

public interface IEngineerService
{
    Task<Engineer> AddAsync(string district, EngineerInputDto engineer, CancellationToken ct = default);
    Task<List<Engineer>> AddRangeAsync(string district, IEnumerable<EngineerInputDto> engineers, CancellationToken ct = default);
}