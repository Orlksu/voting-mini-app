namespace Backend.Contracts.Responses;

/// <summary>Запланированный перерыв (обед) инженера в составе маршрута.</summary>
public record BreakStopDto(
    DateTimeOffset Start,
    DateTimeOffset End,
    int DurationMinutes
);