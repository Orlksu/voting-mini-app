namespace Backend.Contracts.Domain.ValueObjects;

public record PlanMetrics(
    int TotalOrders,
    int AssignedOrders,
    int UnassignedOrders,
    int OptimizedActiveEngineers,
    int BaselineActiveEngineers,
    int SavedEngineersCount,
    double OptimizedTotalDistanceKm,
    double BaselineTotalDistanceKm,
    double DistanceEconomyPercent);