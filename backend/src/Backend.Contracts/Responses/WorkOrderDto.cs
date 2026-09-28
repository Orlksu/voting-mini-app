using Backend.Contracts.Domain.Enums;

namespace Backend.Contracts.Responses;

public record WorkOrderDto(
    Guid Id,
    string ExternalId,
    string District,
    string Address,
    double Latitude,
    double Longitude,
    DateTimeOffset WindowStart,
    DateTimeOffset WindowEnd,
    int ServiceDurationMinutes,
    SkillType RequiredSkill,
    TransportType? RequiredTransport,
    OrderPriority Priority,
    OrderStatus Status,
    string TaskType,
    Guid? DepotId,
    string? CancellationReason = null);