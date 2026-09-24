using Backend.Contracts.Domain.Enums;

namespace Backend.Contracts.Domain.Entities;

public class UnassignedOrder
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public Guid OperationResultId { get; set; }
    public string ExternalOrderId { get; set; } = string.Empty;
    public UnassignedReasonCode ReasonCode { get; set; }
    public string ReasonDescription { get; set; } = string.Empty;
}
