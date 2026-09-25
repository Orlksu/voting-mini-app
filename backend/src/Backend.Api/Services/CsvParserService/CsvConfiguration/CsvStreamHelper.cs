namespace Backend.Api.Services.CsvParserService.CsvConfiguration;

using System.Globalization;
using System.Text;
using CsvHelper.Configuration;

public static class CsvStreamHelper
{
    public static async Task<(Stream Stream, char Delimiter)> PrepareStreamAndDetectDelimiterAsync(Stream sourceStream, CancellationToken ct = default)
    {
        Stream workStream = sourceStream;
        if (!sourceStream.CanSeek)
        {
            var ms = new MemoryStream();
            await sourceStream.CopyToAsync(ms, ct);
            ms.Position = 0;
            workStream = ms;
        }

        var startPos = workStream.Position;
        var buffer = new byte[4096];
        var bytesRead = await workStream.ReadAsync(buffer.AsMemory(0, buffer.Length), ct);
        workStream.Position = startPos;

        var sampleText = Encoding.UTF8.GetString(buffer, 0, bytesRead);
        var delimiter = DetectDelimiter(sampleText);

        return (workStream, delimiter);
    }

    public static CsvConfiguration CreateConfiguration(char delimiter)
    {
        return new CsvConfiguration(CultureInfo.InvariantCulture)
        {
            Delimiter = delimiter.ToString(),
            PrepareHeaderForMatch = args => args.Header.Trim().ToLowerInvariant(),
            MissingFieldFound = null,
            HeaderValidated = null,
            BadDataFound = null,
            TrimOptions = TrimOptions.Trim,
            ShouldSkipRecord = args => args.Row.Parser.Record?.All(string.IsNullOrWhiteSpace) ?? false
        };
    }

    public static char DetectDelimiter(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return ';';

        var firstLine = text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
        if (string.IsNullOrWhiteSpace(firstLine))
            return ';';

        var commas = firstLine.Count(c => c == ',');
        var semicolons = firstLine.Count(c => c == ';');
        var tabs = firstLine.Count(c => c == '\t');

        if (tabs > commas && tabs > semicolons)
            return '\t';

        return commas > semicolons ? ',' : ';';
    }
}
