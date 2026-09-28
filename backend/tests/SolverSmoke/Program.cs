using Backend.Contracts.Domain.Enums;
using Backend.Contracts.Requests;
using Backend.Contracts.Responses;
using Backend.RoutingEngine;
using Backend.RoutingEngine.Solvers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

Console.OutputEncoding = System.Text.Encoding.UTF8;
Console.WriteLine("=== SMOKE TEST: Фаза A (открытые маршруты + обеды + приоритеты) ===\n");

var date = new DateTimeOffset(2026, 9, 24, 0, 0, 0, TimeSpan.FromHours(3));

// Заявки (Москва, разброс по координатам)
var orders = new List<WorkOrderInputDto>
{
    new()
    {
        ExternalId = "ORD-1", Address = "Ул. Тверская 1",
        Latitude = 55.7558, Longitude = 37.6176,
        WindowStart = date.AddHours(9), WindowEnd = date.AddHours(12),
        RequiredSkill = SkillType.Installation, RequiredTransport = TransportType.Car,
        TaskType = "Подключение клиентов Базовая", Priority = OrderPriority.High,
        DurationMinutes = 90,
    },
    new()
    {
        ExternalId = "ORD-2", Address = "Ул. Арбат 2",
        Latitude = 55.7497, Longitude = 37.5907,
        WindowStart = date.AddHours(13), WindowEnd = date.AddHours(16),
        RequiredSkill = SkillType.Installation, RequiredTransport = TransportType.Car,
        TaskType = "Подключение клиентов Базовая", Priority = OrderPriority.High,
        DurationMinutes = 90,
    },
    new()
    {
        ExternalId = "ORD-3", Address = "Ул. Пятницкая 3",
        Latitude = 55.7444, Longitude = 37.6276,
        WindowStart = date.AddHours(10), WindowEnd = date.AddHours(15),
        RequiredSkill = SkillType.LocalRepair, RequiredTransport = TransportType.PublicTransit,
        TaskType = "Локальная заявка ТКД", Priority = OrderPriority.Normal,
        DurationMinutes = 50,
    },
    new()
    {
        ExternalId = "ORD-4", Address = "Ул. Зацепский Вал 4",
        Latitude = 55.7285, Longitude = 37.6400,
        WindowStart = date.AddHours(10), WindowEnd = date.AddHours(12),
        RequiredSkill = SkillType.LocalRepair, RequiredTransport = null,
        TaskType = "Ремонт оборудования", Priority = OrderPriority.Normal,
        DurationMinutes = 50,
    },
    new()
    {
        ExternalId = "ORD-5", Address = "Ул. Мясницкая 5",
        Latitude = 55.7627, Longitude = 37.6344,
        WindowStart = date.AddHours(12), WindowEnd = date.AddHours(18),
        RequiredSkill = SkillType.Emergency, RequiredTransport = null,
        TaskType = "Авария на ТКД", Priority = OrderPriority.Emergency,
        DurationMinutes = 100,
    },
    new()
    {
        // Заявка БЕЗ координат — должна уйти в неназначенные с MissingCoordinates.
        ExternalId = "ORD-6", Address = "Неизвестный адрес",
        Latitude = null, Longitude = null,
        WindowStart = date.AddHours(9), WindowEnd = date.AddHours(18),
        RequiredSkill = SkillType.LocalRepair, RequiredTransport = null,
        TaskType = "Дозаказ оборудования", Priority = OrderPriority.Low,
        DurationMinutes = 40,
    },
    new()
    {
        // Завершённая заявка — не участвует в планировании.
        ExternalId = "ORD-7", Address = "Ул. Новая 7",
        Latitude = 55.7500, Longitude = 37.6000,
        WindowStart = date.AddHours(9), WindowEnd = date.AddHours(18),
        RequiredSkill = SkillType.Installation, RequiredTransport = TransportType.Car,
        TaskType = "Подключение клиентов Базовая", Priority = OrderPriority.High,
        DurationMinutes = 90, Status = OrderStatus.Completed,
    },
};

