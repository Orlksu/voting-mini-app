namespace Backend.Contracts.Requests;

public record EngineerLiveStateDto(
    string EngineerExternalId,
    double CurrentLatitude,
    double CurrentLongitude,
    DateTimeOffset AvailableFromTime,
    List<string> CompletedOrderExternalIds
);
