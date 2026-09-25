namespace Backend.Api.Services.Common;

using System.Globalization;

public static class DateTimeParserHelper
{
    private static readonly TimeZoneInfo MoscowTimeZone = InitMoscowTimeZone();

    private static TimeZoneInfo InitMoscowTimeZone()
    {
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById("Europe/Moscow");
        }
        catch (TimeZoneNotFoundException)
        {
            return TimeZoneInfo.FindSystemTimeZoneById("Russian Standard Time");
        }
    }

    public static TimeZoneInfo MoscowZone => MoscowTimeZone;
    
    public static DateTimeOffset? ParseMskDateTimeToUtc(string? text, DateTime? baseDateMsk = null)
    {
        if (string.IsNullOrWhiteSpace(text))
            return null;

        var trimmed = text.Trim();
        var mskOffset = MoscowTimeZone.GetUtcOffset(DateTime.UtcNow);

        // Если в строке уже есть явный оффсет (например, 2026-09-25T10:00:00+03:00 или Z)
        if (trimmed.Contains('+') || trimmed.EndsWith("Z", StringComparison.OrdinalIgnoreCase) ||
            (trimmed.Length > 10 && trimmed.Substring(10).Contains('-')))
        {
            if (DateTimeOffset.TryParse(trimmed, CultureInfo.InvariantCulture, DateTimeStyles.None, out var explicitDto))
            {
                return explicitDto.ToUniversalTime();
            }
        }

        string[] fullFormats =
        [
            "dd.MM.yyyy HH:mm",
            "dd.MM.yyyy H:mm",
            "dd.MM.yyyy HH:mm:ss",
            "dd.MM.yyyy H:mm:ss",
            "yyyy-MM-ddTHH:mm:ss",
            "yyyy-MM-dd HH:mm:ss",
            "yyyy-MM-dd HH:mm",
            "dd/MM/yyyy HH:mm",
            "dd.MM.yyyy",
            "yyyy-MM-dd"
        ];

        if (DateTime.TryParseExact(trimmed, fullFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt))
        {
            var unspecified = DateTime.SpecifyKind(dt, DateTimeKind.Unspecified);
            return new DateTimeOffset(unspecified, mskOffset).ToUniversalTime();
        }

        string[] timeFormats = ["HH:mm", "H:mm", "HH:mm:ss", "H:mm:ss"];
        if (DateTime.TryParseExact(trimmed, timeFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var timeOnly))
        {
            var mskDate = baseDateMsk ?? TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, MoscowTimeZone).Date;
            var combined = mskDate.Add(timeOnly.TimeOfDay);
            return new DateTimeOffset(DateTime.SpecifyKind(combined, DateTimeKind.Unspecified), mskOffset).ToUniversalTime();
        }

        if (TimeSpan.TryParse(trimmed, CultureInfo.InvariantCulture, out var span))
        {
            var mskDate = baseDateMsk ?? TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, MoscowTimeZone).Date;
            var combined = mskDate.Add(span);
            return new DateTimeOffset(DateTime.SpecifyKind(combined, DateTimeKind.Unspecified), mskOffset).ToUniversalTime();
        }

        if (DateTime.TryParse(trimmed, CultureInfo.GetCultureInfo("ru-RU"), DateTimeStyles.None, out var ruDt))
        {
            var unspecified = DateTime.SpecifyKind(ruDt, DateTimeKind.Unspecified);
            return new DateTimeOffset(unspecified, mskOffset).ToUniversalTime();
        }

        return null;
    }
}
