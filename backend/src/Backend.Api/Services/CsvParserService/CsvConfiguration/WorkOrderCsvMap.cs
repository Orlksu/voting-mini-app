using System.Globalization;
using Backend.Contracts.Domain.Enums;
using Backend.Contracts.Requests;
using CsvHelper.Configuration;

namespace Backend.Api.Services.CsvParserService.CsvConfiguration;

public sealed class WorkOrderCsvMap : ClassMap<WorkOrderInputDto>
{
    public WorkOrderCsvMap()
    {
        Map(m => m.ExternalId).Name("Заявка", "Id", "id", "Номер заявки", "Номер");

        Map(m => m.Address).Name("Адрес", "Address", "address", "Адрес клиента");

        Map(m => m.Latitude).Name("Latitude", "lat", "Широта").Optional()
            .Convert(args => ParseDoubleSafe(args.Row.GetField("Latitude") ?? args.Row.GetField("Широта")));

        Map(m => m.Longitude).Name("Longitude", "lon", "Долгота").Optional()
            .Convert(args => ParseDoubleSafe(args.Row.GetField("Longitude") ?? args.Row.GetField("Долгота")));

        Map(m => m.WindowStart).Name("Начало", "Start", "Время начала")
            .Convert(args => ParseDateTimeSafe(args.Row.GetField("Начало") ?? args.Row.GetField("Start")));

        Map(m => m.WindowEnd).Name("Окончание", "End", "Конец", "Время окончания").Optional()
            .Convert(args => ParseDateTimeSafe(args.Row.GetField("Окончание") ?? args.Row.GetField("End")));

        Map(m => m.RequiredSkill)
            .Convert(args =>
            {
                var bkType = args.Row.GetField("Тип заявки BK") ?? string.Empty;
                var hdType = args.Row.GetField("Тип заявки HD") ?? string.Empty;
                return MapSkillType(bkType, hdType);
            });

        Map(m => m.Priority).Convert(args =>
        {
            var bkType = args.Row.GetField("Тип заявки BK") ?? string.Empty;
            return MapPriority(bkType);
        });

        Map(m => m.TaskType).Convert(args =>
            args.Row.GetField("Тип заявки HD") ?? args.Row.GetField("Тип заявки BK") ?? "Обслуживание");
        Map(m => m.District).Name("Район");
        
        Map(m => m.DurationMinutes).Convert(args =>
        {
            var bkType = args.Row.GetField("Тип заявки BK") ?? string.Empty;
            return MapDuration(bkType);
        });
    }

    private static double? ParseDoubleSafe(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        text = text.Replace(',', '.').Trim();
        return double.TryParse(text, NumberStyles.Any, CultureInfo.InvariantCulture, out var value) ? value : null;
    }

    private static int MapDuration(string? bkType)
    {
        var  formatedType = bkType.ToLowerInvariant();
        if (formatedType.Contains("авария") || formatedType.Contains("глобальная"))
        {
            return 80;
        }

        if (formatedType.Contains("подключение"))
        {
            return 60;
        }

        if (formatedType.Contains("дозаказ"))
        {
            return 10 ;
        }
        
        return 30;
      
    }
   
    private static DateTimeOffset? ParseDateTimeSafe(string? text, DateTime? baseDate = null)
    {
        if (string.IsNullOrWhiteSpace(text))
            return null;

        var msk = TimeZoneInfo.FindSystemTimeZoneById("Europe/Moscow");
        var mskOffset = msk.GetUtcOffset(DateTime.UtcNow);
        var trimmed = text.Trim();

        string[] fullFormats =
        [
            "dd.MM.yyyy HH:mm",
            "dd.MM.yyyy H:mm",
            "dd.MM.yyyy HH:mm:ss",
            "yyyy-MM-ddTHH:mm:ss",
            "yyyy-MM-dd HH:mm"
        ];

    
        if (DateTime.TryParseExact(trimmed, fullFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt))
        {
            return new DateTimeOffset(DateTime.SpecifyKind(dt, DateTimeKind.Unspecified), mskOffset);
        }

       
        string[] timeFormats = ["HH:mm", "H:mm", "HH:mm:ss"];
        if (DateTime.TryParseExact(trimmed, timeFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var timeOnly))
        {
            var mskToday = baseDate
                ?? TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, msk).Date;
            var combined = mskToday.Add(timeOnly.TimeOfDay);
            return new DateTimeOffset(DateTime.SpecifyKind(combined, DateTimeKind.Unspecified), mskOffset);
        }

     
        if (DateTimeOffset.TryParse(trimmed, CultureInfo.GetCultureInfo("ru-RU"), DateTimeStyles.None, out var dto))
        {
            return dto;
        }

        return null;
    }
    private static SkillType MapSkillType(string bkType, string hdType)
    {
        var combined = $"{bkType} {hdType}".ToLowerInvariant();

        if (combined.Contains("авар") || combined.Contains("сбой") || combined.Contains("нет связи") || combined.Contains("emergency"))
        {
            return SkillType.Emergency;
        }

        if (combined.Contains("подключ") || combined.Contains("монтаж") || combined.Contains("установк") || combined.Contains("конвергенц") || combined.Contains("install"))
        {
            return SkillType.Installation;
        }

        return SkillType.LocalRepair;
    }

    private static OrderPriority MapPriority(string bkType)
    {
        var formatedType = bkType.ToLowerInvariant();
        if (formatedType.Contains("авария") || formatedType.Contains("глобальная"))
        {
            return OrderPriority.Emergency;
        }

        if (formatedType.Contains("подключение"))
        {
            return OrderPriority.High;
        }

        if (formatedType.Contains("дозаказ") || formatedType.Contains("локальная заявка"))
        {
            return OrderPriority.Normal;
        }
        
        return OrderPriority.Low;
    }
}