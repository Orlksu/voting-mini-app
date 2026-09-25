using System.Text;
using System.Text.RegularExpressions;
using Backend.Api.Services.CsvParserService.CsvConfiguration;
using Backend.Contracts.Requests;
using Backend.Contracts.Responses;
using CsvHelper;

namespace Backend.Api.Services.CsvParserService;

public class CsvParserService : ICsvParserService
{
    private static readonly string[] DepotKeywords = ["офис", "депо", "база", "склад", "office", "depot"];

    public async Task<ParseImportDto> ParseOrderAsync(Stream fileStream, CancellationToken cancellationToken = default)
    {
        var (workStream, delimiter) = await CsvStreamHelper.PrepareStreamAndDetectDelimiterAsync(fileStream, cancellationToken);
        var config = CsvStreamHelper.CreateConfiguration(delimiter);

        using var reader = new StreamReader(workStream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, leaveOpen: true);
        using var csv = new CsvReader(reader, config);

        csv.Context.RegisterClassMap<WorkOrderCsvMap>();

        var orders = new List<WorkOrderInputDto>();
        RawDepotDto? depot = null;

        await foreach (var record in csv.GetRecordsAsync<WorkOrderInputDto>(cancellationToken))
        {
            var rawRow = csv.Parser.Record;

            var firstCell = rawRow?.FirstOrDefault()?.Trim() ?? string.Empty;
            bool isOfficeRow = rawRow != null 
                && (firstCell.StartsWith("Адрес Офиса", StringComparison.OrdinalIgnoreCase) 
                    || DepotKeywords.Any(k => firstCell.Equals(k, StringComparison.OrdinalIgnoreCase))
                    || (string.IsNullOrWhiteSpace(record.ExternalId) && DepotKeywords.Any(k => firstCell.Contains(k, StringComparison.OrdinalIgnoreCase))));

            if (isOfficeRow)
            {
                var detectedAddress = rawRow!
                    .Skip(1)
                    .FirstOrDefault(cell => !string.IsNullOrWhiteSpace(cell) && !DepotKeywords.Any(k => cell.Equals(k, StringComparison.OrdinalIgnoreCase)));

                if (string.IsNullOrWhiteSpace(detectedAddress))
                {
                    detectedAddress = Regex.Replace(firstCell, @"^.*?(?:адрес офиса|офис|депо|база|склад|office|depot)[а-я\s:-]*", "", RegexOptions.IgnoreCase).Trim();
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