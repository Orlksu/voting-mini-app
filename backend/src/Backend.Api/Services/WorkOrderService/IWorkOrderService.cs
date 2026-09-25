using Backend.Contracts.Requests;
using Backend.Contracts.Responses;
using Microsoft.AspNetCore.Http;

namespace Backend.Api.Services.WorkOrderService;

public interface IWorkOrderService
{
    Task<int> AddFromImportAsync(DepotDto depotDto, List<WorkOrderInputDto> orders, CancellationToken ct = default);
    Task<OrderImportResultDto> ImportOrdersCsvAsync(List<IFormFile> files, CancellationToken ct = default);
    Task<OrderImportResultDto> ImportOrdersFromPayloadsAsync(
        List<(string FileName, byte[] Content)> files,
        IProgress<(int Processed, int Total, string Stage)>? progress = null,
        CancellationToken ct = default);
    Task<List<WorkOrderDto>> GetAllAsync(CancellationToken ct = default);
    Task<List<EngineerOrderDto>?> GetByEngineerAsync(Guid engineerId, CancellationToken ct = default);
    Task<List<EngineerOrderDto>?> GetByEngineerAsync(string identifier, CancellationToken ct = default);
}