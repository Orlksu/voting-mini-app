namespace Backend.Contracts.Responses;

public record PlanComparisonMetricsDto(
    int TotalOrders,
    int AssignedOrders,
    int UnassignedOrders,
    int OptimizedActiveEngineers,
    int BaselineActiveEngineers,
    int SavedEngineersCount,
    double OptimizedTotalDistanceKm,
    double BaselineTotalDistanceKm,
    double DistanceEconomyPercent
);
