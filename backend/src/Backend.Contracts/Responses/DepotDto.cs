namespace Backend.Contracts.Responses;

public record DepotDto(string Name, string Address, double Latitude, double Longitude, Guid? Id = null);