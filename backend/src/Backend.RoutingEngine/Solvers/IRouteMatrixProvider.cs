using Backend.Contracts.Domain.Enums;

namespace Backend.RoutingEngine.Solvers;

/// <summary>
/// Матрицы маршрута: расстояние (км) и время в пути (мин) между точками
/// (старты инженеров + заявки). Диагональ равна нулю.
/// Фаза B: матрицы по-бригадные — transit считается в costing транспорта конкретного
/// инженера, поэтому время/расстояние зависят от бригады.
/// </summary>
public sealed record RouteMatrices(
    double[][,] DistanceKm,   // [vehicle][from,to]
    long[][,] TimeMinutes);   // [vehicle][from,to]

/// <summary>
/// Поставщик матриц «точка-точка».
/// Фаза B: Valhalla (time+dist) с costing по типу транспорта и фолбэком на Haversine.
/// locations: старты инженеров, затем точки заявок (как в PlanPreprocessor.BuildLocations);
/// vehicleTransports: транспорты инженеров в том же порядке, что первые locations.Count штук.
/// </summary>
public interface IRouteMatrixProvider
{
    Task<RouteMatrices> BuildAsync(
        IReadOnlyList<(double Lat, double Lon)> locations,
        IReadOnlyList<TransportType> vehicleTransports,
        CancellationToken cancellationToken = default);
}