var engineers = new List<EngineerInputDto>
{
    new(
        ExternalId: "ENG-1", Name: "Алексей Иванов", StartAddress: "Офис 1 (ТТК)",
        StartLatitude: 55.7660, StartLongitude: 37.6202,
        ShiftStart: date.AddHours(9), ShiftEnd: date.AddHours(18),
        Transport: TransportType.Car,
        Skills: [SkillType.Installation, SkillType.LocalRepair]),
    new(
        ExternalId: "ENG-2", Name: "Мария Петрова", StartAddress: "Офис 2 (Павелецкая)",
        StartLatitude: 55.7298, StartLongitude: 37.6368,
        ShiftStart: date.AddHours(9), ShiftEnd: date.AddHours(18),
        Transport: TransportType.PublicTransit,
        Skills: [SkillType.LocalRepair, SkillType.Emergency]),
    // Инженер с пустой сменой — должен быть отсеян препроцессором.
    new(
        ExternalId: "ENG-3", Name: "Пустой Иван", StartAddress: "Офис 3",
        StartLatitude: 55.7000, StartLongitude: 37.6000,
        ShiftStart: date.AddHours(12), ShiftEnd: date.AddHours(10),
        Transport: TransportType.Car,
        Skills: [SkillType.Installation, SkillType.LocalRepair, SkillType.Emergency]),
};

var request = new CreateOptimizationPlanRequest(date, engineers, orders, TimeLimitSeconds: 10);

var services = new ServiceCollection();
// По умолчанию — Valhalla на хосте (порт из docker-compose). Переопределить: $env:VALHALLA_URL="http://valhalla:8002"
var valhallaUrl = Environment.GetEnvironmentVariable("VALHALLA_URL") ?? "http://localhost:8002";
services.AddRoutingEngine(valhallaUrl);
services.AddLogging(b => b.AddSimpleConsole(o => o.SingleLine = true));
await using var provider = services.BuildServiceProvider();

var optimizer = provider.GetRequiredService<Backend.Contracts.Interfaces.IRouteOptimizer>();
var result = await optimizer.OptimizeAsync(request);

Console.WriteLine("--- МАРШРУТЫ ---");
foreach (var route in result.Routes)
{
    Console.WriteLine($"\n[{route.EngineerExternalId}] {route.EngineerName} ({route.Transport}), " +
                      $"заявок: {route.TotalOrders}, км: {route.TotalDistanceKm}, " +
                      $"в пути: {route.TotalTravelMinutes} мин");
    foreach (var b in route.BreakStops)
        Console.WriteLine($"    ОБЕД: {b.Start:HH:mm}–{b.End:HH:mm} ({b.DurationMinutes} мин)");
    foreach (var s in route.Stops)
        Console.WriteLine($"    #{s.Sequence} {s.ExternalOrderId} {s.Address} | " +
                          $"прибытие {s.EstimatedArrival:HH:mm}, работa {s.ServiceStart:HH:mm}–{s.ServiceEnd:HH:mm}, " +
                          $"дорога {s.TravelDurationMinutes} мин, {s.DistanceFromPrevKm} км | {s.TaskType} ({s.Priority})");
}

Console.WriteLine("\n--- НЕНАЗНАЧЕННЫЕ ---");
foreach (var u in result.UnassignedOrders)
    Console.WriteLine($"    {u.ExternalOrderId} {u.Address}: [{u.ReasonCode}] {u.ReasonDescription}");

var m = result.Metrics;
Console.WriteLine("\n--- МЕТРИКИ ---");
Console.WriteLine($"    Всего заявок: {m.TotalOrders}, назначено: {m.AssignedOrders}, неназначено: {m.UnassignedOrders}");
Console.WriteLine($"    Бригад в оптимизации: {m.OptimizedActiveEngineers} / базово: {m.BaselineActiveEngineers}, сэкономлено: {m.SavedEngineersCount}");
Console.WriteLine($"    Дистанция: {m.OptimizedTotalDistanceKm} км (baseline {m.BaselineTotalDistanceKm} км), экономия {m.DistanceEconomyPercent}%");

