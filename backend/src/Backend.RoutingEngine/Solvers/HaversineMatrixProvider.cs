using Backend.Contracts.Domain.Enums;

namespace Backend.RoutingEngine.Solvers;

/// <summary>
/// Haversine-матрицы: расстояние по прямой (км), время из средней скорости (мин).
/// Фолбэк Фазы B, когда Valhalla недоступна. Матрица одинаковая для всех бригад
/// (Haversine не зависит от транспорта), поэтому базовая матрица клонируется по-bridged.
/// </summary>
public sealed class HaversineMatrixProvider : IRouteMatrixProvider
{
    private const double EarthRadiusKm = 6371.0;

    public Task<RouteMatrices> BuildAsync(
        IReadOnlyList<(double Lat, double Lon)> locations,
        IReadOnlyList<TransportType> vehicleTransports,
        CancellationToken cancellationToken = default)
    {
        int count = locations.Count;
        int vehicles = vehicleTransports.Count;

        var baseDistance = new double[count, count];

        for (int i = 0; i < count; i++)
        {
            for (int j = 0; j < count; j++)
            {
                if (i == j) continue;

                cancellationToken.ThrowIfCancellationRequested();

                double d = HaversineKm(locations[i].Lat, locations[i].Lon, locations[j].Lat, locations[j].Lon);
                baseDistance[i, j] = Math.Round(d, 2);
            }
        }

        var distances = new double[vehicles][,];
        var times = new long[vehicles][,];

        for (int v = 0; v < vehicles; v++)
        {
            distances[v] = (double[,])baseDistance.Clone();

            var vTime = new long[count, count];
            double speedKmh = GetAverageSpeedKmh(vehicleTransports[v]);

            for (int i = 0; i < count; i++)
            {
                for (int j = 0; j < count; j++)
                {
                    if (i == j) continue;

                    double dist = baseDistance[i, j];
                    vTime[i, j] = (long)Math.Ceiling(dist / speedKmh * 60.0);
                }
            }

            times[v] = vTime;
        }

        return Task.FromResult(new RouteMatrices(distances, times));
    }

    private static double GetAverageSpeedKmh(TransportType transport) => transport switch
    {
        TransportType.Car => 28.0,
        TransportType.PublicTransit => 18.0,
        TransportType.Bicycle => 14.0,
        TransportType.Pedestrian => 4.5,
        _ => 25.0
    };

    private static double HaversineKm(double lat1, double lon1, double lat2, double lon2)
    {
        double dLat = ToRadians(lat2 - lat1);
        double dLon = ToRadians(lon2 - lon1);

        double a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2) +
                   Math.Cos(ToRadians(lat1)) * Math.Cos(ToRadians(lat2)) *
                   Math.Sin(dLon / 2) * Math.Sin(dLon / 2);

        return EarthRadiusKm * 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));
    }

    private static double ToRadians(double value) => Math.PI / 180.0 * value;
}