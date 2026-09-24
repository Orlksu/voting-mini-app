using Backend.Contracts.Domain.Entities;
using Backend.Contracts.Requests;
using Backend.Contracts.Responses;

namespace Backend.Api.Services.EngineerService;

public interface IEngineerService
{
    Task<List<EngineerDto>> GetAllAsync(CancellationToken ct = default);
    Task<Engineer> AddAsync(string district, EngineerInputDto engineer, CancellationToken ct = default);
    Task<List<Engineer>> AddRangeAsync(string district, IEnumerable<EngineerInputDto> engineers, CancellationToken ct = default);

    Task<EngineerImportResultDto> ImportEngineersAsync(List<EngineerCsvRowDto> rawEngineers,
        CancellationToken ct = default);
    Task<bool> DeleteEngineerAsync(string externalId, CancellationToken ct = default);
    Task<EngineerDto?> GetEngineerByExternalIdAsync(string externalId, CancellationToken ct = default);
}