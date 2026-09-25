using Backend.Contracts.Domain.ValueObjects;

namespace Backend.RoutingEngine.Clients;

/// <summary>Матрица Valhalla: время (секунды) и расстояние (метры) по всем парам точек.</summary>
public sealed record ValhallaMatrix(double[,] TimeSeconds, double[,] DistanceMeters);

public interface IValhallaClient
{
    /// <summary>
    /// Матрица времени (секунды) и расстояния (метры) между всеми парами точек.
    /// Costing: "auto" (автомобиль), "transit" (общественный транспорт), "bicycle", "pedestrian".
    /// При недоступности Valhalla возвращается матрица, посчитанная по Haversine.
    /// </summary>
    Task<ValhallaMatrix> GetMatrixAsync(
        IReadOnlyList<Location> locations,
        string costing = "auto",
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Прямой запрос матрицы у Valhalla без Haversine-фолбэка.
    /// Возвращает null, если Valhalla недоступна или ответила ошибкой (например,
    /// costing "transit" отсутствует в графе — нет GTFS) — вызывающий решает, как деградировать.
    /// </summary>
    Task<ValhallaMatrix?> TryGetMatrixAsync(
        IReadOnlyList<Location> locations,
        string costing = "auto",
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Получение детальной геометрии маршрута (GeoJSON) для отображения на фронтенде
    /// </summary>
    Task<string?> GetRouteGeoJsonAsync(
        IReadOnlyList<Location> stops,
        string costing = "auto",
        CancellationToken cancellationToken = default);
}