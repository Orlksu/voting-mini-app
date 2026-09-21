namespace Backend.Contracts.Responses;

public record OptimizeRoutesResponse(
    List<EngineerRouteDto> Routes,
    List<string> UnassignedOrderIds,
    RoutingMetricsDto Metrics
);
