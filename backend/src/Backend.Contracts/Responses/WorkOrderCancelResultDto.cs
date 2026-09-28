namespace Backend.Contracts.Responses;

/// <summary>
/// Ответ на отмену заявки.
/// Содержит данные отменённой заявки и, если она была в активном маршруте,
/// идентификатор фоновой задачи автоматического перепланирования.
/// </summary>
public record WorkOrderCancelResultDto(
    /// <summary>Данные отменённой заявки.</summary>
    WorkOrderDto Order,

    /// <summary>
    /// JobId задачи перепланирования, запущенной автоматически.
    /// null — если заявка не была распределена (NotDispatched) или не было активного плана.
    /// </summary>
    Guid? ReplanJobId
);
