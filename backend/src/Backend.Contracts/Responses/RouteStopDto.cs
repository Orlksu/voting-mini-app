using Backend.Contracts.Models;

namespace Backend.Contracts.Responses;

public record RouteStopDto(
    int StopOrder,
    string? OrderId,
    LocationDto Location,
    TimeOnly EstimatedArrival,
    TimeOnly ServiceStart,
    TimeOnly ServiceEnd,
    TimeOnly EstimatedDeparture,
    int TravelDurationMinutes,
    double DistanceKm,
    bool IsLate = false
);
