using Backend.Api.Services;
using Backend.RoutingEngine;
using Microsoft.AspNetCore.HttpOverrides;
using Scalar.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddControllers();
builder.Services.AddOpenApi();
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders =
        ForwardedHeaders.XForwardedFor |
        ForwardedHeaders.XForwardedProto;
    options.KnownNetworks.Clear();
    options.KnownProxies.Clear();
});

// Хранилище задач оптимизации (in-memory)
builder.Services.AddSingleton<OptimizationJobStore>();

// Подключаем движок маршрутизации с адресом Valhalla
var valhallaUrl = builder.Configuration["Valhalla:BaseUrl"] ?? "http://valhalla:8002";
builder.Services.AddRoutingEngine(valhallaUrl);

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
