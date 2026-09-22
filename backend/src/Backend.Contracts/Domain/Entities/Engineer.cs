using Backend.Contracts.Domain.Enums;
using Backend.Contracts.Domain.ValueObjects;

namespace Backend.Contracts.Domain.Entities;

public class Engineer
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public string ExternalId { get; init; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public Location StartLocation { get; set; } = null!;
    public TimeWindow Shift { get; set; } = null!;
    public TransportType Transport { get; set; }
    public HashSet<SkillType> Skills { get; set; } = [];
    public bool IsAvailable { get; set; } = true;
    public int MaxOrdersPerShift { get; set; } = 15;
}
