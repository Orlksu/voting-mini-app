using Backend.Contracts.Requests;
using CsvHelper.Configuration;

namespace Backend.Api.Services.CsvParserService.CsvConfiguration;

public sealed class EngineerCsvMap : ClassMap<EngineerCsvRowDto>
{
    public EngineerCsvMap()
    {
        Map(m => m.ExternalId).Name("ExternalId", "Id", "ID", "Идентификатор", "Код инженера");
        Map(m => m.Name).Name("Name", "ФИО", "Имя");
        Map(m => m.District).Name("District", "Район");
        Map(m => m.StartAddress).Name("StartAddress", "Start Address", "Адрес старта", "Адрес выезда");
        Map(m => m.StartLatitude).Name("StartLatitude", "Latitude", "lat", "Широта старта", "Широта");
        Map(m => m.StartLongitude).Name("StartLongitude", "Longitude", "lon", "Долгота старта", "Долгота");
        Map(m => m.ShiftStart).Name("ShiftStart", "Начало смены", "Начало");
        Map(m => m.ShiftEnd).Name("ShiftEnd", "Конец смены", "Окончание");
        Map(m => m.Transport).Name("Transport", "Транспорт", "ТС");
        Map(m => m.Skills).Name("Skills", "Навыки", "Скиллы");
        Map(m => m.MaxOrdersPerShift).Name("MaxOrdersPerShift", "Лимит заявок", "Лимит");
    }
}