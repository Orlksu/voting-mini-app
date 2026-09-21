using Backend.Contracts.Models;

namespace Backend.Contracts.Requests;

public record OptimizeRoutesRequest(
    List<EngineerDto> Engineers,
    List<OrderDto> Orders,
    DateOnly Date
);
