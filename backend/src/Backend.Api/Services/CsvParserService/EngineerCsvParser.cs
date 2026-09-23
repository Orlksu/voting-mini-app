using System.Globalization;
using Backend.Contracts.Requests;
using CsvHelper;

namespace Backend.Api.Services.CsvParserService;

public class EngineerCsvParser : IEngineerCsvParser
{
    public async Task<List<EngineerCsvRowDto>> ParseEngineersAsync(Stream stream, CancellationToken ct = default)
    {
        using var reader = new StreamReader(stream, System.Text.Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        var text = await reader.ReadToEndAsync(ct);

        var config = new CsvHelper.Configuration.CsvConfiguration(CultureInfo.InvariantCulture)
        {
            Delimiter = DetectDelimiter(text).ToString(),
            PrepareHeaderForMatch = args => args.Header.Trim().ToLowerInvariant(),
            MissingFieldFound = null,
            HeaderValidated = null,
            TrimOptions = CsvHelper.Configuration.TrimOptions.Trim,
            ShouldSkipRecord = args => args.Row.Parser.Record?.All(string.IsNullOrWhiteSpace) ?? false,
        };

        using var csv = new CsvReader(new StringReader(text), config);
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

    private static char DetectDelimiter(string text)
    {
        var headerLine = text
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
            .FirstOrDefault();

        if (string.IsNullOrWhiteSpace(headerLine))
            return ';';

        int commas = headerLine.Count(c => c == ',');
        int semicolons = headerLine.Count(c => c == ';');
        return commas > semicolons ? ',' : ';';
    }
}