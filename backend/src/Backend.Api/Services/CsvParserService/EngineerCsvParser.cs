using Backend.Api.Services.CsvParserService.CsvConfiguration;
using Backend.Contracts.Requests;
using CsvHelper;

namespace Backend.Api.Services.CsvParserService;

public class EngineerCsvParser : IEngineerCsvParser
{
    public async Task<List<EngineerCsvRowDto>> ParseEngineersAsync(Stream stream, CancellationToken ct = default)
    {
        var (workStream, delimiter) = await CsvStreamHelper.PrepareStreamAndDetectDelimiterAsync(stream, ct);
        var config = CsvStreamHelper.CreateConfiguration(delimiter);

        using var reader = new StreamReader(workStream, System.Text.Encoding.UTF8, detectEncodingFromByteOrderMarks: true, leaveOpen: true);
        using var csv = new CsvReader(reader, config);
        csv.Context.RegisterClassMap<CsvConfiguration.EngineerCsvMap>();

        var result = new List<EngineerCsvRowDto>();
        await foreach (var record in csv.GetRecordsAsync<EngineerCsvRowDto>(ct))
        {
            if (string.IsNullOrWhiteSpace(record.ExternalId) && string.IsNullOrWhiteSpace(record.Name))
                continue;

            result.Add(record);
        }

        return result;
    }
}