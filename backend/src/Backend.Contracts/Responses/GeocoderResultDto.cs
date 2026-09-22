using System.Text.Json.Serialization;

namespace Backend.Api.Services.GeoCodingService;

public class GeocoderResultDto
{
    [JsonPropertyName("lat")]
    public string LatString { get; set; } = string.Empty;

    [JsonPropertyName("lon")]
    public string LonString { get; set; } = string.Empty;

    [JsonPropertyName("display_name")]
    public string DisplayName { get; set; } = string.Empty;
    public double Latitude => double.TryParse(LatString, System.Globalization.CultureInfo.InvariantCulture, out var val) ? val : 0;
    public double Longitude => double.TryParse(LonString, System.Globalization.CultureInfo.InvariantCulture, out var val) ? val : 0;
}