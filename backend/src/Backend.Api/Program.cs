using Backend.Api.Infrastructure;
using Backend.Api.Services;
using Backend.Api.Services.CsvParserService;
using Backend.Api.Services.GeoCodingService;
using Backend.RoutingEngine;
using Microsoft.EntityFrameworkCore;
using Scalar.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddControllers();
builder.Services.AddOpenApi();

// Подключаем PostgreSQL (EF Core)
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection")));

// Хранилище задач оптимизации (in-memory)
builder.Services.AddSingleton<OptimizationJobStore>();

// Подключаем движок маршрутизации с адресом Valhalla
var valhallaUrl = builder.Configuration["Valhalla:BaseUrl"] ?? "http://valhalla:8002";
builder.Services.AddRoutingEngine(valhallaUrl);


builder.Services.AddScoped<ICsvParserService,  CsvParserService>();
builder.Services.AddScoped<IGeocoder,  GeocoderService>();
builder.Services.AddHttpClient<IGeocoder, GeocoderService>((sp, client) =>
{
    var config = sp.GetRequiredService<IConfiguration>();
    var baseUrl = config["Nominatim:BaseUrl"] ?? "https://nominatim.openstreetmap.org";
    var userAgent = config["Nominatim:UserAgent"] ?? "FieldRoutingApp/1.0";

    client.BaseAddress = new Uri(baseUrl);
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

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.MapScalarApiReference();
}

app.UseCors();

app.UseAuthorization();

app.MapControllers();

app.Run();
