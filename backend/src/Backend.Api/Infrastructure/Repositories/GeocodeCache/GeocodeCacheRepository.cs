using System.Globalization;
using Backend.Api.Services.GeoCodingService;
using Backend.Contracts.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Backend.Api.Infrastructure.Repositories;

public class GeocodeCacheRepository(AppDbContext db) : IGeocodeCacheRepository
{
    public async Task<GeocoderResultDto?> GetByQueryAsync(string query, CancellationToken ct = default)
    {
        var lowerQuery = query.ToLowerInvariant();
        var item = await db.GeocodeCache
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.RawQuery == lowerQuery, ct);

        if (item is null)
            return null;

        return ToDto(item);
    }

    public async Task<Dictionary<string, GeocoderResultDto>> GetBatchByQueriesAsync(IEnumerable<string> queries, CancellationToken ct = default)
    {
        var queryList = queries.Select(q => q.ToLowerInvariant()).Distinct().ToList();
        if (queryList.Count == 0)
            return [];

        var items = await db.GeocodeCache
            .AsNoTracking()
            .Where(c => queryList.Contains(c.RawQuery))
            .ToListAsync(ct);

        return items.ToDictionary(c => c.RawQuery, ToDto, StringComparer.OrdinalIgnoreCase);
    }

    public async Task SaveAsync(string query, double lat, double lon, CancellationToken ct = default)
    {
        var item = new GeocodeCacheItem
        {
            RawQuery = query.ToLowerInvariant(),
            Latitude = lat,
            Longitude = lon,
            CreatedAt = DateTimeOffset.UtcNow,
        };

        try
        {
            await db.GeocodeCache.AddAsync(item, ct);
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
           
            db.Entry(item).State = EntityState.Detached;
        }
    }

    private static GeocoderResultDto ToDto(GeocodeCacheItem item) => new()
    {
        LatString = item.Latitude.ToString("R", CultureInfo.InvariantCulture),
        LonString = item.Longitude.ToString("R", CultureInfo.InvariantCulture),
        DisplayName = item.RawQuery,
    };
}