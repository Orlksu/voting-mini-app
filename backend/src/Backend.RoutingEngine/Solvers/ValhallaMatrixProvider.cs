using Backend.Contracts.Domain.Enums;
using Backend.Contracts.Domain.ValueObjects;
using Backend.RoutingEngine.Clients;
using Microsoft.Extensions.Logging;

namespace Backend.RoutingEngine.Solvers;

/// <summary>
/// Фаза B: матрицы «точка-точка» через Valhalla с costing по типу транспорта бригады.
///
/// Для каждого из встречающихся типов транспорта запрашивается одна матрица
/// [старты бригад этого типа + все заявки], из которой вырезаются пер-бригадные матрицы:
/// строка «старт бригады» и строки заявок берутся из матрицы её costing.
///
/// Если Valhalla недоступна — ValhallaClient сам отдаёт Haversine-значения (фолбэк внутри
/// клиента); в крайнем случае (исключение выше слоя клиента) — переключаемся на
/// HaversineMatrixProvider в целом.
/// </summary>
public sealed class ValhallaMatrixProvider : IRouteMatrixProvider
{
    private readonly IValhallaClient _valhalla;
    private readonly HaversineMatrixProvider _haversine;
    private readonly ILogger<ValhallaMatrixProvider> _logger;

    public ValhallaMatrixProvider(
        IValhallaClient valhalla,
        HaversineMatrixProvider haversine,
        ILogger<ValhallaMatrixProvider> logger)
    {
        _valhalla = valhalla;
        _haversine = haversine;
        _logger = logger;
    }

    public async Task<RouteMatrices> BuildAsync(
        IReadOnlyList<(double Lat, double Lon)> locations,
        IReadOnlyList<TransportType> vehicleTransports,
        CancellationToken cancellationToken = default)
    {
        int vehicles = vehicleTransports.Count;
        int orders = Math.Max(0, locations.Count - vehicles);
        int total = vehicles + orders;

        var distances = new double[vehicles][,];
        var times = new long[vehicles][,];
        for (int v = 0; v < vehicles; v++)
        {
            distances[v] = new double[total, total];
            times[v] = new long[total, total];
        }

        try
        {
            var orderLocations = locations.Skip(vehicles).ToArray();

            foreach (var group in vehicleTransports
                         .Select((transport, index) => (Transport: transport, Index: index))
                         .GroupBy(x => x.Transport))
            {
                cancellationToken.ThrowIfCancellationRequested();

                var members = group.ToList();
                string costing = ToValhallaCosting(group.Key);

                var requestLocations = members
                    .Select(m => ToLocation(locations[m.Index]))
                    .Concat(orderLocations.Select(ToLocation))
                    .ToArray();

                ValhallaMatrix matrix = await _valhalla.GetMatrixAsync(requestLocations, costing, cancellationToken);

                // Строки матрицы Valhalla: [0..members.Count-1] — старты бригад группы,
                // затем все заявки. Копируем их в пер-бригадные матрицы (для каждой бригады группы).
                for (int p = 0; p < members.Count; p++)
                {
                    int vIdx = members[p].Index;
                    CopyRow(matrix, sourceRow: p, targetRow: vIdx, groupPos: p,
                        members.Count, distances[vIdx], times[vIdx], vehicles, orders, vIdx);
                }

                for (int o = 0; o < orders; o++)
                {
                    for (int p = 0; p < members.Count; p++)
                    {
                        int vIdx = members[p].Index;
                        CopyRow(matrix, sourceRow: members.Count + o, targetRow: vehicles + o, groupPos: p,
                            members.Count, distances[vIdx], times[vIdx], vehicles, orders, vIdx);
                    }
                }

                _logger.LogInformation("Valhalla matrix ready for transport {Transport} ({Costing}): {Members} engineers, {Orders} orders",
                    group.Key, costing, members.Count, orders);
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Valhalla matrix build failed, falling back to Haversine matrices");
            return await _haversine.BuildAsync(locations, vehicleTransports, cancellationToken);
        }

        return new RouteMatrices(distances, times);
    }

    /// <summary>
    /// Копирует строку sourceRow матрицы Valhalla в матрицу бригады targetRow.
    /// Колонки: старт бригады (groupPos в M, vIdx в пер-бригадной) + все заявки.
    /// </summary>
    private static void CopyRow(
        ValhallaMatrix matrix,
        int sourceRow,
        int targetRow,
        int groupPos,
        int membersCount,
        double[,] distances,
        long[,] times,
        int vehicles,
        int orders,
        int vIdx)
    {
        times[targetRow, vIdx] = SecToMinutes(matrix.TimeSeconds[sourceRow, groupPos]);
        distances[targetRow, vIdx] = MetersToKm(matrix.DistanceMeters[sourceRow, groupPos]);

        for (int o = 0; o < orders; o++)
        {
            times[targetRow, vehicles + o] = SecToMinutes(matrix.TimeSeconds[sourceRow, membersCount + o]);
            distances[targetRow, vehicles + o] = MetersToKm(matrix.DistanceMeters[sourceRow, membersCount + o]);
        }
    }

    private static long SecToMinutes(double seconds) => (long)Math.Ceiling(seconds / 60.0);

    private static double MetersToKm(double meters) => Math.Round(meters / 1000.0, 2);

    private static string ToValhallaCosting(TransportType transport) => transport switch
    {
        TransportType.Car => "auto",
        TransportType.PublicTransit => "transit",
        TransportType.Bicycle => "bicycle",
        TransportType.Pedestrian => "pedestrian",
        _ => "auto"
    };

    private static Location ToLocation((double Lat, double Lon) point) => new()
    {
        Coordinates = new GeoCoordinate(point.Lat, point.Lon)
    };
}