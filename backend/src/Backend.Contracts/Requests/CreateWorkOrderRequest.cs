using Backend.Contracts.Domain.Enums;

namespace Backend.Contracts.Requests;

public record CreateWorkOrderRequest(
    string? ExternalId,
    string? District,
    Guid? DepotId,
    string Address,
    double? Latitude,
    double? Longitude,
    DateTimeOffset? WindowStart,
    DateTimeOffset? WindowEnd,
    SkillType RequiredSkill,
    TransportType? RequiredTransport,
    OrderPriority Priority = OrderPriority.Normal,
    string? TaskType = null,
    int DurationMinutes = 30
);
