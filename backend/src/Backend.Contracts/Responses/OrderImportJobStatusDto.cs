using Backend.Contracts.Domain.Enums;

namespace Backend.Contracts.Responses;

public record OrderImportJobStatusDto(
    Guid JobId,
    JobStatus Status,
    int TotalItems,
    int ProcessedItems,
    int ProgressPercent,
    string CurrentStage,
    OrderImportResultDto? Result,
    string? ErrorMessage,
    DateTimeOffset CreatedAt
);
