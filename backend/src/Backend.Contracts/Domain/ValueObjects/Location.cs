namespace Backend.Contracts.Domain.ValueObjects;

public class Location
{
    public string RawAddress { get; init; } = string.Empty;
    public GeoCoordinate Coordinates { get; init; }
}