var assignedIds = result.Routes.SelectMany(r => r.Stops).Select(s => s.ExternalOrderId).ToHashSet();
var unassignedIds = result.UnassignedOrders.Select(u => u.ExternalOrderId).ToHashSet();
var allKnown = assignedIds.Concat(unassignedIds).ToHashSet();

Console.WriteLine("\n--- ПРОВЕРКИ ---");
Console.WriteLine($"    Все заявки учтены (назначены или неназначены): {allKnown.Count == orders.Count} ({allKnown.Count}/{orders.Count})");
Console.WriteLine($"    ORD-6 (без координат) → MissingCoordinates: {unassignedIds.Contains("ORD-6")}");
Console.WriteLine($"    ORD-7 (завершена) не в планировании: {!allKnown.Contains("ORD-7")}");
Console.WriteLine($"    ENG-3 (пустая смена) отсеян: {!result.Routes.Any(r => r.EngineerExternalId == "ENG-3") && result.Metrics.OptimizedActiveEngineers == 2}");
foreach (var route in result.Routes)
{
    var first = route.Stops.FirstOrDefault();
    if (first != null && first.ServiceEnd > date.AddHours(18))
        Console.WriteLine($"    [!!!] {route.EngineerExternalId}: ServiceEnd позже 18:00 — нарушение смены");
}
foreach (var route in result.Routes)
{
    foreach (var s in route.Stops)
    {
        var order = orders.First(o => o.ExternalId == s.ExternalOrderId);
        if (s.ServiceEnd > order.WindowEnd)
            Console.WriteLine($"    [!!!] {s.ExternalOrderId}: ServiceEnd {s.ServiceEnd:HH:mm} позже WindowEnd {order.WindowEnd:HH:mm} — нарушение окна");
    }
}
Console.WriteLine("\n--- ПРОВЕРКИ ОБЕДОВ ---");
foreach (var route in result.Routes)
{
    foreach (var b in route.BreakStops)
    {
        if (b.Start < date.AddHours(9) || b.End > date.AddHours(18))
            Console.WriteLine($"    [!!!] {route.EngineerExternalId}: обед {b.Start:HH:mm}–{b.End:HH:mm} вне смены 09:00–18:00");
        foreach (var s in route.Stops)
        {
            bool overlap = b.Start < s.ServiceEnd && s.ServiceStart < b.End;
            if (overlap)
                Console.WriteLine($"    [!!!] {route.EngineerExternalId}: обед {b.Start:HH:mm}–{b.End:HH:mm} пересекается с {s.ExternalOrderId} ({s.ServiceStart:HH:mm}–{s.ServiceEnd:HH:mm})");
        }
    }
}
Console.WriteLine("\n=== ГОТОВО ===");

Console.WriteLine("\n\n=== СТРЕСС-ТЕСТ (4 бригады, 12 заявок) ===");
var date2 = new DateTimeOffset(2026, 9, 25, 0, 0, 0, TimeSpan.FromHours(3));
var baseLat = 55.75;
var baseLon = 37.617;

var engineer2 = new List<EngineerInputDto>
{
    new("ENG-11", "Смирнов Олег", "Офис А", baseLat + 0.01, baseLon + 0.008, date2.AddHours(8), date2.AddHours(20), TransportType.Car, [SkillType.Installation, SkillType.LocalRepair]),
    new("ENG-12", "Кузнецов Пётр", "Офис Б", baseLat - 0.008, baseLon + 0.005, date2.AddHours(8), date2.AddHours(20), TransportType.Car, [SkillType.Installation, SkillType.LocalRepair]),
    new("ENG-13", "Соколова Анна", "Офис В", baseLat + 0.005, baseLon - 0.009, date2.AddHours(8), date2.AddHours(20), TransportType.Car, [SkillType.Emergency, SkillType.LocalRepair]),
    new("ENG-14", "Волков Дмитрий", "Офис Г", baseLat - 0.01, baseLon - 0.006, date2.AddHours(8), date2.AddHours(20), TransportType.PublicTransit, [SkillType.LocalRepair, SkillType.Installation]),
};

