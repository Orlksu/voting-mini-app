using System.Globalization;
using Backend.Api.Services.GeoCodingService;
using Backend.Contracts.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Backend.Api.Infrastructure.Repositories;

public class GeocodeCacheRepository(AppDbContext db) : IGeocodeCacheRepository
{
    public async Task<GeocoderResultDto?> GetByQueryAsync(string query, CancellationToken ct = default)
    {
        var item = await db.GeocodeCache
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.RawQuery == query, ct);

        if (item is null)
            return null;

        return new GeocoderResultDto
        {
            LatString = item.Latitude.ToString("R", CultureInfo.InvariantCulture),
            LonString = item.Longitude.ToString("R", CultureInfo.InvariantCulture),
            DisplayName = item.RawQuery,
        };
    }

    public async Task SaveAsync(string query, double lat, double lon, CancellationToken ct = default)
    {
        try
        {
            await db.GeocodeCache.AddAsync(new GeocodeCacheItem
            {
                RawQuery = query,
                Latitude = lat,
                Longitude = lon,
                CreatedAt = DateTimeOffset.UtcNow,
            }, ct);
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            // Конкурентная вставка того же RawQuery (уникальный индекс) — кэш уже заполнен кем-то другим.
        }
    }
}