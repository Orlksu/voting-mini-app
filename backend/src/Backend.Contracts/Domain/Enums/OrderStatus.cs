namespace Backend.Contracts.Domain.Enums;

public enum OrderStatus
{
    NotDispatched = 0,
    Dispatched = 1,
    InProgress = 2,
    Completed = 3,
    Cancelled = 4
}
