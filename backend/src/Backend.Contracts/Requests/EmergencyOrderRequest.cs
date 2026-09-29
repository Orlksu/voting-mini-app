namespace Backend.Contracts.Requests;

/// <summary>
/// Запрос на оперативное перепланирование при поступлении аварийной заявки.
/// Система сама определяет текущее состояние инженеров по расписанию плана.
/// </summary>
public record EmergencyOrderRequest(
    /// <summary>Аварийная заявка, которую нужно вписать в план.</summary>
    WorkOrderInputDto UrgentOrder,

    /// <summary>Время события (когда пришла заявка). По умолчанию — текущий момент UTC.</summary>
    DateTimeOffset? EventTime = null
);
