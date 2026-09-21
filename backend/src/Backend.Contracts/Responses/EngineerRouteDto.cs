namespace Backend.Contracts.Responses;

public record EngineerRouteDto(
    string EngineerId,
    string EngineerName,
    List<RouteStopDto> Stops,
    int TotalTravelTimeMinutes,
    int TotalServiceTimeMinutes,
    double TotalDistanceKm
);
