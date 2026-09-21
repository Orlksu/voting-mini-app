# 🛠 Backend Guide · Кейс 13: Маршрутизация выездных инженеров

Руководство для разработчиков бэкенда проекта.  
Стек: **C# / .NET 10**, **Google OR-Tools 9.x**, **Valhalla Routing Engine**, **PostgreSQL 17**, **Docker Compose**.

---

## 📌 1. Архитектура и разделение зон ответственности

Проект спроектирован по модульному принципу, чтобы **два разработчика могли работать одновременно в разных ветках без merge-конфликтов в Git**:

```
backend/
├── Backend.slnx                      # Solution файл (.NET 10)
├── Dockerfile                        # Multi-stage сборка для Docker
├── .dockerignore
└── src/
    ├── Backend.Contracts/            # 🤝 ОБЩИЙ ДОГОВОР (DTO, Enums, Интерфейсы)
    │   ├── Enums/                    # TransportType, OrderPriority
    │   ├── Models/                   # EngineerDto, OrderDto, LocationDto, TimeWindowDto
    │   ├── Requests/                 # OptimizeRoutesRequest
    │   ├── Responses/                # OptimizeRoutesResponse, EngineerRouteDto, RouteStopDto, RoutingMetricsDto
    │   └── Interfaces/               # IRouteOptimizer
    │
    ├── Backend.RoutingEngine/        # 🧠 ЗОНА 1: Разработчик алгоритмов (OR-Tools)
    │   ├── Solvers/                  # OrToolsVrpSolver.cs (математика и солвер)
    │   ├── Services/                 # RouteOptimizer.cs, MockRouteOptimizer.cs
    │   └── DependencyInjection.cs    # AddRoutingEngine()
    │
    └── Backend.Api/                  # 🌐 ЗОНА 2: Разработчик REST API & Базы данных
        ├── Controllers/              # RoutesController.cs
        ├── appsettings.json          # Конфигурация и ConnectionString
        └── Program.cs                # Swagger/OpenAPI, CORS, DI
```

---

## 🌿 2. Git Workflow: как работать без конфликтов

| Роль | Ветка | Разрешённая для правок папка | Что НЕ трогать |
|---|---|---|---|
| **Разработчик API & DB** | `feature/rest-api` | `src/Backend.Api/` | Не трогать `Backend.RoutingEngine/` |
| **Разработчик Алгоритмов** | `feature/routing-engine` | `src/Backend.RoutingEngine/` | Не трогать `Backend.Api/` |

### ⚠️ Правило изменения контрактов (`src/Backend.Contracts/`):
* `Backend.Contracts` — это общий договор.
* Если нужно добавить новое поле — **добавляйте с опциональным значением по умолчанию** (например, `string? Note = null`), чтобы не ломать сборку у напарника.
* Удалять или переименовывать существующие поля — **только после синхронизации со вторым разработчиком и фронтендом**.

---

## 🌐 3. Памятка для Разработчика REST API (`Backend.Api`)

Ваша задача — бизнес-логика API, работа с базой данных и предоставление данных фронтенду.

### Как вызывать оптимизацию:
Вам **не нужно** ждать, пока напарник напишет математическую модель на OR-Tools. В проекте уже работает `MockRouteOptimizer`, возвращающий валидные данные с рассчитанными маршрутами.

Просто внедряйте интерфейс `IRouteOptimizer`:
```csharp
[ApiController]
[Route("api/[controller]")]
public class RoutesController(IRouteOptimizer optimizer) : ControllerBase
{
    [HttpPost("optimize")]
    public async Task<ActionResult<OptimizeRoutesResponse>> Optimize(
        [FromBody] OptimizeRoutesRequest request,
        CancellationToken ct)
    {
        var result = await optimizer.OptimizeAsync(request, ct);
        return Ok(result);
    }
}
```

