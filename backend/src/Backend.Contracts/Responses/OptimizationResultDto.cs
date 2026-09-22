namespace Backend.Contracts.Responses;

public record OptimizationResultDto(
    Guid PlanId,
    DateTimeOffset PlanDate,
    PlanComparisonMetricsDto Metrics,
    List<EngineerRouteDto> Routes,
    List<UnassignedOrderDto> UnassignedOrders
);