var orders2 = new List<WorkOrderInputDto>();
void AddOrder(string id, SkillType skill, TransportType? transport, string task, OrderPriority prio, int dur, double lat, double lon, int ws, int we)
{
    orders2.Add(new WorkOrderInputDto
    {
        ExternalId = id,
        Address = $"Стресс-адрес {id}",
        Latitude = lat,
        Longitude = lon,
        WindowStart = date2.AddHours(ws),
        WindowEnd = date2.AddHours(we),
        RequiredSkill = skill,
        RequiredTransport = transport,
        TaskType = task,
        Priority = prio,
        DurationMinutes = dur,
    });
}

// 12 заявок: 4 подключения (Car), 4 ремонта (любой), 2 аварии, 2 дозаказа
for (int k = 0; k < 4; k++)
    AddOrder($"S-INST-{k}", SkillType.Installation, TransportType.Car, "Подключение клиентов Базовая", OrderPriority.High, 90,
        baseLat + (k % 2 == 0 ? 0.012 : -0.012) + k * 0.001, baseLon + (k / 2 == 0 ? 0.012 : -0.012), 8 + k, 13 + k);
for (int k = 0; k < 4; k++)
    AddOrder($"S-REP-{k}", SkillType.LocalRepair, null, "Локальная заявка ТКД", OrderPriority.Normal, 50,
        baseLat + (k % 2 == 0 ? -0.015 : 0.015), baseLon + (k / 2 == 0 ? -0.013 : 0.014), 9 + k, 16 + k);
AddOrder("S-EM-0", SkillType.Emergency, null, "Авария на ТКД", OrderPriority.Emergency, 100, baseLat + 0.018, baseLon + 0.016, 10, 19);
AddOrder("S-EM-1", SkillType.Emergency, null, "Авария на ТКД", OrderPriority.Emergency, 100, baseLat - 0.019, baseLon - 0.017, 12, 19);
AddOrder("S-DOP-0", SkillType.LocalRepair, null, "Дозаказ оборудования", OrderPriority.Low, 40, baseLat + 0.003, baseLon - 0.004, 8, 19);
AddOrder("S-DOP-1", SkillType.LocalRepair, null, "Дозаказ оборудования", OrderPriority.Low, 40, baseLat - 0.004, baseLon + 0.006, 9, 19);

var request2 = new CreateOptimizationPlanRequest(date2, engineer2, orders2, TimeLimitSeconds: 20);
var sw = System.Diagnostics.Stopwatch.StartNew();
var result2 = await optimizer.OptimizeAsync(request2);
sw.Stop();

Console.WriteLine($"    Время решения: {sw.ElapsedMilliseconds} мс (лимит 20 с)");
Console.WriteLine($"    Назначено: {result2.Metrics.AssignedOrders}/{result2.Metrics.TotalOrders}, маршрутов с заявками: "
                  + $"{result2.Metrics.OptimizedActiveEngineers} из {result2.Metrics.BaselineActiveEngineers} бригад");
foreach (var route in result2.Routes)
{
    Console.WriteLine($"    [{route.EngineerExternalId}] заявок {route.TotalOrders}, обеды {route.BreakStops.Count}: "
                      + (route.BreakStops.Count > 0 ? $"{route.BreakStops[0].Start:HH:mm}–{route.BreakStops[0].End:HH:mm}" : "—"));
}

int wv = 0;
foreach (var route in result2.Routes)
    foreach (var s in route.Stops)
    {
        var order = orders2.First(o => o.ExternalId == s.ExternalOrderId);
        if (s.ServiceEnd > order.WindowEnd) wv++;
    }
