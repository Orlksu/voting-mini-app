using System.Globalization;
using CsvHelper.Configuration;

namespace Backend.Api.Services.CsvParserService.CsvConfiguration;

public static class CsvConfigProvider
{
    public static CsvHelper.Configuration.CsvConfiguration CreateConfig(string delimiter = ";")
    {
        return new CsvHelper.Configuration.CsvConfiguration(CultureInfo.InvariantCulture)
        {
            Delimiter = delimiter,
            PrepareHeaderForMatch = args => args.Header.Trim().ToLowerInvariant(),
            MissingFieldFound = null,
            HeaderValidated = null,
            TrimOptions = TrimOptions.Trim,
            ShouldSkipRecord = args => args.Row.Parser.Record?.All(string.IsNullOrWhiteSpace) ?? false
        };
    }
}