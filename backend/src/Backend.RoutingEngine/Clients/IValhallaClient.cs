using Backend.Contracts.Domain.ValueObjects;

namespace Backend.RoutingEngine.Clients;

public interface IValhallaClient
{
    /// <summary>
    /// Расчет матрицы времени (секунды) и расстояния (метры).
    /// Costing: "auto" (автомобиль) или "pedestrian" (пешеход)
    /// </summary>
    Task<double[,]> GetTimeMatrixAsync(
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
