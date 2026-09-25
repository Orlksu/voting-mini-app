using Backend.Contracts.Interfaces;
using Backend.Contracts.Requests;
using Backend.Contracts.Responses;
using Backend.RoutingEngine.Solvers;
using Microsoft.Extensions.Logging;

namespace Backend.RoutingEngine.Services;

public class RouteOptimizer : IRouteOptimizer
{
    private readonly OrToolsVrpSolver _solver;
    private readonly MockRouteOptimizer _mockFallback;
    private readonly IRouteMatrixProvider _matrixProvider;
    private readonly ILogger<RouteOptimizer> _logger;

    public RouteOptimizer(
        OrToolsVrpSolver solver,
        MockRouteOptimizer mockFallback,
        IRouteMatrixProvider matrixProvider,
        ILogger<RouteOptimizer> logger)
    {
        _solver = solver;
        _mockFallback = mockFallback;
        _matrixProvider = matrixProvider;
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

            // 2. Матрицы расстояний/времени (Фаза B: Valhalla с costing по транспорту,
            //    фолбэк — Haversine). Пер-бригадные: каждая бригада ездит в costing своего транспорта.
            var locations = PlanPreprocessor.BuildLocations(preprocess.Engineers, preprocess.Orders);
            var vehicleTransports = preprocess.Engineers.Select(e => e.Transport).ToList();
            var matrices = await _matrixProvider.BuildAsync(locations, vehicleTransports, cancellationToken);

            // 3. Каскадные окна обедов (пока одни едят — другие работают).
            var lunchWindows = preprocess.Engineers
                .Select((engineer, index) => LunchPolicy.Resolve(engineer, index, preprocess.Engineers.Count))
                .ToList();

            // 4. Ядро оптимизации.
            var result = _solver.Solve(
                preprocess.Engineers,
                preprocess.Orders,
                lunchWindows,
                matrices,
                preprocess.Unassigned,
                request.TimeLimitSeconds);

            return result;
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
}