int lunchOverlaps = 0;
foreach (var route in result2.Routes)
    foreach (var b in route.BreakStops)
        foreach (var s in route.Stops)
            if (b.Start < s.ServiceEnd && s.ServiceStart < b.End) lunchOverlaps++;
Console.WriteLine($"    Нарушений окон: {wv}, пересечений обедов с работами: {lunchOverlaps}");
Console.WriteLine("=== СТРЕСС-ТЕСТ ЗАВЕРШЁН ===");

Console.WriteLine("\n\n=== ФАЗА B: ПРАВИЛО «СНАЧАЛА МАКСИМУМ ЗАЯВОК, ПОТОМ МИНИМУМ БРИГАД» ===");
Console.WriteLine("(Valhalla недоступна — матрицы через Haversine-фолбэк; правило проверяется на структуре задачи)\n");

var dateB = new DateTimeOffset(2026, 9, 26, 0, 0, 0, TimeSpan.FromHours(3));

EngineerInputDto Eng(string id, DateTimeOffset d) => new(
    ExternalId: id, Name: $"Инженер {id}", StartAddress: "База",
    StartLatitude: 55.7558, StartLongitude: 37.6176,
    ShiftStart: d.AddHours(9), ShiftEnd: d.AddHours(12.5),
    Transport: TransportType.Car,
    Skills: [SkillType.Installation],
    LunchStart: d.AddHours(11.5), LunchEnd: d.AddHours(12.5));

// R1: 3 заявки выполнимы ТОЛЬКО двумя бригадами (окна не позволяют одной бригаде
// успеть все три). Правило: «больше заявок важнее, чем меньше бригад» → 3 заявки, 2 бригады.
var r1Engineers = new List<EngineerInputDto> { Eng("R1-E1", dateB), Eng("R1-E2", dateB) };
var r1Orders = new List<WorkOrderInputDto>
{
    new() { ExternalId = "R1-O1", Address = "Близко 1", Latitude = 55.7560, Longitude = 37.6180,
        WindowStart = dateB.AddMinutes(540), WindowEnd = dateB.AddMinutes(615),
        RequiredSkill = SkillType.Installation, TaskType = "Тест: заявка в окне",
        Priority = OrderPriority.High, DurationMinutes = 50 },
    new() { ExternalId = "R1-O2", Address = "Близко 2", Latitude = 55.7562, Longitude = 37.6182,
        WindowStart = dateB.AddMinutes(540), WindowEnd = dateB.AddMinutes(615),
        RequiredSkill = SkillType.Installation, TaskType = "Тест: заявка в окне",
        Priority = OrderPriority.Normal, DurationMinutes = 50 },
    new() { ExternalId = "R1-O3", Address = "Дальняя", Latitude = 55.7300, Longitude = 37.5400,
        WindowStart = dateB.AddMinutes(630), WindowEnd = dateB.AddMinutes(690),
        RequiredSkill = SkillType.Installation, TaskType = "Тест: заявка в окне",
        Priority = OrderPriority.High, DurationMinutes = 50 },
};
var r1Request = new CreateOptimizationPlanRequest(dateB, r1Engineers, r1Orders, TimeLimitSeconds: 15);
var r1 = await optimizer.OptimizeAsync(r1Request);
Console.WriteLine("--- R1: максимум заявок важнее минимума бригад ---");
foreach (var u in r1.UnassignedOrders)
    Console.WriteLine($"    [R1-unassigned] {u.ExternalOrderId} reason={u.ReasonCode}");
bool r1O2 = r1.Routes.SelectMany(x => x.Stops).Any(s => s.ExternalOrderId == "R1-O2");
bool r1Ok = r1.Metrics.AssignedOrders == 3 && r1.Metrics.OptimizedActiveEngineers == 2 && r1O2;
Console.WriteLine($"    [R1-result] assigned={r1.Metrics.AssignedOrders}/3 active={r1.Metrics.OptimizedActiveEngineers}/2 r1o2={r1O2} RULE={r1Ok}");

