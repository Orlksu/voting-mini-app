namespace Backend.Api.Services.GeoCodingService;

public interface IGeocoder
{
    Task<GeocoderResultDto?> GeocodeAsync(string address, string? district = null, CancellationToken ct = default);
}