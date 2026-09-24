using Backend.Api.Services.GeoCodingService;
using Backend.Contracts.Requests;
using Backend.Contracts.Responses;

namespace Backend.Api.Services.CsvParserService;

public interface ICsvParserService
{
    public Task<ParseImportDto> ParseOrderAsync(Stream fileStream, CancellationToken cancellationToken = default);
}