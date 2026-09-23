using Backend.Contracts.Domain.ValueObjects;

namespace Backend.Contracts.Domain.Entities;

public class RouteStop
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public Guid EngineerRouteId { get; set; }
    public int Sequence { get; set; }
    public string ExternalOrderId { get; set; } = string.Empty;
    public Location Location { get; set; } = null!;
    public DateTimeOffset EstimatedArrival { get; set; }
    public DateTimeOffset ServiceStart { get; set; }
    public DateTimeOffset ServiceEnd { get; set; }
    public double DistanceFromPreviousKm { get; set; }
    public TimeSpan TravelTimeFromPrevious { get; set; }
    public TimeSpan WaitingDuration { get; set; }
    public string AssignmentReason { get; set; } = string.Empty;
}
