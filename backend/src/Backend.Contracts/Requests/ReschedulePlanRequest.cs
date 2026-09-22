using Backend.Contracts.Domain.Enums;

namespace Backend.Contracts.Requests;

public record ReschedulePlanRequest(
    DateTimeOffset EventTime,
    RescheduleEventType EventType,
    List<EngineerLiveStateDto> EngineersStates,
    List<WorkOrderInputDto> RemainingOrders,
    WorkOrderInputDto? NewUrgentOrder,
    string? CancelledOrderExternalId,
    string? UnavailableEngineerExternalId
);
