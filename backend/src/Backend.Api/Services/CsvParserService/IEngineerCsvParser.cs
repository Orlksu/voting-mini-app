using Backend.Contracts.Requests;

namespace Backend.Api.Services.CsvParserService;

public interface IEngineerCsvParser
{
    Task<List<EngineerCsvRowDto>> ParseEngineersAsync(Stream stream, CancellationToken ct = default);
}