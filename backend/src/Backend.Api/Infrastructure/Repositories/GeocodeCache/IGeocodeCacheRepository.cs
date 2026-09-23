using Backend.Api.Services.GeoCodingService;

namespace Backend.Api.Infrastructure.Repositories;

public interface IGeocodeCacheRepository
{
    Task<GeocoderResultDto?> GetByQueryAsync(string query, CancellationToken ct = default);
    Task SaveAsync(string query, double lat, double lon, CancellationToken ct = default);
}