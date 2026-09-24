namespace Backend.Contracts.Responses;

public record EngineerOrderDto(
    Guid RouteId,
    int Sequence,
    WorkOrderDto Order,
    DateTimeOffset EstimatedArrival,
    DateTimeOffset ServiceStart,
    DateTimeOffset ServiceEnd,
    string AssignmentReason);