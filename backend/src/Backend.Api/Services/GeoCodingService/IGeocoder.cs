namespace Backend.Api.Services.GeoCodingService;

public interface IGeocoder
{
    Task<GeocoderResultDto?> GeocodeAsync(string address, string? district = null, CancellationToken ct = default);
    Task<Dictionary<string, GeocoderResultDto?>> GeocodeBatchAsync(IEnumerable<(string Address, string? District)> items, CancellationToken ct = default);
    Task<Dictionary<string, GeocoderResultDto?>> GeocodeBatchAsync(IEnumerable<(string Address, string? District)> items, IProgress<(int Processed, int Total, string Stage)>? progress, CancellationToken ct = default);
}