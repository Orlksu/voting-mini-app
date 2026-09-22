namespace Backend.Contracts.Domain.ValueObjects;

public record TimeWindow(DateTimeOffset Start, DateTimeOffset End)
{
    public bool Contains(DateTimeOffset time) => time >= Start && time <= End;
}
