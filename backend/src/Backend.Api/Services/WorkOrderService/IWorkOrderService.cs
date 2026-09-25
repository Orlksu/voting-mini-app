using Backend.Contracts.Requests;
using Backend.Contracts.Responses;
using Microsoft.AspNetCore.Http;

namespace Backend.Api.Services.WorkOrderService;

public interface IWorkOrderService
{
    Task<WorkOrderDto> CreateAsync(CreateWorkOrderRequest request, CancellationToken ct = default);
    Task<WorkOrderDto?> GetByIdentifierAsync(string identifier, CancellationToken ct = default);
    Task<bool> DeleteByIdentifierAsync(string identifier, CancellationToken ct = default);
    Task<int> AddFromImportAsync(DepotDto depotDto, List<WorkOrderInputDto> orders, CancellationToken ct = default);
    Task<OrderImportResultDto> ImportOrdersCsvAsync(List<IFormFile> files, CancellationToken ct = default);
    Task<OrderImportResultDto> ImportOrdersFromPayloadsAsync(
        List<(string FileName, byte[] Content)> files,
        IProgress<(int Processed, int Total, string Stage)>? progress = null,
        CancellationToken ct = default);
    Task<List<WorkOrderDto>> GetAllAsync(CancellationToken ct = default);
    Task<EngineerRouteDto?> GetRouteByEngineerAsync(string identifier, CancellationToken ct = default);
}