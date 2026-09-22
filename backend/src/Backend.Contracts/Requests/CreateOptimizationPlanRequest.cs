namespace Backend.Contracts.Requests;

public record CreateOptimizationPlanRequest(
    DateTimeOffset PlanDate,
    List<EngineerInputDto> Engineers,
    List<WorkOrderInputDto> Orders,
    int TimeLimitSeconds = 15
);
