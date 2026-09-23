using Backend.Contracts.Domain.ValueObjects;

namespace Backend.Contracts.Domain.Entities;

public class OperationResult
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public Guid JobId { get; set; }
    public DateTimeOffset PlanDate { get; set; }
    public PlanMetrics Metrics { get; set; } = null!;
    public List<EngineerRoute> Routes { get; set; } = [];
    public List<UnassignedOrder> UnassignedOrders { get; set; } = [];
}