using Backend.Contracts.Domain.Entities;

namespace Backend.Api.Infrastructure.Repositories;

public interface IOperationResultRepository
{
    Task SaveResultAsync(OperationResult result, CancellationToken ct = default);
    Task<OperationResult?> GetByIdWithDetailsAsync(Guid id, CancellationToken ct = default);
    Task<OperationResult?> GetByJobIdWithDetailsAsync(Guid jobId, CancellationToken ct = default);
    Task<List<OperationResult>> GetHistoryByDateAsync(DateTimeOffset date, CancellationToken ct = default);
}