namespace Backend.RoutingEngine.Solvers;

/// <summary>
/// Haversine-матрицы: расстояние по прямой (км), время из средней скорости (мин).
/// Заглушка для Фазы A; во Фазе B заменяется/дополняется Valhalla.
/// </summary>
public sealed class HaversineMatrixProvider : IRouteMatrixProvider
{
    private const double AverageSpeedKmh = 30.0;

    private const double EarthRadiusKm = 6371.0;

    public Task<RouteMatrices> BuildAsync(
        IReadOnlyList<(double Lat, double Lon)> locations,
        CancellationToken cancellationToken = default)
    {
        int count = locations.Count;
        var distance = new double[count, count];
        var time = new long[count, count];

        for (int i = 0; i < count; i++)
        {
            for (int j = 0; j < count; j++)
            {
                if (i == j) continue;

                cancellationToken.ThrowIfCancellationRequested();

                double d = HaversineKm(locations[i].Lat, locations[i].Lon, locations[j].Lat, locations[j].Lon);
                distance[i, j] = Math.Round(d, 2);
                time[i, j] = (long)Math.Ceiling(d / AverageSpeedKmh * 60.0);
            }
        }

        return Task.FromResult(new RouteMatrices(distance, time));
    }

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