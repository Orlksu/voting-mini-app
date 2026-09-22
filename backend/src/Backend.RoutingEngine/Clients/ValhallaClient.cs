using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Backend.Contracts.Domain.ValueObjects;
using Microsoft.Extensions.Logging;

namespace Backend.RoutingEngine.Clients;

public class ValhallaClient : IValhallaClient
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<ValhallaClient> _logger;

    public ValhallaClient(HttpClient httpClient, ILogger<ValhallaClient> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    public async Task<double[,]> GetTimeMatrixAsync(
        IReadOnlyList<Location> locations,
        string costing = "auto",
        CancellationToken cancellationToken = default)
    {
        int n = locations.Count;
        var matrix = new double[n, n];

        if (n <= 1)
        {
            return matrix;
        }

        try
        {
            var coords = locations.Select(l => new { lat = l.Coordinates.Latitude, lon = l.Coordinates.Longitude }).ToArray();
            var payload = new
            {
                sources = coords,
                targets = coords,
                costing = costing
            };

            var response = await _httpClient.PostAsJsonAsync("/sources_to_targets", payload, cancellationToken);
            if (response.IsSuccessStatusCode)
            {
                var json = await response.Content.ReadFromJsonAsync<JsonObject>(cancellationToken: cancellationToken);
                var sourcesToTargets = json?["sources_to_targets"]?.AsArray();

                if (sourcesToTargets != null)
                {
                    for (int i = 0; i < n && i < sourcesToTargets.Count; i++)
                    {
                        var row = sourcesToTargets[i]?.AsArray();
                        if (row == null) continue;

                        for (int j = 0; j < n && j < row.Count; j++)
                        {
                            var element = row[j]?.AsObject();
                            if (element != null && element.TryGetPropertyValue("time", out var timeNode) && timeNode != null)
                            {
                                matrix[i, j] = timeNode.GetValue<double>();
                            }
                        }
                    }

                    _logger.LogInformation("Successfully fetched {N}x{N} time matrix from Valhalla ({Costing})", n, n, costing);
                    return matrix;
                }
            }

            _logger.LogWarning("Valhalla /sources_to_targets returned status {StatusCode}, falling back to Haversine", response.StatusCode);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error querying Valhalla, falling back to Haversine distance matrix");
        }

        // Fallback: Haversine distance with realistic city speeds
        double speedKmH = costing == "pedestrian" ? 5.0 : 30.0;
        double tortuosity = costing == "pedestrian" ? 1.2 : 1.35;

        for (int i = 0; i < n; i++)
        {
            for (int j = 0; j < n; j++)
            {
                if (i == j)
                {
                    matrix[i, j] = 0;
                    continue;
                }

                double distKm = CalculateHaversineKm(locations[i], locations[j]) * tortuosity;
                double travelHours = distKm / speedKmH;
                matrix[i, j] = travelHours * 3600.0; // в секундах
            }
        }

        return matrix;
    }

    public async Task<string?> GetRouteGeoJsonAsync(
        IReadOnlyList<Location> stops,
        string costing = "auto",
        CancellationToken cancellationToken = default)
    {
        if (stops.Count < 2)
        {
            return null;
        }

        try
        {
            var coords = stops.Select(s => new { lat = s.Coordinates.Latitude, lon = s.Coordinates.Longitude }).ToArray();
            var payload = new
            {
                locations = coords,
                costing = costing,
                format = "osrm"
            };

            var response = await _httpClient.PostAsJsonAsync("/route", payload, cancellationToken);
            if (response.IsSuccessStatusCode)
            {
                return await response.Content.ReadAsStringAsync(cancellationToken);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to get route geometry from Valhalla");
        }

        return null;
    }

    private static double CalculateHaversineKm(Location loc1, Location loc2)
    {
        const double R = 6371.0; // Радиус Земли в км
        double dLat = DegreesToRadians(loc2.Coordinates.Latitude - loc1.Coordinates.Latitude);
        double dLon = DegreesToRadians(loc2.Coordinates.Longitude - loc1.Coordinates.Longitude);

        double a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2) +
                   Math.Cos(DegreesToRadians(loc1.Coordinates.Latitude)) * Math.Cos(DegreesToRadians(loc2.Coordinates.Latitude)) *
                   Math.Sin(dLon / 2) * Math.Sin(dLon / 2);

        double c = 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));
        return R * c;
    }

    private static double DegreesToRadians(double degrees) => degrees * Math.PI / 180.0;
}
