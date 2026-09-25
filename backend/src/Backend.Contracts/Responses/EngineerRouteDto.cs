using Backend.Contracts.Domain.Enums;

namespace Backend.Contracts.Responses;

public record EngineerRouteDto(
    string EngineerExternalId,
    string EngineerName,
    TransportType Transport,
    int TotalOrders,
    double TotalDistanceKm,
    double BaselineDistanceKm,
    int TotalTravelMinutes,
    List<RouteStopDto> Stops,
    string? EncodedPolyline,
    List<BreakStopDto> BreakStops,
    string? StartAddress = null,
    double? StartLatitude = null,
    double? StartLongitude = null
);
