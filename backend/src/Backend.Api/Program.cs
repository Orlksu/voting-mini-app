using Backend.RoutingEngine;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddControllers();
builder.Services.AddOpenApi();

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
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseCors();

app.UseAuthorization();

app.MapControllers();

app.Run();
