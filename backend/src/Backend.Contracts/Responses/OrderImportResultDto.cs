namespace Backend.Contracts.Responses;

public record OrderImportResultDto(
    int ProcessedFiles,
    int ImportedOrdersCount,
    int UnresolvedAddressesCount,
    List<string> Warnings);
