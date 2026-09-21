namespace Backend.Contracts.Models;

public record LocationDto(
    double Latitude,
    double Longitude,
    string? Address = null
);
