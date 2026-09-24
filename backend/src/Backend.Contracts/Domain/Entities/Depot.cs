using Backend.Contracts.Domain.ValueObjects;

namespace Backend.Contracts.Domain.Entities;

public class Depot
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string District { get; set; } = string.Empty;
    public Location? Location { get; set; }
}