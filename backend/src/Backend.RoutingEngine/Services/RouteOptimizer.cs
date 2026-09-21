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
    private readonly ILogger<RouteOptimizer> _logger;

    public RouteOptimizer(ILogger<RouteOptimizer> logger)
    {
        _logger = logger;
        _solver = new OrToolsVrpSolver();
        _mockFallback = new MockRouteOptimizer();
    }

    public async Task<OptimizeRoutesResponse> OptimizeAsync(OptimizeRoutesRequest request, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Starting route optimization for {EngineerCount} engineers and {OrderCount} orders",
            request.Engineers.Count, request.Orders.Count);

        try
        {
            var result = _solver.Solve(request);
            if (result != null)
            {
                return result;
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "OR-Tools solver exception, falling back to mock solver");
        }

        _logger.LogInformation("Using fallback mock solver for routes");
        return await _mockFallback.OptimizeAsync(request, cancellationToken);
    }
}
