using Backend.Contracts.Domain.Enums;

namespace Backend.Contracts.Responses;

public record EngineerDto(
    Guid Id,
    string ExternalId,
    string Name,
    string District,
    Guid? DepotId,
    string StartAddress,
    double StartLatitude,
    double StartLongitude,
    DateTimeOffset ShiftStart,
    DateTimeOffset ShiftEnd,
    TransportType Transport,
    IReadOnlySet<SkillType> Skills,
    bool IsAvailable,
    int MaxOrdersPerShift);