// R2: 2 заявки выполнимы одной бригадой. Равное число заявок → минимум бригад: 1 бригада.
var r2Engineers = new List<EngineerInputDto> { Eng("R2-E1", dateB), Eng("R2-E2", dateB) };
var r2Orders = new List<WorkOrderInputDto>
{
    new() { ExternalId = "R2-O1", Address = "Близко 1", Latitude = 55.7560, Longitude = 37.6180,
        WindowStart = dateB.AddMinutes(540), WindowEnd = dateB.AddMinutes(620),
        RequiredSkill = SkillType.Installation, TaskType = "Тест: заявка в окне",
        Priority = OrderPriority.High, DurationMinutes = 60 },
    new() { ExternalId = "R2-O2", Address = "Близко 2", Latitude = 55.7562, Longitude = 37.6182,
        WindowStart = dateB.AddMinutes(630), WindowEnd = dateB.AddMinutes(690),
        RequiredSkill = SkillType.Installation, TaskType = "Тест: заявка в окне",
        Priority = OrderPriority.High, DurationMinutes = 40 },
};
var r2Request = new CreateOptimizationPlanRequest(dateB, r2Engineers, r2Orders, TimeLimitSeconds: 15);
var r2 = await optimizer.OptimizeAsync(r2Request);
Console.WriteLine("\n--- R2: минимум бригад при равном числе заявок ---");
bool r2Ok = r2.Metrics.AssignedOrders == 2 && r2.Metrics.OptimizedActiveEngineers == 1;
Console.WriteLine($"    [R2-result] assigned={r2.Metrics.AssignedOrders}/2 active={r2.Metrics.OptimizedActiveEngineers}/2 RULE={r2Ok}");

Console.WriteLine("\n=== ФАЗА B ЗАВЕРШЕНА ===");

Console.WriteLine("\n\n=== ФАЗА C: ЛИМИТ СМЕНЫ + ОБОРУДОВАНИЕ + СИНТЕТИКА 40/40/10/10 ===");

var dateC = new DateTimeOffset(2026, 9, 27, 0, 0, 0, TimeSpan.FromHours(3));

// C1: MaxOrdersPerShift — счётная дименсия в реальном солвере (причина ShiftLimitExceeded).
var c1Engineer = new EngineerInputDto(
    ExternalId: "C1-E1", Name: "Инженер C1", StartAddress: "База",
    StartLatitude: 55.7558, StartLongitude: 37.6176,
    ShiftStart: dateC.AddHours(9), ShiftEnd: dateC.AddHours(18),
    Transport: TransportType.Car,
    Skills: [SkillType.Installation],
    MaxOrdersPerShift: 1);
var c1Orders = new List<WorkOrderInputDto>
{
    new() { ExternalId = "C1-O1", Address = "Заявка 1", Latitude = 55.7560, Longitude = 37.6180,
        WindowStart = dateC.AddHours(9), WindowEnd = dateC.AddHours(13),
        RequiredSkill = SkillType.Installation, TaskType = "Тест: лимит смены",
        Priority = OrderPriority.High, DurationMinutes = 60 },
    new() { ExternalId = "C1-O2", Address = "Заявка 2", Latitude = 55.7562, Longitude = 37.6182,
        WindowStart = dateC.AddHours(9), WindowEnd = dateC.AddHours(13),
        RequiredSkill = SkillType.Installation, TaskType = "Тест: лимит смены",
        Priority = OrderPriority.High, DurationMinutes = 60 },
};
var c1 = await optimizer.OptimizeAsync(new CreateOptimizationPlanRequest(dateC, [c1Engineer], c1Orders, TimeLimitSeconds: 15));
bool c1Limit = c1.UnassignedOrders.Any(u => u.ReasonCode == UnassignedReasonCode.ShiftLimitExceeded);
Console.WriteLine($"    [C1-result] assigned={c1.Metrics.AssignedOrders}/2 shiftLimit={c1Limit} RULE={c1.Metrics.AssignedOrders == 1 && c1Limit}");

