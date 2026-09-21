namespace Backend.Contracts.Responses;

public record RoutingMetricsDto(
    int TotalOrders,
    int AssignedOrders,
    int UnassignedOrders,
    double TotalDistanceKm,
    int TotalDurationMinutes,
    int SlaViolationsCount,
    long SolverExecutionTimeMs
);
