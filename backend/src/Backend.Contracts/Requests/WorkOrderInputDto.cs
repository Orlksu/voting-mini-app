using Backend.Contracts.Domain.Enums;

namespace Backend.Contracts.Requests;

public record WorkOrderInputDto(
    string ExternalId,
    string Address,
    double? Latitude,
    double? Longitude,
    DateTimeOffset WindowStart,
    DateTimeOffset WindowEnd,
    SkillType RequiredSkill,
    TransportType? RequiredTransport,
    OrderPriority Priority,
    string TaskType,
    int DurationMinutes
);