// C2: оборудование — жёсткое ограничение (нужно ВСЁ требуемое заявкой).
var c2Engineers = new List<EngineerInputDto>
{
    new(ExternalId: "C2-E1", Name: "С оборудованием", StartAddress: "База",
        StartLatitude: 55.7558, StartLongitude: 37.6176,
        ShiftStart: dateC.AddHours(9), ShiftEnd: dateC.AddHours(18),
        Transport: TransportType.Car,
        Skills: [SkillType.Installation],
        Equipment: ["Гигабит"]),
    new(ExternalId: "C2-E2", Name: "Без оборудования", StartAddress: "База",
        StartLatitude: 55.7558, StartLongitude: 37.6176,
        ShiftStart: dateC.AddHours(9), ShiftEnd: dateC.AddHours(18),
        Transport: TransportType.Car,
        Skills: [SkillType.Installation]),
};
var c2Orders = new List<WorkOrderInputDto>
{
    new() { ExternalId = "C2-O1", Address = "Нужен Гигабит", Latitude = 55.7560, Longitude = 37.6180,
        WindowStart = dateC.AddHours(9), WindowEnd = dateC.AddHours(13),
        RequiredSkill = SkillType.Installation, TaskType = "Тест: оборудование",
        Priority = OrderPriority.High, DurationMinutes = 60,
        RequiredEquipment = ["Гигабит"] },
    new() { ExternalId = "C2-O2", Address = "Нужен Конвертер", Latitude = 55.7562, Longitude = 37.6182,
        WindowStart = dateC.AddHours(9), WindowEnd = dateC.AddHours(13),
        RequiredSkill = SkillType.Installation, TaskType = "Тест: оборудование",
        Priority = OrderPriority.High, DurationMinutes = 60,
        RequiredEquipment = ["Конвертер"] },
};
var c2 = await optimizer.OptimizeAsync(new CreateOptimizationPlanRequest(dateC, c2Engineers, c2Orders, TimeLimitSeconds: 15));
bool c2EqAssigned = c2.Routes.SelectMany(x => x.Stops).Any(s => s.ExternalOrderId == "C2-O1");
bool c2NoEqReason = c2.UnassignedOrders.Any(u => u.ExternalOrderId == "C2-O2" && u.ReasonCode == UnassignedReasonCode.NoQualifiedEngineer);
Console.WriteLine($"    [C2-result] eqAssigned={c2EqAssigned} noEqReason={c2NoEqReason} RULE={c2EqAssigned && c2NoEqReason}");

// C3: синтетика 40/40/10/10 (ремонт/подключение/авария/дозаказ) — всё назначается, окна целы.
var c3Engineers = new List<EngineerInputDto>
{
    new(ExternalId: "C3-E1", Name: "Бригада 1", StartAddress: "База", StartLatitude: 55.7558, StartLongitude: 37.6176,
        ShiftStart: dateC.AddHours(9), ShiftEnd: dateC.AddHours(19), Transport: TransportType.Car,
        Skills: [SkillType.Installation, SkillType.LocalRepair], MaxOrdersPerShift: 6),
    new(ExternalId: "C3-E2", Name: "Бригада 2", StartAddress: "База", StartLatitude: 55.7570, StartLongitude: 37.6200,
        ShiftStart: dateC.AddHours(9), ShiftEnd: dateC.AddHours(19), Transport: TransportType.Car,
        Skills: [SkillType.Emergency, SkillType.LocalRepair], MaxOrdersPerShift: 5),
    new(ExternalId: "C3-E3", Name: "Бригада 3", StartAddress: "База", StartLatitude: 55.7600, StartLongitude: 37.6300,
        ShiftStart: dateC.AddHours(10), ShiftEnd: dateC.AddHours(20), Transport: TransportType.PublicTransit,
        Skills: [SkillType.Installation], MaxOrdersPerShift: 5),
    new(ExternalId: "C3-E4", Name: "Бригада 4", StartAddress: "База", StartLatitude: 55.7500, StartLongitude: 37.6100,
        ShiftStart: dateC.AddHours(9), ShiftEnd: dateC.AddHours(19), Transport: TransportType.Bicycle,
        Skills: [SkillType.LocalRepair], MaxOrdersPerShift: 6),
    new(ExternalId: "C3-E5", Name: "Бригада 5", StartAddress: "База", StartLatitude: 55.7620, StartLongitude: 37.6150,
        ShiftStart: dateC.AddHours(9), ShiftEnd: dateC.AddHours(19), Transport: TransportType.Pedestrian,
        Skills: [SkillType.LocalRepair, SkillType.Emergency], MaxOrdersPerShift: 4),
    new(ExternalId: "C3-E6", Name: "Бригада 6", StartAddress: "База", StartLatitude: 55.7550, StartLongitude: 37.6250,
        ShiftStart: dateC.AddHours(9), ShiftEnd: dateC.AddHours(19), Transport: TransportType.Car,
        Skills: [SkillType.Installation, SkillType.Emergency], MaxOrdersPerShift: 5),
};

