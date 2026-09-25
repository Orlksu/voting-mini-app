using Backend.Contracts.Domain.Entities;
using Backend.Contracts.Domain.Enums;
using Backend.Contracts.Responses;

namespace Backend.Contracts.Requests;

public record WorkOrderInputDto
{
    public string ExternalId { get; set; } = string.Empty;
    public string? District { get; set; }
    public DepotDto? Depot { get; set; }
    public string Address { get; set; } = string.Empty;
    public double? Latitude { get; set; }
    public double? Longitude { get; set; }
    public DateTimeOffset? WindowStart { get; set; }
    public DateTimeOffset? WindowEnd { get; set; }
    public SkillType RequiredSkill { get; set; }
    public TransportType? RequiredTransport { get; set; }
    public OrderPriority Priority { get; set; }
    public string TaskType { get; set; } = string.Empty;
    public int DurationMinutes { get; set; } = 30;
    /// <summary>Статус заявки. Завершённые/отменённые заявки в планирование не попадают.</summary>
    public OrderStatus Status { get; set; } = OrderStatus.NotDispatched;
}
