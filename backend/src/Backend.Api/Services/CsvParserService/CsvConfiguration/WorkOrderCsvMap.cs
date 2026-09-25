using System.Globalization;
using Backend.Api.Services.Common;
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
            .Convert(args => ParseCoordinateSafe(GetFirstField(args.Row, "Latitude", "lat", "Широта"), -90, 90));

        Map(m => m.Longitude).Name("Longitude", "lon", "Долгота").Optional()
            .Convert(args => ParseCoordinateSafe(GetFirstField(args.Row, "Longitude", "lon", "Долгота"), -180, 180));

        Map(m => m.WindowStart).Name("Начало", "Start", "Время начала")
            .Convert(args => DateTimeParserHelper.ParseMskDateTimeToUtc(GetFirstField(args.Row, "Начало", "Start", "Время начала")));

        Map(m => m.WindowEnd).Name("Окончание", "End", "Конец", "Время окончания").Optional()
            .Convert(args => DateTimeParserHelper.ParseMskDateTimeToUtc(GetFirstField(args.Row, "Окончание", "End", "Конец", "Время окончания")));

        Map(m => m.RequiredSkill)
            .Convert(args =>
            {
                var bkType = args.Row.GetField("Тип заявки BK") ?? string.Empty;
                var hdType = args.Row.GetField("Тип заявки HD") ?? string.Empty;
                return MapSkillType(bkType, hdType);
            });

        Map(m => m.RequiredTransport).Optional()
            .Convert(args =>
            {
                var gigabit = GetFirstField(args.Row, "Гигабит", "Gigabit", "Требуется авто", "Авто");
                if (!string.IsNullOrWhiteSpace(gigabit) &&
                    (gigabit.Equals("Да", StringComparison.OrdinalIgnoreCase) || gigabit.Equals("true", StringComparison.OrdinalIgnoreCase) || gigabit.Equals("1")))
                {
                    return TransportType.Car;
                }
                var transport = GetFirstField(args.Row, "Транспорт", "Transport");
                if (!string.IsNullOrWhiteSpace(transport) && transport.Contains("авто", StringComparison.OrdinalIgnoreCase))
                {
                    return TransportType.Car;
                }
                return null;
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

    private static string? GetFirstField(CsvHelper.IReaderRow row, params string[] names)
    {
        foreach (var name in names)
        {
            if (row.TryGetField(name, out string? val) && !string.IsNullOrWhiteSpace(val))
                return val;
        }
        return null;
    }

    private static double? ParseCoordinateSafe(string? text, double min, double max)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        text = text.Replace(',', '.').Trim();
        if (double.TryParse(text, NumberStyles.Any, CultureInfo.InvariantCulture, out var value) && value >= min && value <= max)
        {
            return value;
        }
        return null;
    }

    private static int MapDuration(string? bkType)
    {
        var formatedType = (bkType ?? string.Empty).ToLowerInvariant();
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
            return 10;
        }
        
        return 30;
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
        var formatedType = (bkType ?? string.Empty).ToLowerInvariant();
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