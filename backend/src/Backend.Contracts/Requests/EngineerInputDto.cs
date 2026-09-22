using Backend.Contracts.Domain.Enums;

namespace Backend.Contracts.Requests;

public record EngineerInputDto(
    string ExternalId,
    string Name,
    string StartAddress,
    double? StartLatitude,
    double? StartLongitude,
    DateTimeOffset ShiftStart,
    DateTimeOffset ShiftEnd,
    TransportType Transport,
    List<SkillType> Skills,
    int MaxOrdersPerShift = 15
);
