using Backend.Api.Infrastructure;
using Backend.Api.Infrastructure.Repositories;
using Backend.Api.Services;
using Backend.Api.Services.CsvParserService;
using Backend.Api.Services.EngineerService;
using Backend.Api.Services.GeoCodingService;
using Backend.Api.Services.WorkOrderService;
using Backend.RoutingEngine;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.EntityFrameworkCore;
using Scalar.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddProblemDetails(options =>
{
    options.CustomizeProblemDetails = ctx =>
    {
        ctx.ProblemDetails.Extensions["traceId"] = ctx.HttpContext.TraceIdentifier;
    };
});

builder.Services.AddControllers()
    .AddJsonOptions(options =>
        options.JsonSerializerOptions.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter()));
builder.Services.AddOpenApi();
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders =
        ForwardedHeaders.XForwardedFor |
        ForwardedHeaders.XForwardedProto;
    options.KnownIPNetworks.Clear();
    options.KnownProxies.Clear();
});

// Подключаем PostgreSQL (EF Core)
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection")));

// Хранилище задач оптимизации (in-memory)
builder.Services.AddSingleton<OptimizationJobStore>();

// Подключаем движок маршрутизации с адресом Valhalla
var valhallaUrl = builder.Configuration["Valhalla:BaseUrl"] ?? "http://valhalla:8002";
builder.Services.AddRoutingEngine(valhallaUrl);

builder.Services.AddScoped<ICsvParserService, CsvParserService>();
builder.Services.AddScoped<IEngineerCsvParser, EngineerCsvParser>();
builder.Services.AddScoped<IDepotRepository, DepotRepository>();
builder.Services.AddScoped<IWorkOrderRepository, WorkOrderRepository>();
builder.Services.AddScoped<IEngineerRepository, EngineerRepository>();
builder.Services.AddScoped<IEngineerRouteRepository, EngineerRouteRepository>();
builder.Services.AddScoped<IOperationResultRepository, OperationResultRepository>();
builder.Services.AddScoped<IWorkOrderService, WorkOrderService>();
builder.Services.AddScoped<IEngineerService, EngineerService>();
builder.Services.AddScoped<IGeocodeCacheRepository, GeocodeCacheRepository>();
builder.Services.AddHttpClient<IGeocoder, GeocoderService>((sp, client) =>
{
    var config = sp.GetRequiredService<IConfiguration>();
    var baseUrl = config["Nominatim:BaseUrl"] ?? "https://nominatim.openstreetmap.org";
    var userAgent = config["Nominatim:UserAgent"] ?? "FieldRoutingApp/1.0";

    client.BaseAddress = new Uri(baseUrl);
    client.Timeout = TimeSpan.FromSeconds(10);
    if (!string.IsNullOrWhiteSpace(userAgent))
        client.DefaultRequestHeaders.UserAgent.ParseAdd(userAgent);
});

// Настройка CORS для взаимодействия с фронтендом
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        policy.AllowAnyOrigin()
              .AllowAnyHeader()
              .AllowAnyMethod();
    });
});

var app = builder.Build();

app.UseExceptionHandler();
app.UseStatusCodePages();

using (var scope = app.Services.CreateScope())
{
    var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();
    try
    {
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        dbContext.Database.Migrate();
        logger.LogInformation("База данных успешно обновлена до актуальных миграций.");
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "Ошибка применения миграций базы данных при запуске приложения.");
        throw;
    }
}

// Configure the HTTP request pipeline.
app.UseForwardedHeaders();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.MapScalarApiReference();
}

app.MapGet("/healthz", () => Results.Ok(new { status = "ok" }));

app.UseCors();

app.UseAuthorization();

app.MapControllers();

app.Run();
