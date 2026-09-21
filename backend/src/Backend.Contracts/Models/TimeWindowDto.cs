namespace Backend.Contracts.Models;

public record TimeWindowDto(
    TimeOnly Start,
    TimeOnly End
);
