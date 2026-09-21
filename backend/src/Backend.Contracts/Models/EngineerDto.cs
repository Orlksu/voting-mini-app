using Backend.Contracts.Enums;

namespace Backend.Contracts.Models;

public record EngineerDto(
    string Id,
    string Name,
    List<string> Skills,
    TransportType Transport,
    LocationDto StartLocation,
    TimeWindowDto WorkHours,
    LocationDto? EndLocation = null
);
