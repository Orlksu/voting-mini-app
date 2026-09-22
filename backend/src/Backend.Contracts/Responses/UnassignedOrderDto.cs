using Backend.Contracts.Domain.Enums;

namespace Backend.Contracts.Responses;

public record UnassignedOrderDto(
    string ExternalOrderId,
    string Address,
    UnassignedReasonCode ReasonCode,
    string ReasonDescription
);
