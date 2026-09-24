namespace Backend.Contracts.Responses;

public record EngineerImportResultDto(
    int Imported,
    int Skipped,
    int UnresolvedAddresses,
    int DepotsCreated,
    IReadOnlyList<string> Warnings);