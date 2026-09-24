namespace Backend.Contracts.Domain.Entities;

public class GeocodeCacheItem
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public string RawQuery { get; set; } = string.Empty;
    public double Latitude { get; set; }
    public double Longitude { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}