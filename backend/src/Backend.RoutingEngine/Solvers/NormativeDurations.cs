namespace Backend.RoutingEngine.Solvers;

/// <summary>
/// Нормативы длительности работ (без времени на дорогу — дорогу считает матрица/Valhalla).
/// Источник: «Нормативы (2).xlsx»: Базовый норматив − 20 мин «Дорога до клиента/ТКД».
/// </summary>
public static class NormativeDurations
{
    /// <summary>Подключение клиентов Базовая: 60 техн. + 10 документы = 70.</summary>
    public const int InstallationMinutes = 70;

    /// <summary>Аварий на ТКД: 80 техн. + 0 документы = 80 (норматив 100 включает 20 мин дороги).</summary>
    public const int EmergencyMinutes = 80;

    /// <summary>Дозаказ оборудования: 10 техн. + 10 документы = 20.</summary>
    public const int AdditionalEquipmentMinutes = 20;

    /// <summary>Локальная заявка/ремонт у клиента: 30 техн. + 0 документы = 30.</summary>
    public const int RepairMinutes = 30;

    /// <summary>
    /// Сопоставляет TaskType заявки с нормативом. Возвращает null, если тип не распознан —
    /// тогда используется переданная в заявке длительность.
    /// </summary>
    public static int? TryGetDurationMinutes(string? taskType)
    {
        if (string.IsNullOrWhiteSpace(taskType)) return null;

        var t = taskType.ToLowerInvariant();
        if (t.Contains("авар")) return EmergencyMinutes;
        if (t.Contains("подключ")) return InstallationMinutes;
        if (t.Contains("дозаказ")) return AdditionalEquipmentMinutes;
        if (t.Contains("ремонт") || t.Contains("локальн")) return RepairMinutes;
        return null;
    }
}