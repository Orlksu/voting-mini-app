namespace Backend.Contracts.Domain.ValueObjects;

public record Location(string RawAddress, GeoCoordinate Coordinates);
