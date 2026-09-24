using Backend.Contracts.Requests;

namespace Backend.Contracts.Responses;

public record ParseImportDto(RawDepotDto? RawDepot, List<WorkOrderInputDto> Orders); 