using System.Text;
using System.Text.RegularExpressions;
using Backend.Api.Services.CsvParserService.CsvConfiguration;
using Backend.Api.Services.GeoCodingService;
using Backend.Contracts.Requests;
using Backend.Contracts.Responses;
using CsvHelper;

namespace Backend.Api.Services.CsvParserService;

public class CsvParserService : ICsvParserService
{
    public async Task<ParseImportDto> ParseOrderAsync(Stream fileStream, CancellationToken cancellationToken = default)
    {
        using var reader = new StreamReader(fileStream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        using var csv = new CsvReader(reader, CsvConfigProvider.CreateConfig());

        csv.Context.RegisterClassMap<WorkOrderCsvMap>();

        var orders = new List<WorkOrderInputDto>();
        RawDepotDto? depot = null;
        await foreach (var record in csv.GetRecordsAsync<WorkOrderInputDto>(cancellationToken))
        {
            var rawRow = csv.Parser.Record;

            bool isOfficeRow = rawRow != null && rawRow.Any(cell => cell.Contains("офис", StringComparison.OrdinalIgnoreCase));

            if (isOfficeRow)
            {
                var detectedAddress = rawRow!
                    .FirstOrDefault(cell => !string.IsNullOrWhiteSpace(cell) && 
                                            !cell.Contains("офис", StringComparison.OrdinalIgnoreCase));

                if (string.IsNullOrWhiteSpace(detectedAddress))
                {
                    var cellWithOffice = rawRow!.FirstOrDefault(cell => cell.Contains("офис", StringComparison.OrdinalIgnoreCase)) ?? "";
                    detectedAddress = Regex.Replace(cellWithOffice, @"^.*?офис[а-я\s:-]*", "", RegexOptions.IgnoreCase).Trim();
                }

                if (!string.IsNullOrWhiteSpace(detectedAddress))
                {
                    depot = new RawDepotDto(detectedAddress);
                }
                continue;
            }

            if (string.IsNullOrWhiteSpace(record.Address))
                continue;

            orders.Add(record);
        }

        return new ParseImportDto(depot, orders);
    }
}