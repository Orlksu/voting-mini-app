using Backend.Contracts.Domain.Entities;
using Backend.Contracts.Requests;
using Backend.Contracts.Responses;

namespace Backend.Api.Services.EngineerService;

public interface IEngineerService
{
    Task<List<EngineerDto>> GetAllAsync(CancellationToken ct = default);
    Task<Engineer> AddAsync(string district, EngineerInputDto engineer, CancellationToken ct = default);
    Task<List<Engineer>> AddRangeAsync(string district, IEnumerable<EngineerInputDto> engineers, CancellationToken ct = default);

    Task<EngineerImportResultDto> ImportEngineersAsync(
        List<EngineerCsvRowDto> rawEngineers,
        IProgress<(int Processed, int Total, string Stage)>? progress = null,
        CancellationToken ct = default);

    Task<EngineerImportResultDto> ImportEngineersFromPayloadAsync(
        byte[] content,
        string fileName,
        IProgress<(int Processed, int Total, string Stage)>? progress = null,
        CancellationToken ct = default);
    Task<bool> DeleteEngineerAsync(string externalId, DateTimeOffset? currentTime = null, CancellationToken ct = default);
    Task<bool> DeleteEngineerByIdentifierAsync(string identifier, DateTimeOffset? currentTime = null, CancellationToken ct = default);
    Task<EngineerDto?> GetEngineerByExternalIdAsync(string externalId, CancellationToken ct = default);
    Task<EngineerDto?> GetEngineerByIdAsync(Guid id, CancellationToken ct = default);
    Task<EngineerDto?> GetEngineerByIdentifierAsync(string identifier, CancellationToken ct = default);
    Task UpdateAvailabilityAsync(Guid engineerId, bool isAvailable, DateTimeOffset? currentTime = null, CancellationToken ct = default);
}