var c3Orders = new List<WorkOrderInputDto>();
var rnd3 = new Random(97);
int c3Index = 0;
var mix = Enumerable.Repeat((Skill: SkillType.LocalRepair, Tr: (TransportType?)null, Dur: 50, Task: "Локальная заявка ТКД", Prio: OrderPriority.Normal), 8)
    .Concat(Enumerable.Repeat((Skill: SkillType.Installation, Tr: (TransportType?)TransportType.Car, Dur: 90, Task: "Подключение клиентов Базовая", Prio: OrderPriority.High), 8))
    .Concat(Enumerable.Repeat((Skill: SkillType.Emergency, Tr: (TransportType?)TransportType.Car, Dur: 100, Task: "Авария на ТКД", Prio: OrderPriority.Emergency), 2))
    .Concat(Enumerable.Repeat((Skill: SkillType.LocalRepair, Tr: (TransportType?)null, Dur: 40, Task: "Дозаказ оборудования", Prio: OrderPriority.Normal), 2))
    .ToList();
// Окна 6 ч, старты равномерно с 09:00 до ~12:48 — плотная, но выполнимая синтетика.
foreach (var (skill, tr, dur, task, prio) in mix)
{
    int ws = 540 + c3Index * 12 + rnd3.Next(0, 8);
    int windowLen = 360;
    c3Orders.Add(new WorkOrderInputDto
    {
        ExternalId = $"C3-O{++c3Index}", Address = $"Адрес {c3Index}",
        Latitude = 55.74 + rnd3.NextDouble() * 0.04, Longitude = 37.57 + rnd3.NextDouble() * 0.08,
        WindowStart = dateC.AddMinutes(ws), WindowEnd = dateC.AddMinutes(ws + windowLen),
        RequiredSkill = skill, RequiredTransport = tr, TaskType = task, Priority = prio,
        DurationMinutes = dur,
    });
}
var c3 = await optimizer.OptimizeAsync(new CreateOptimizationPlanRequest(dateC, c3Engineers, c3Orders, TimeLimitSeconds: 20));
Console.WriteLine($"    [C3-result] orders=20 assigned={c3.Metrics.AssignedOrders} unassigned={c3.UnassignedOrders.Count} RULE={c3.UnassignedOrders.Count == 0}");
foreach (var u in c3.UnassignedOrders)
    Console.WriteLine($"    [C3-unassigned] {u.ExternalOrderId} reason={u.ReasonCode}");

// D1: полилинии маршрутов (Valhalla /route) — у маршрутов с остановками должен появиться EncodedPolyline.
int polyRoutes = c3.Routes.Count(r => !string.IsNullOrEmpty(r.EncodedPolyline));
Console.WriteLine($"    [D1-result] routes={c3.Routes.Count} withPolyline={polyRoutes} RULE={polyRoutes > 0}");

Console.WriteLine("\n=== ФАЗА C/D ЗАВЕРШЕНЫ ===");