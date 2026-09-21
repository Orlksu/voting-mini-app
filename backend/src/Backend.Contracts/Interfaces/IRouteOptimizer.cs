using Backend.Contracts.Requests;
using Backend.Contracts.Responses;

namespace Backend.Contracts.Interfaces;

public interface IRouteOptimizer
{
    Task<OptimizeRoutesResponse> OptimizeAsync(OptimizeRoutesRequest request, CancellationToken cancellationToken = default);
}
