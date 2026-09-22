using Backend.Contracts.Domain.Enums;

namespace Backend.Contracts.Responses;

public record JobStatusResponseDto(
    Guid JobId,
    JobStatus Status,
    string? ErrorMessage,
    DateTimeOffset CreatedAt
);
