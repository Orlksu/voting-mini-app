namespace Backend.RoutingEngine.Solvers;

/// <summary>
/// Матрицы маршрута: расстояния (км) и время в пути (мин) между точками
/// (старты инженеров + заявки). Диагональ равна нулю.
/// </summary>
public sealed record RouteMatrices(double[,] DistanceKm, long[,] TimeMinutes);

/// <summary>
/// Поставщик матриц «точка-точка». Фаза A: Haversine со средней скоростью.
/// Фаза B: Valhalla (time+dist) с costing по типу транспорта и фолбэком на Haversine.
/// </summary>
public interface IRouteMatrixProvider
{
    Task<RouteMatrices> BuildAsync(
        IReadOnlyList<(double Lat, double Lon)> locations,
        CancellationToken cancellationToken = default);
}