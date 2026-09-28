using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Backend.Contracts.Domain.ValueObjects;
using Microsoft.Extensions.Logging;

namespace Backend.RoutingEngine.Clients;

public class ValhallaClient : IValhallaClient
{
    private const double UnreachableSeconds = 24 * 3600.0;

    private readonly HttpClient _httpClient;
    private readonly ILogger<ValhallaClient> _logger;

    public ValhallaClient(HttpClient httpClient, ILogger<ValhallaClient> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    public async Task<ValhallaMatrix> GetMatrixAsync(
        IReadOnlyList<Location> locations,
        string costing = "auto",
        CancellationToken cancellationToken = default)
    {
        int n = locations.Count;
        if (n <= 1)
        {
            return new ValhallaMatrix(new double[n, n], new double[n, n]);
        }

        ValhallaMatrix? matrix = await TryGetMatrixAsync(locations, costing, cancellationToken);
        if (matrix != null)
        {
            return matrix;
        }

        _logger.LogWarning("Valhalla matrix unavailable for {Costing}; falling back to Haversine distance matrix", costing);

        // Fallback: Haversine distance with realistic city speeds/tortuosity by costing
        double speedKmH = costing == "pedestrian" ? 5.0 : 30.0;
        double tortuosity = costing == "pedestrian" ? 1.2 : 1.35;

        var distance = new double[n, n];
        var time = new double[n, n];
        for (int i = 0; i < n; i++)
        {
            for (int j = 0; j < n; j++)
            {
                if (i == j)
                {
                    continue;
                }

                double distKm = CalculateHaversineKm(locations[i], locations[j]) * tortuosity;
                distance[i, j] = distKm * 1000.0;
                time[i, j] = distKm / speedKmH * 3600.0; // в секундах
            }
        }

        return new ValhallaMatrix(time, distance);
    }

    public async Task<ValhallaMatrix?> TryGetMatrixAsync(
        IReadOnlyList<Location> locations,
        string costing = "auto",
        CancellationToken cancellationToken = default)
    {
        int n = locations.Count;
        if (n <= 1)
        {
            return new ValhallaMatrix(new double[n, n], new double[n, n]);
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
                    var time = new double[n, n];
                    var distance = new double[n, n];

                    for (int i = 0; i < n && i < sourcesToTargets.Count; i++)
                    {
                        var row = sourcesToTargets[i]?.AsArray();
                        if (row == null)
                        {
                            continue;
                        }

                        for (int j = 0; j < n && j < row.Count; j++)
                        {
                            var element = row[j]?.AsObject();
                            if (element == null)
                            {
                                continue;
                            }

                            time[i, j] = element.TryGetPropertyValue("time", out var timeNode) && timeNode != null
                                ? timeNode.GetValue<double>()
                                : UnreachableSeconds;

                            distance[i, j] = element.TryGetPropertyValue("distance", out var distNode) && distNode != null
                                ? distNode.GetValue<double>()
                                : 0;
                        }
                    }

                    _logger.LogInformation("Successfully fetched {N}x{N} matrix from Valhalla ({Costing})", n, n, costing);
                    return new ValhallaMatrix(time, distance);
                }

                _logger.LogWarning("Valhalla /sources_to_targets returned unexpected response shape for costing {Costing}", costing);
            }
            else
            {
                _logger.LogWarning("Valhalla /sources_to_targets returned status {StatusCode} for costing {Costing}", response.StatusCode, costing);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error querying Valhalla for costing {Costing}", costing);
        }

        return null;
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
            _logger.LogWarning(ex, "Failed to get route geometry from Valhalla, trying OSRM fallback");
        }

        // Резервный геосервер дорожной сети (OSRM) для построения точных дорожных полилиний
        try
        {
            string profile = costing switch
            {
                "pedestrian" => "walking",
                "bicycle" => "cycling",
                _ => "driving"
            };

            var coordsStr = string.Join(";", stops.Select(s =>
                string.Create(CultureInfo.InvariantCulture, $"{s.Coordinates.Longitude:F6},{s.Coordinates.Latitude:F6}")));

            var handler = new HttpClientHandler
            {
                ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator
            };
            using var osrmClient = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(5) };
            osrmClient.DefaultRequestHeaders.UserAgent.ParseAdd("LCT-Routing-Engine/1.0");

            var osrmUrl = $"https://router.project-osrm.org/route/v1/{profile}/{coordsStr}?overview=full&geometries=polyline";
            var osrmResp = await osrmClient.GetAsync(osrmUrl, cancellationToken);
            if (osrmResp.IsSuccessStatusCode)
            {
                return await osrmResp.Content.ReadAsStringAsync(cancellationToken);
            }
        }
        catch (Exception osrmEx)
        {
            _logger.LogWarning(osrmEx, "OSRM road geometry fallback failed, synthesizing polyline from stops");
        }

        // Автономный fallback: синтезируем валидный ответ с закодированной полилинией
        try
        {
            string poly = EncodePolyline(stops);
            return $"{{\"code\":\"Ok\",\"routes\":[{{\"geometry\":\"{poly}\",\"weight_name\":\"routability\"}}]}}";
        }
        catch
        {
            return null;
        }
    }

    private static string EncodePolyline(IReadOnlyList<Location> locations)
    {
        var str = new System.Text.StringBuilder();
        int prevLat = 0;
        int prevLon = 0;

        foreach (var loc in locations)
        {
            int lat = (int)Math.Round(loc.Coordinates.Latitude * 1e5);
            int lon = (int)Math.Round(loc.Coordinates.Longitude * 1e5);

            EncodeValue(lat - prevLat, str);
            EncodeValue(lon - prevLon, str);

            prevLat = lat;
            prevLon = lon;
        }

        return str.ToString();

        static void EncodeValue(int val, System.Text.StringBuilder sb)
        {
            int v = val < 0 ? ~(val << 1) : (val << 1);
            while (v >= 0x20)
            {
                sb.Append((char)((0x20 | (v & 0x1f)) + 63));
                v >>= 5;
            }
            sb.Append((char)(v + 63));
        }
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