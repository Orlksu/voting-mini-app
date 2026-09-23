using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Backend.Api.Infrastructure.Repositories;

namespace Backend.Api.Services.GeoCodingService;

public class GeocoderService(HttpClient httpClient, IGeocodeCacheRepository cache) : IGeocoder
{
    public async Task<GeocoderResultDto?> GeocodeAsync(string address, string? district = null, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(address))
            return null;

        var rawQuery = QueryKey(address, district);

        var cached = await cache.GetByQueryAsync(rawQuery, cancellationToken);
        if (cached != null)
            return cached;

        var searchQueries = BuildSearchQueries(address, district);

        foreach (var query in searchQueries)
        {
            var result = await QueryNominatimAsync(query, cancellationToken);
            if (result != null)
            {
                await cache.SaveAsync(rawQuery, result.Latitude, result.Longitude, cancellationToken);
                return result;
            }
        }

        return null;
    }

    private static string QueryKey(string address, string? district)
        => string.IsNullOrWhiteSpace(district)
            ? address.Trim()
            : $"{address.Trim()} | {district.Trim()}";

    public async Task<GeocoderResultDto?> QueryNominatimAsync(string query, CancellationToken ct)
    {
        try
        {
            const string mskViewbox = "37.1,56.1,38.5,54.7";

            var url = $"/search?q={Uri.EscapeDataString(query)}&format=json&limit=1&countrycodes=ru&viewbox={mskViewbox}&bounded=1";
        
            var results = await httpClient.GetFromJsonAsync<List<GeocoderResultDto>>(url, ct);

            await Task.Delay(1100, ct);

            return results?.FirstOrDefault();
        }
        catch
        {
            return null;
        }
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
        string houseRaw = "";

        var houseMatch = Regex.Match(s, @"(?:[,\s]+д\.?\s*)(.+)$", RegexOptions.IgnoreCase);
        if (houseMatch.Success)
        {
            streetRaw = s.Substring(0, houseMatch.Index).Trim(' ', ',');
            houseRaw = houseMatch.Groups[1].Value.Trim(' ', ',');
        }

        string cleanStreet = Regex.Replace(streetRaw, @"\bобл\.Московская область\b[,\s]*", "", RegexOptions.IgnoreCase);
        cleanStreet = Regex.Replace(cleanStreet, @"\bМО\b[,\s]*", "", RegexOptions.IgnoreCase);
        cleanStreet = Regex.Replace(cleanStreet, @"\bг\.Город\b[,\s]*", "", RegexOptions.IgnoreCase);
        cleanStreet = Regex.Replace(cleanStreet, @"\bГород\b\s*", "", RegexOptions.IgnoreCase);
        cleanStreet = Regex.Replace(cleanStreet, @"\bг\.\s*", "", RegexOptions.IgnoreCase);
        cleanStreet = Regex.Replace(cleanStreet, @"\bпгт\.\s*", "", RegexOptions.IgnoreCase);
        cleanStreet = Regex.Replace(cleanStreet, @"\bмкр\.\s*", "", RegexOptions.IgnoreCase);
        cleanStreet = Regex.Replace(cleanStreet, @"\bпос\.\s*", "", RegexOptions.IgnoreCase);

        cleanStreet = Regex.Replace(cleanStreet, @"([А-Яа-яЁё]+)-\d+\b", "$1");
        cleanStreet = Regex.Replace(cleanStreet, @"^(Москва|Кашира|Домодедово|Ступино)\s+([А-Яа-яЁё0-9])", "$1, $2", RegexOptions.IgnoreCase);

        cleanStreet = NormalizeStreet(cleanStreet);

        var parts = cleanStreet.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var baseLocations = new List<string>();

        if (parts.Length >= 3)
        {
            string city = parts[0];
            string subArea = parts[1];
            string street = parts[^1];

            baseLocations.Add($"микрорайон {subArea}, {street}");
            baseLocations.Add($"{subArea}, {street}");
            baseLocations.Add($"{city}, {street}");
            baseLocations.Add($"{city}, {subArea}, {street}");
        }
        else
        {
            baseLocations.Add(cleanStreet);
        }

        var expandedLocations = new List<string>();
        foreach (var loc in baseLocations)
        {
            expandedLocations.Add(loc);
            string noType = Regex.Replace(loc, @"\b(улица|проезд|бульвар|проспект|шоссе|переулок|набережная)\b", "", RegexOptions.IgnoreCase);
            noType = Regex.Replace(noType, @"\s+", " ").Trim(' ', ',');
            if (!string.Equals(noType, loc, StringComparison.OrdinalIgnoreCase))
            {
                expandedLocations.Add(noType);
            }
        }
        
        if (raw.Contains("Москва", StringComparison.OrdinalIgnoreCase))
        {
            for (int i = 0; i < expandedLocations.Count; i++)
            {
                var trimmed = expandedLocations[i].Trim(' ', ',');
                if (!trimmed.StartsWith("Москва", StringComparison.OrdinalIgnoreCase))
                {
                    expandedLocations[i] = $"Москва, {trimmed}";
                }
            }
        }

        string cleanHouse = NormalizeHouse(houseRaw);

        foreach (var loc in expandedLocations.Distinct())
        {
            string? locWithDistrict = null;
            if (!string.IsNullOrEmpty(cleanDistrict))
            {
                locWithDistrict = loc.StartsWith("Москва", StringComparison.OrdinalIgnoreCase)
                    ? loc.Insert("Москва, ".Length, $"район {cleanDistrict}, ")
                    : $"район {cleanDistrict}, {loc}";
            }

            var activeLocations = new List<string>();
            if (locWithDistrict != null) 
                activeLocations.Add(locWithDistrict);
            
            activeLocations.Add(loc);

            foreach (var currentLoc in activeLocations)
            {
                if (!string.IsNullOrEmpty(cleanHouse))
                {
                    queries.Add($"{currentLoc}, {cleanHouse}");

                    if (currentLoc.Contains('ё') || currentLoc.Contains('Ё'))
                    {
                        queries.Add($"{currentLoc.Replace('ё', 'е').Replace('Ё', 'Е')}, {cleanHouse}");
                    }

                    string withoutStr = Regex.Replace(cleanHouse, @"\s*стр\.?\s*\d+", "", RegexOptions.IgnoreCase).Trim();
                    if (!string.IsNullOrEmpty(withoutStr) && withoutStr != cleanHouse)
                    {
                        queries.Add($"{currentLoc}, {withoutStr}");
                    }

                    string withoutCorp = Regex.Replace(withoutStr, @"к(?:орп)?\.?\s*\d+", "", RegexOptions.IgnoreCase).Trim();
                    if (!string.IsNullOrEmpty(withoutCorp) && withoutCorp != withoutStr)
                    {
                        queries.Add($"{currentLoc}, {withoutCorp}");
                    }

                    if (Regex.IsMatch(withoutCorp, @"^\d+/\d+"))
                    {
                        string baseNum = Regex.Replace(withoutCorp, @"^(\d+)/\d+.*", "$1");
                        queries.Add($"{currentLoc}, {baseNum}");
                    }
                }

                queries.Add(currentLoc);
            }
        }

        return queries.Distinct().ToList();
    }

    private static string NormalizeStreet(string street)
    {
        street = Regex.Replace(street, @"\b([А-Яа-яЁё0-9-]+)\s+(?:ул\.|ул)\b", "улица $1", RegexOptions.IgnoreCase);
        street = Regex.Replace(street, @"\b([А-Яа-яЁё0-9-]+)\s+(?:пр-зд\.|пр-зд|проезд)\b", "проезд $1", RegexOptions.IgnoreCase);

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

        return street.Trim(' ', ',');
    }

    private static string NormalizeHouse(string house)
    {
        if (string.IsNullOrWhiteSpace(house))
            return string.Empty;

        string h = Regex.Replace(house, @"\s*к(?:орп)?\.?\s*(\d+)", "к$1", RegexOptions.IgnoreCase);
        h = Regex.Replace(h, @"\s*стр\.?\s*(\d+)", " стр $1", RegexOptions.IgnoreCase);
        h = Regex.Replace(h, @"\s+", " ");
        return h.Trim(' ', ',');
    }
}