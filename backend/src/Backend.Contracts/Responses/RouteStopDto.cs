using Backend.Contracts.Domain.Enums;

namespace Backend.Contracts.Responses;

public record RouteStopDto(
    int Sequence,
    string ExternalOrderId,
    string Address,
    double Latitude,
    double Longitude,
    DateTimeOffset EstimatedArrival,
    DateTimeOffset ServiceStart,
    DateTimeOffset ServiceEnd,
    int TravelDurationMinutes,
    double DistanceFromPrevKm,
    string TaskType,
    OrderPriority Priority,
    string AssignmentReason,
    OrderStatus Status = OrderStatus.Dispatched,
    Guid? OrderId = null
);
