using Backend.Contracts.Domain.Enums;
using Backend.Contracts.Domain.ValueObjects;
using Backend.Contracts.Interfaces;
using Backend.Contracts.Requests;
using Backend.Contracts.Responses;
using Backend.RoutingEngine.Clients;
using Backend.RoutingEngine.Solvers;
using Microsoft.Extensions.Logging;

namespace Backend.RoutingEngine.Services;

public class RouteOptimizer : IRouteOptimizer
{
    private readonly OrToolsVrpSolver _solver;
    private readonly MockRouteOptimizer _mockFallback;
    private readonly IRouteMatrixProvider _matrixProvider;
    private readonly IValhallaClient _valhalla;
    private readonly ILogger<RouteOptimizer> _logger;

    public RouteOptimizer(
        OrToolsVrpSolver solver,
        MockRouteOptimizer mockFallback,
        IRouteMatrixProvider matrixProvider,
        IValhallaClient valhalla,
        ILogger<RouteOptimizer> logger)
    {
        _solver = solver;
        _mockFallback = mockFallback;
        _matrixProvider = matrixProvider;
        _valhalla = valhalla;
        _logger = logger;
    }

    public async Task<OptimizationResultDto> OptimizeAsync(
        CreateOptimizationPlanRequest request,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Starting route optimization for {EngineerCount} engineers and {OrderCount} orders",
            request.Engineers.Count, request.Orders.Count);

        try
        {
            // 1. Предобработка: отсев некорректных/закрытых заявок, нормализация длительностей.
            var preprocess = PlanPreprocessor.Preprocess(request);

            if (preprocess.Engineers.Count == 0 || preprocess.Orders.Count == 0)
            {
                return OrToolsVrpSolver.BuildEmptyResult(request.PlanDate, preprocess.Engineers, preprocess.Orders, preprocess.Unassigned);
            }

            // 2. Матрицы расстояний/времени (Valhalla с costing по транспорту, фолбэк — Haversine).
            var locations = PlanPreprocessor.BuildLocations(preprocess.Engineers, preprocess.Orders);
            var vehicleTransports = preprocess.Engineers.Select(e => e.Transport).ToList();
            var matrices = await _matrixProvider.BuildAsync(locations, vehicleTransports, cancellationToken);

            // 3. Каскадные окна обедов (пока одни едят — другие работают).
            var lunchWindows = preprocess.Engineers
                .Select((engineer, index) => LunchPolicy.Resolve(engineer, index, preprocess.Engineers.Count))
                .ToList();

            // 4. Ядро оптимизации на Google OR-Tools.
            var result = _solver.Solve(
                preprocess.Engineers,
                preprocess.Orders,
                lunchWindows,
                matrices,
                preprocess.Unassigned,
                request.TimeLimitSeconds);

            // 5. Полилинии маршрутов для карты (Valhalla /route, GeoJSON). Не фатально при ошибке.
            return await EnrichWithRouteGeometryAsync(result, preprocess.Engineers, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "OR-Tools solver exception, falling back to mock solver");
        }

        _logger.LogInformation("Using fallback mock solver for routes");
        return await _mockFallback.OptimizeAsync(request, cancellationToken);
    }

    /// <summary>
    /// Для каждого маршрута запрашиваем геометрию у Valhalla (costing по транспорту бригады)
    /// и кладём ответ в <see cref="EngineerRouteDto.RouteGeoJson"/> и <see cref="EngineerRouteDto.EncodedPolyline"/>.
    /// При сбое маршрут остаётся без геометрии.
    /// </summary>
    private async Task<OptimizationResultDto> EnrichWithRouteGeometryAsync(
        OptimizationResultDto result,
        IReadOnlyList<EngineerInputDto> engineers,
        CancellationToken cancellationToken)
    {
        var engineersByExtId = engineers.ToDictionary(e => e.ExternalId);
        var routes = new List<EngineerRouteDto>(result.Routes.Count);

        foreach (var route in result.Routes)
        {
            EngineerRouteDto enriched = route;
            try
            {
                if (!engineersByExtId.TryGetValue(route.EngineerExternalId, out var engineer))
                {
                    routes.Add(route);
                    continue;
                }

                var locations = new List<Location>
                {
                    new()
                    {
                        Coordinates = new GeoCoordinate(
                            engineer.StartLatitude ?? 0.0,
                            engineer.StartLongitude ?? 0.0)
                    }
                };
                locations.AddRange(route.Stops.Select(s => new Location
                {
                    Coordinates = new GeoCoordinate(s.Latitude, s.Longitude)
                }));

                if (locations.Count < 2)
                {
                    routes.Add(route);
                    continue;
                }

                string costing = route.Transport switch
                {
                    TransportType.Car => "auto",
                    TransportType.PublicTransit => "transit",
                    TransportType.Bicycle => "bicycle",
                    TransportType.Pedestrian => "pedestrian",
                    _ => "auto"
                };

                string? geoJson = await _valhalla.GetRouteGeoJsonAsync(locations, costing, cancellationToken);
                if (string.IsNullOrWhiteSpace(geoJson) && costing == "transit")
                {
                    // Без GTFS транзит-полилинию Valhalla не отдаёт (400) — берём auto-прокси
                    geoJson = await _valhalla.GetRouteGeoJsonAsync(locations, "auto", cancellationToken);
                }

                if (!string.IsNullOrWhiteSpace(geoJson))
                {
                    enriched = route with
                    {
                        RouteGeoJson = geoJson,
                        EncodedPolyline = TryExtractPolyline(geoJson)
                    };
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to fetch route geometry for engineer {Engineer}", route.EngineerExternalId);
            }

            routes.Add(enriched);
        }

        return result with { Routes = routes };
    }

    /// <summary>Из OSRM-ответа Valhalla извлекаем строку полилинии routes[0].geometry.</summary>
    private static string? TryExtractPolyline(string? geoJson)
    {
        if (string.IsNullOrWhiteSpace(geoJson))
        {
            return null;
        }

        try
        {
            var node = System.Text.Json.Nodes.JsonNode.Parse(geoJson);
            string? geometry = node?["routes"]?.AsArray().FirstOrDefault()?["geometry"]?.GetValue<string>();
            return string.IsNullOrEmpty(geometry) ? null : geometry;
        }
        catch
        {
            return null;
        }
    }
}