using Backend.Contracts.Enums;

namespace Backend.Contracts.Models;

public record OrderDto(
    string Id,
    string Title,
    LocationDto Location,
    TimeWindowDto TimeWindow,
    int DurationMinutes,
    List<string> RequiredSkills,
    OrderPriority Priority = OrderPriority.Normal,
    List<string>? RequiredEquipment = null
);