### Подключение к PostgreSQL:
Строка подключения уже настроена в `appsettings.json` и `docker-compose.yml`:
* **Внутри Docker**: `Host=db;Port=5432;Database=lctdb;Username=user;Password=password`
* **Локально из IDE**: `Host=localhost;Port=5432;Database=lctdb;Username=user;Password=password`

Если подключаете EF Core:
```bash
dotnet add src/Backend.Api/Backend.Api.csproj package Npgsql.EntityFrameworkCore.PostgreSQL
```

---

## 🧠 4. Памятка для Разработчика Алгоритмов (`Backend.RoutingEngine`)

Ваша задача — решить задачу маршрутизации выездных специалистов (**VRPTW**: Vehicle Routing Problem with Time Windows).

### Где писать код:
Основная точка входа: [`src/Backend.RoutingEngine/Solvers/OrToolsVrpSolver.cs`](file:///Users/artem/WebstormProjects/case-13-field-engineer-routing-team-97/backend/src/Backend.RoutingEngine/Solvers/OrToolsVrpSolver.cs).

### Пакет Google OR-Tools:
В проект `Backend.RoutingEngine` уже установлен пакет **`Google.OrTools 9.15`** (включая нативные бинарники под macOS ARM64 и Linux).

### План реализации VRPTW в `OrToolsVrpSolver`:
1. **Матрица дистанций и времени через Valhalla**:
   * В проекте доступен внедряемый сервис `IValhallaClient`:
     ```csharp
     // Для авто:
     double[,] autoMatrix = await valhallaClient.GetTimeMatrixAsync(locations, costing: "auto");
     // Для пеших инженеров:
     double[,] footMatrix = await valhallaClient.GetTimeMatrixAsync(locations, costing: "pedestrian");
     ```
   * Valhalla рассчитывает честную матрицу времени в секундах по реальным дорогам Москвы.
   * Если контейнер Valhalla недоступен, клиент автоматически фоллбэчится на формулу Haversine.
2. **Инициализация OR-Tools**:
   ```csharp
   int numLocations = request.Orders.Count + request.Engineers.Count;
   int numVehicles = request.Engineers.Count;
   var manager = new RoutingIndexManager(numLocations, numVehicles, starts, ends);
   var routing = new RoutingModel(manager);
   ```
3. **Регистрация транзитного колбэка**:
   ```csharp
   int transitCallbackIndex = routing.RegisterTransitCallback((long fromIndex, long toIndex) => {
       var fromNode = manager.IndexToNode(fromIndex);
       var toNode = manager.IndexToNode(toIndex);
       return timeMatrix[fromNode, toNode];
   });
   routing.SetArcCostEvaluatorOfAllVehicles(transitCallbackIndex);
   ```
4. **Временные окна (Time Windows Dimension)**:
   ```csharp
   routing.AddDimension(
       transitCallbackIndex,
       slack_max: 60,      // максимальное время ожидания у клиента
       capacity: 24 * 60,  // горизонт планирования (минуты)
       fix_start_cumul_to_zero: false,
       name: "Time"
   );
   var timeDimension = routing.GetDimensionOrDie("Time");
   // Для каждой заявки задайте: timeDimension.CumulVar(orderIndex).SetRange(windowStart, windowEnd);
   ```
5. **Совместимость навыков и оборудования**:
   * Если у инженера нет нужного скилла для заявки, запретите посещение:
     `routing.VehicleVar(orderNode).RemoveValue(engineerVehicleIndex);`
6. **Настройка параметров поиска**:
   ```csharp
   RoutingSearchParameters searchParameters = operations_research_constraint_solver.DefaultRoutingSearchParameters();
   searchParameters.FirstSolutionStrategy = FirstSolutionStrategy.Types.Value.PathCheapestArc;
   searchParameters.LocalSearchMetaheuristic = LocalSearchMetaheuristic.Types.Value.GuidedLocalSearch;
   searchParameters.TimeLimit = new Google.Protobuf.WellKnownTypes.Duration { Seconds = 5 };
   
   Assignment solution = routing.SolveWithParameters(searchParameters);
   ```

### Регистрация зависимостей:
Если вы создаёте дополнительные сервисы (калькуляторы матриц, геокодеры), регистрируйте их в [`src/Backend.RoutingEngine/DependencyInjection.cs`](file:///Users/artem/WebstormProjects/case-13-field-engineer-routing-team-97/backend/src/Backend.RoutingEngine/DependencyInjection.cs):
```csharp
public static class DependencyInjection
{
    public static IServiceCollection AddRoutingEngine(this IServiceCollection services)
    {
        services.AddScoped<IRouteOptimizer, RouteOptimizer>();
        services.AddSingleton<OrToolsVrpSolver>();
        // Ваши новые сервисы регистрируйте тут:
        // services.AddSingleton<IDistanceMatrixCalculator, HaversineDistanceMatrixCalculator>();
        return services;
    }
}
```
**Не редактируйте `Program.cs` в `Backend.Api`** — метод `AddRoutingEngine()` уже подключен.

---

## 🚀 5. Запуск и тестирование

### Вариант 1: Запуск через Docker Compose (рекомендуется)
Из корня репозитория:
```bash
# Запуск всего стека (БД + Бэкенд + Фронтенд)
docker compose up -d

# Логи бэкенда
docker compose logs -f backend

# Пересборка бэкенда после изменений
docker compose up -d --build backend
```

### Вариант 2: Локальный запуск в IDE (Rider / VS Code / Visual Studio)
1. База должна быть запущена: `docker compose up -d db`
2. Откройте [`backend/Backend.slnx`](file:///Users/artem/WebstormProjects/case-13-field-engineer-routing-team-97/backend/Backend.slnx).
3. Проект для запуска: **`Backend.Api`**.
4. Порт по умолчанию: `http://localhost:5001`.

> 💡 **Для пользователей macOS**: Если в системе установлен .NET 9 для других проектов, запускайте через CLI с указанием SDK .NET 10:
> ```bash
> DOTNET_ROOT=/opt/homebrew/opt/dotnet/libexec /opt/homebrew/opt/dotnet/bin/dotnet run --project src/Backend.Api
> ```

---

## 📡 6. Готовые эндпоинты для проверки

### 1. Получить тестовый набор инженеров и заявок (Москва)
```bash
curl http://localhost:5001/api/routes/sample-request
```

### 2. Запустить оптимизацию (POST)
```bash
curl -X POST http://localhost:5001/api/routes/optimize \
  -H "Content-Type: application/json" \
  -d "$(curl -s http://localhost:5001/api/routes/sample-request)"
```

### 3. OpenAPI / Swagger спецификация
* JSON схема: [http://localhost:5001/openapi/v1.json](http://localhost:5001/openapi/v1.json)
* Через Nginx прокси (фронтенд порт): [http://localhost:8080/api/routes/sample-request](http://localhost:8080/api/routes/sample-request)

### 4. Прямая проверка Valhalla Routing Engine (Порт 8002)
* Проверка статуса:
  ```bash
  curl http://localhost:8002/status
  ```
* Запрос матрицы времени между точками (авто):
  ```bash
  curl -X POST http://localhost:8002/sources_to_targets \
    -H "Content-Type: application/json" \
    -d '{"sources":[{"lat":55.7558,"lon":37.6173}],"targets":[{"lat":55.7602,"lon":37.6485}],"costing":"auto"}'
  ```
* Запрос матрицы времени (пешеход):
  ```bash
  curl -X POST http://localhost:8002/sources_to_targets \
    -H "Content-Type: application/json" \
    -d '{"sources":[{"lat":55.7558,"lon":37.6173}],"targets":[{"lat":55.7602,"lon":37.6485}],"costing":"pedestrian"}'
  ```
