namespace Backend.Contracts.Requests;

public class EngineerCsvRowDto
{
    public string ExternalId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string District { get; set; } = string.Empty;
    public string StartAddress { get; set; } = string.Empty;
    public string StartLatitude { get; set; } = string.Empty;
    public string StartLongitude { get; set; } = string.Empty;
    public string ShiftStart { get; set; } = string.Empty;
    public string ShiftEnd { get; set; } = string.Empty;
    public string Transport { get; set; } = string.Empty;
    public string Skills { get; set; } = string.Empty;
    public string MaxOrdersPerShift { get; set; } = string.Empty;
}