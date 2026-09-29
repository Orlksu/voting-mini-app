using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Backend.Api.Infrastructure.Repositories;
using Microsoft.Extensions.Logging;

namespace Backend.Api.Services.GeoCodingService;

public class GeocoderService(HttpClient httpClient, IGeocodeCacheRepository cache, ILogger<GeocoderService> logger) : IGeocoder
{

    private static readonly (string Needle, double Lat, double Lon, string District)[] ManualOverrides =
    [
        ("Талалихина, д. 16",  55.7355, 37.6768, "Таганский"),
    ];

    public async Task<GeocoderResultDto?> GeocodeAsync(string address, string? district = null, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(address))
            return null;


        foreach (var (needle, lat, lon, overrideDistrict) in ManualOverrides)
        {
            if (address.Contains(needle, StringComparison.OrdinalIgnoreCase))
            {
                
                return new GeocoderResultDto
                {
                    LatString = lat.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    LonString = lon.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    DisplayName = address
                };
               
            }
        }

        var rawQuery = QueryKey(address, district);

        var cached = await cache.GetByQueryAsync(rawQuery, cancellationToken);
        if (cached != null)
        {
            return cached.IsNotFound ? null : cached;
        }

        return await GeocodeInternalAsync(address, district, rawQuery, cancellationToken);
    }

    private async Task<GeocoderResultDto?> GeocodeInternalAsync(string address, string? district, string rawQuery, CancellationToken cancellationToken)
    {
        var searchQueries = BuildSearchQueries(address, district);
        
        bool allQueriesAttemptedCleanly = true;
        foreach (var query in searchQueries.Take(6))
        {
            var (result, isSuccess) = await QueryNominatimWithStatusAsync(query, cancellationToken);
            if (result != null)
            {
                await cache.SaveAsync(rawQuery, result.Latitude, result.Longitude, cancellationToken);
                return result;
            }

            if (!isSuccess)
            {
                allQueriesAttemptedCleanly = false;
                break;
            }
        }

        if (allQueriesAttemptedCleanly)
        {
            await cache.SaveAsync(rawQuery, -999, -999, cancellationToken);
        }

        return null;
    }

    public Task<Dictionary<string, GeocoderResultDto?>> GeocodeBatchAsync(
        IEnumerable<(string Address, string? District)> items, 
        CancellationToken ct = default)
        => GeocodeBatchAsync(items, null, ct);

    public async Task<Dictionary<string, GeocoderResultDto?>> GeocodeBatchAsync(
        IEnumerable<(string Address, string? District)> items, 
        IProgress<(int Processed, int Total, string Stage)>? progress,
        CancellationToken ct = default)
    {
        var distinctItems = items
            .Where(x => !string.IsNullOrWhiteSpace(x.Address))
            .Distinct()
            .ToList();

        var result = new Dictionary<string, GeocoderResultDto?>(StringComparer.OrdinalIgnoreCase);
        var toProcess = new List<(string Address, string? District)>();

        foreach (var item in distinctItems)
        {
            var key = QueryKey(item.Address, item.District);
            bool overridden = false;
            foreach (var (needle, lat, lon, overrideDistrict) in ManualOverrides)
            {
                if (item.Address.Contains(needle, StringComparison.OrdinalIgnoreCase))
                {
                    result[key] = new GeocoderResultDto
                    {
                        LatString = lat.ToString(System.Globalization.CultureInfo.InvariantCulture),
                        LonString = lon.ToString(System.Globalization.CultureInfo.InvariantCulture),
                        DisplayName = item.Address
                    };
                    overridden = true;
                    break;
                }
            }
            if (!overridden) toProcess.Add(item);
        }

        var queryKeys = toProcess
            .Select(x => QueryKey(x.Address, x.District))
            .Distinct()
            .ToList();

        var cachedMap = await cache.GetBatchByQueriesAsync(queryKeys, ct);

        int processed = distinctItems.Count - toProcess.Count;
        int total = distinctItems.Count;

        foreach (var (address, district) in toProcess)
        {
            var key = QueryKey(address, district);
            if (result.ContainsKey(key))
            {
                processed++;
                continue;
            }

            if (cachedMap.TryGetValue(key, out var cached) && cached is not null)
            {
                result[key] = cached.IsNotFound ? null : cached;
                processed++;
                progress?.Report((processed, total, $"Геокодирование адресов ({processed} из {total})"));
                continue;
            }

            var geo = await GeocodeInternalAsync(address, district, key, ct);
            result[key] = geo;
            processed++;
            progress?.Report((processed, total, $"Геокодирование адресов ({processed} из {total})"));
        }

        return result;
    }

    public static string QueryKey(string address, string? district)
        => string.IsNullOrWhiteSpace(district)
            ? address.Trim().ToLowerInvariant()
            : $"{address.Trim().ToLowerInvariant()} | {district.Trim().ToLowerInvariant()}";

    public async Task<(GeocoderResultDto? Result, bool IsHttpSuccess)> QueryNominatimWithStatusAsync(string query, CancellationToken ct)
    {
        const string regionViewbox = "36.0,56.8,39.5,54.3";
        var url = $"/search?q={Uri.EscapeDataString(query)}&format=json&limit=1&countrycodes=ru&viewbox={regionViewbox}&bounded=1";

        for (int attempt = 0; attempt < 2; attempt++)
        {
            try
            {
                await NominatimRateLimiter.WaitAsync(ct);

                using var response = await httpClient.GetAsync(url, ct);
                if ((int)response.StatusCode == 429 && attempt == 0)
                {
                    logger.LogWarning("Nominatim вернул статус 429 (Too Many Requests), повторная попытка через 1.5с для запроса \"{Query}\"", query);
                    await Task.Delay(1500, ct);
                    continue;
                }

                if (!response.IsSuccessStatusCode)
                {
                    logger.LogWarning("Nominatim вернул статус {StatusCode} для запроса \"{Query}\"", response.StatusCode, query);
                    return (null, false);
                }

                var results = await response.Content.ReadFromJsonAsync<List<GeocoderResultDto>>(ct);
                return (results?.FirstOrDefault(), true);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                if (attempt == 0)
                {
                    logger.LogWarning(ex, "Сбой при обращении к Nominatim, повторная попытка через 1с для \"{Query}\"", query);
                    await Task.Delay(1000, ct);
                    continue;
                }

                logger.LogWarning(ex, "Ошибка геокодирования запроса \"{Query}\"", query);
                return (null, false);
            }
        }

        return (null, false);
    }

    public async Task<GeocoderResultDto?> QueryNominatimAsync(string query, CancellationToken ct)
    {
        var (res, _) = await QueryNominatimWithStatusAsync(query, ct);
        return res;
    }

    public static List<string> BuildSearchQueries(string raw, string? district)
    {
        var queries = new List<string>();
        if (string.IsNullOrWhiteSpace(raw))
            return queries;

        string? cleanDistrict = null;
        if (!string.IsNullOrWhiteSpace(district))
        {
            cleanDistrict = Regex.Replace(district, @"\b(район|р-н)\b", "", RegexOptions.IgnoreCase).Trim(' ', ',');
        }

        string s = Regex.Replace(raw, @",?\s*(?:кв|оф|пом)\.?\s*\d+$", "", RegexOptions.IgnoreCase).Trim(' ', ',');

        string streetRaw = s;
        string houseRaw = string.Empty;

        var houseMatch = Regex.Match(s, @"[,\s]+(?:д\.|д\s+|дом\b)\s*(.+)$", RegexOptions.IgnoreCase);
        if (houseMatch.Success)
        {
            streetRaw = s[..houseMatch.Index].Trim(' ', ',');
            houseRaw = houseMatch.Groups[1].Value.Trim(' ', ',');
        }

        // Clean administrative prefixes
        string cleanStreet = Regex.Replace(streetRaw, @"\bобл\.Московская область\b[,\s]*", "", RegexOptions.IgnoreCase);
        cleanStreet = Regex.Replace(cleanStreet, @"\bМосковская область\b[,\s]*", "", RegexOptions.IgnoreCase);
        cleanStreet = Regex.Replace(cleanStreet, @"\bМО\b[,\s]*", "", RegexOptions.IgnoreCase);
        cleanStreet = Regex.Replace(cleanStreet, @"\bг\.Город\b[,\s]*", "", RegexOptions.IgnoreCase);
        cleanStreet = Regex.Replace(cleanStreet, @"\bГород\b\s*", "", RegexOptions.IgnoreCase);
        cleanStreet = Regex.Replace(cleanStreet, @"\bг\.\s*", "", RegexOptions.IgnoreCase);
        cleanStreet = Regex.Replace(cleanStreet, @"\bпгт\.\s*", "", RegexOptions.IgnoreCase);
        cleanStreet = Regex.Replace(cleanStreet, @"\bмкр\.\s*", "", RegexOptions.IgnoreCase);
        cleanStreet = Regex.Replace(cleanStreet, @"\bпос\.\s*", "", RegexOptions.IgnoreCase);
        cleanStreet = Regex.Replace(cleanStreet, @"([А-Яа-яЁё]+)-\d+\b", "$1");

        // City extraction
        string city = string.Empty;
        var cityMatch = Regex.Match(cleanStreet, @"^(Москва|Кашира|Домодедово|Ступино)\b[,\s]*", RegexOptions.IgnoreCase);
        if (cityMatch.Success)
        {
            city = char.ToUpperInvariant(cityMatch.Groups[1].Value[0]) + cityMatch.Groups[1].Value[1..].ToLowerInvariant();
            cleanStreet = cleanStreet[cityMatch.Length..].Trim(' ', ',');
        }
        else if (raw.Contains("Москва", StringComparison.OrdinalIgnoreCase))
        {
            city = "Москва";
        }

        cleanStreet = NormalizeStreet(cleanStreet);
        
        var streetVariants = GenerateStreetVariants(cleanStreet);
        var houseVariants = GenerateHouseVariants(houseRaw);
        foreach (var house in houseVariants)
        {
            foreach (var street in streetVariants)
            {
                var q = !string.IsNullOrEmpty(city)
                    ? $"{city}, {street}, {house}"
                    : $"{street}, {house}";

                if (!queries.Contains(q, StringComparer.OrdinalIgnoreCase))
                    queries.Add(q);
            }
        }

      
        if (!string.IsNullOrEmpty(cleanDistrict))
        {
            foreach (var house in houseVariants)
            {
                foreach (var street in streetVariants)
                {
                    var q = !string.IsNullOrEmpty(city)
                        ? $"{city}, район {cleanDistrict}, {street}, {house}"
                        : $"район {cleanDistrict}, {street}, {house}";

                    if (!queries.Contains(q, StringComparer.OrdinalIgnoreCase))
                        queries.Add(q);
                }
            }
        }
        
        foreach (var street in streetVariants)
        {
            var q = !string.IsNullOrEmpty(city)
                ? $"{city}, {street}"
                : street;

            if (!queries.Contains(q, StringComparer.OrdinalIgnoreCase))
                queries.Add(q);
        }

        return queries;
    }

    private static string NormalizeStreet(string street)
    {
        street = Regex.Replace(street, @"\b([А-Яа-яЁё0-9-]+)\s+(?:ул\b\.?|улица\b)", "улица $1", RegexOptions.IgnoreCase);
        street = Regex.Replace(street, @"\b([А-Яа-яЁё0-9-]+)\s+(?:пр-зд\b\.?|проезд\b)", "проезд $1", RegexOptions.IgnoreCase);

        street = Regex.Replace(street, @"\bул\.\s*", "улица ", RegexOptions.IgnoreCase);
        street = Regex.Replace(street, @"\b(проезд|пр-зд)\.\s*", "проезд ", RegexOptions.IgnoreCase);
        street = Regex.Replace(street, @"\bб-р\.\s*", "бульвар ", RegexOptions.IgnoreCase);
        street = Regex.Replace(street, @"\bпр-кт\.\s*", "проспект ", RegexOptions.IgnoreCase);
        street = Regex.Replace(street, @"\bш\.\s*", "шоссе ", RegexOptions.IgnoreCase);
        street = Regex.Replace(street, @"\bпер\.\s*", "переулок ", RegexOptions.IgnoreCase);
        street = Regex.Replace(street, @"\bнаб\.\s*", "набережная ", RegexOptions.IgnoreCase);

        street = Regex.Replace(street, @"(\d+)-([яеий])", "$1-$2");
        street = Regex.Replace(street, @"\s+", " ");
        street = Regex.Replace(street, @",\s*,", ",");

        return street.Trim(' ', ',', '.');
    }

    private static List<string> GenerateStreetVariants(string street)
    {
        var variants = new List<string>();
        if (string.IsNullOrWhiteSpace(street))
            return variants;

        const string streetTypes = @"улица|проезд|бульвар|проспект|переулок|шоссе|набережная";
        street = Regex.Replace(street,
            $@"\b(?!(?:{streetTypes})\b)([А-Яа-яЁё]+)\s+(\d+-(?:й|я|е|го|ая|ой|ья))\b",
            "$2 $1", RegexOptions.IgnoreCase);
        
        var ordMatch = Regex.Match(street,
            $@"\b(?:({streetTypes})\s+)?(\d+-(?:й|я|е|го|ая|ой|ья))\s+([А-Яа-яЁё-]+)(?:\s+({streetTypes}))?\b",
            RegexOptions.IgnoreCase);

        if (ordMatch.Success)
        {
            var typePre = ordMatch.Groups[1].Value;
            var num = ordMatch.Groups[2].Value;
            var name = ordMatch.Groups[3].Value;
            var typePost = ordMatch.Groups[4].Value;
            var mainType = !string.IsNullOrEmpty(typePost) ? typePost :
                           !string.IsNullOrEmpty(typePre) ? typePre : "улица";

            variants.Add($"{num} {name} {mainType}");
            variants.Add($"{num} {mainType} {name}");
            variants.Add($"{num} {name}");
            variants.Add($"{mainType} {num} {name}");
        }
        else
        {
            variants.Add(street);

            var noType = Regex.Replace(street, $@"\b({streetTypes})\b", "", RegexOptions.IgnoreCase);
            noType = Regex.Replace(noType, @"\s+", " ").Trim(' ', ',');
            if (!string.IsNullOrEmpty(noType) && !string.Equals(noType, street, StringComparison.OrdinalIgnoreCase))
            {
                variants.Add(noType);
            }
        }

        var parts = street.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length >= 2)
        {
            var lastPart = parts[^1];
            if (!variants.Contains(lastPart, StringComparer.OrdinalIgnoreCase))
                variants.Add(lastPart);
        }

        return variants.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static List<string> GenerateHouseVariants(string houseRaw)
    {
        var variants = new List<string>();
        if (string.IsNullOrWhiteSpace(houseRaw))
            return variants;

        string cleanHouse = Regex.Replace(houseRaw, @"(\d+)\s*с(?![а-яa-z])\.?\s*(\d+)", "$1 стр $2", RegexOptions.IgnoreCase);
        cleanHouse = Regex.Replace(cleanHouse, @"\s*к(?:орп)?\.?\s*(\d+)", "к$1", RegexOptions.IgnoreCase);
        cleanHouse = Regex.Replace(cleanHouse, @"\s*стр\.?\s*(\d+)", " стр $1", RegexOptions.IgnoreCase);
        cleanHouse = Regex.Replace(cleanHouse, @"\s+", " ").Trim(' ', ',');

        if (string.IsNullOrEmpty(cleanHouse))
            return variants;

        variants.Add(cleanHouse);

        var withoutStr = Regex.Replace(cleanHouse, @"\s*стр\.?\s*\d+", "", RegexOptions.IgnoreCase).Trim();
        if (!string.IsNullOrEmpty(withoutStr) && withoutStr != cleanHouse)
        {
            variants.Add(withoutStr);
        }

        var baseHouse = withoutStr.Length > 0 ? withoutStr : cleanHouse;
        var withoutCorp = Regex.Replace(baseHouse, @"к(?:орп)?\.?\s*\d+", "", RegexOptions.IgnoreCase).Trim();
        if (!string.IsNullOrEmpty(withoutCorp) && !variants.Contains(withoutCorp, StringComparer.OrdinalIgnoreCase))
        {
            variants.Add(withoutCorp);
        }

        var slashMatch = Regex.Match(withoutCorp, @"^(\d+)/\d+");
        if (slashMatch.Success && !variants.Contains(slashMatch.Groups[1].Value, StringComparer.OrdinalIgnoreCase))
        {
            variants.Add(slashMatch.Groups[1].Value);
        }

        return variants;
    }
}