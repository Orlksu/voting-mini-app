using Backend.Contracts.Requests;
using Backend.Contracts.Responses;

namespace Backend.Contracts.Interfaces;

public interface IRouteOptimizer
{
    Task<OptimizationResultDto> OptimizeAsync(CreateOptimizationPlanRequest request, CancellationToken cancellationToken = default);
}
