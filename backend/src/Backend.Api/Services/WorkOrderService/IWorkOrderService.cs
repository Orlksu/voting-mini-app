using Backend.Contracts.Requests;
using Backend.Contracts.Responses;

namespace Backend.Api.Services.WorkOrderService;

public interface IWorkOrderService
{
    Task<int> AddFromImportAsync(DepotDto depotDto, List<WorkOrderInputDto> orders, CancellationToken ct = default);
}