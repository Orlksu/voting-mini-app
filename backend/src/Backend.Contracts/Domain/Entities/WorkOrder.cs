using Backend.Contracts.Domain.Enums;
using Backend.Contracts.Domain.ValueObjects;

namespace Backend.Contracts.Domain.Entities;

public class WorkOrder
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public string ExternalId { get; init; } = string.Empty;
    public Guid? DepotId { get; set; }
    public Depot Depot { get; set; } = null!;
    public string District { get; init; } = string.Empty;
    public Location Location { get; set; } = null!;
    public TimeWindow PreferredWindow { get; set; } = null!;
    public TimeSpan ServiceDuration { get; set; }
    public SkillType RequiredSkill { get; set; }
    public TransportType? RequiredTransport { get; set; }
    public OrderPriority Priority { get; set; } = OrderPriority.Normal;
    public OrderStatus Status { get; set; } = OrderStatus.NotDispatched;
    public string TaskType { get; set; } = string.Empty;
    public string? CancellationReason { get; set; }

    /// <summary>
    /// Проверка возможности удаления заявки по бизнес-правилам домена.
    /// Удаление разрешено только для нераспределенных (NotDispatched) или отмененных (Cancelled) заявок.
    /// </summary>
    public bool CanBeDeleted() => Status is OrderStatus.NotDispatched or OrderStatus.Cancelled;

    /// <summary>
    /// Отменяет заявку с указанием причины отмены.
    /// Если заявка уже отменена, операция идемпотентна (причина может обновляться).
    /// Завершенную заявку (Completed) отменить нельзя.
    /// </summary>
    public void Cancel(string? reason)
    {
        if (Status == OrderStatus.Completed)
            throw new InvalidOperationException("Нельзя отменить уже завершенную заявку.");

        Status = OrderStatus.Cancelled;
        CancellationReason = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim();
    }

    /// <summary>
    /// Завершает выполнение заявки.
    /// Если заявка уже завершена, операция идемпотентна.
    /// Отмененную заявку (Cancelled) завершить нельзя.
    /// </summary>
    public void Complete()
    {
        if (Status == OrderStatus.Cancelled)
            throw new InvalidOperationException("Нельзя завершить отмененную заявку.");

        Status = OrderStatus.Completed;
    }
}
