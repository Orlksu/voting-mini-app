namespace Backend.Contracts.Domain.Enums;

public enum UnassignedReasonCode
{
    NoQualifiedEngineer = 1,
    NoTransportMatch = 2,
    TimeWindowMissed = 3,
    ShiftLimitExceeded = 4,
    CapacityOverload = 5,
    EmergencyDisplaced = 6
}
