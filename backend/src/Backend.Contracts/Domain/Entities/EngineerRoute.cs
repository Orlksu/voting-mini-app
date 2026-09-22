using Backend.Contracts.Domain.Enums;

namespace Backend.Contracts.Domain.Entities;

public class EngineerRoute
{
    public Guid EngineerId { get; set; }
    public string EngineerExternalId { get; set; } = string.Empty;
    public string EngineerName { get; set; } = string.Empty;
    public TransportType Transport { get; set; }
    public List<RouteStop> Stops { get; set; } = [];

    public double TotalDistanceKm => Stops.Sum(s => s.DistanceFromPreviousKm);
    public TimeSpan TotalTravelTime => TimeSpan.FromMinutes(Stops.Sum(s => s.TravelTimeFromPrevious.TotalMinutes));

    // Сравнение индивидуального пробега с Greedy Baseline
    public double? BaselineDistanceKm { get; set; }
    public string? EncodedPolyline { get; set; }
}
