using Backend.Contracts.Domain.Enums;

namespace Backend.Contracts.Responses;

public record EngineerRouteDto(
    string EngineerExternalId,
    string EngineerName,
    TransportType Transport,
    int TotalOrders,
    int ActiveOrdersCount,
    int CompletedOrdersCount,
    int CancelledOrdersCount,
    double TotalDistanceKm,
    double BaselineDistanceKm,
    int TotalTravelMinutes,
    List<RouteStopDto> Stops,
    string? EncodedPolyline,
    string? StartAddress = null,
    double? StartLatitude = null,
    double? StartLongitude = null
);
