using Backend.Contracts.Domain.Entities;
using Backend.Contracts.Domain.Enums;
using Backend.Contracts.Domain.ValueObjects;
using Microsoft.EntityFrameworkCore;

namespace Backend.Api.Infrastructure;

public static class DbTestSeeder
{
    public static async Task TestDatabaseAsync(AppDbContext db)
    {
        // 1. Проверяем Engineer + ComplexProperty + jsonb
        if (!await db.Engineers.AnyAsync())
        {
            var testEngineer = new Engineer
            {
                ExternalId = "ENG-001",
                Name = "Иван Иванов",
                Transport = TransportType.Car,
                StartLocation = new Location
                {
                    RawAddress = "г. Москва, ул Юных Ленинцев, д 83с 4",
                    Coordinates = new GeoCoordinate(55.7004508, 37.7494938)
                },
                Shift = new TimeWindow(DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddHours(9)),
                Skills = [SkillType.Installation, SkillType.Emergency]
            };

            await db.Engineers.AddAsync(testEngineer);
        }

        // 2. Проверяем WorkOrder + TimeWindow + Duration
        if (!await db.WorkOrders.AnyAsync())
        {
            var testOrder = new WorkOrder
            {
                ExternalId = "TEST-1001",
                District = "Кузьминки",
                TaskType = "Конвергенция абонента",
                RequiredSkill = SkillType.Installation,
                Priority = OrderPriority.Normal,
                Status = OrderStatus.NotDispatched,
                ServiceDuration = TimeSpan.FromMinutes(30),
                Location = new Location
                {
                    RawAddress = "Город Москва, пр-кт.Волгоградский, д. 128 к 5",
                    Coordinates = new GeoCoordinate(55.700846, 37.7822191)
                },
                PreferredWindow = new TimeWindow
                (
                  DateTimeOffset.UtcNow.AddHours(2),
                  DateTimeOffset.UtcNow.AddHours(4)
                )
            };

            await db.WorkOrders.AddAsync(testOrder);
        }

        await db.SaveChangesAsync();
        
        var savedEngineer = await db.Engineers.FirstAsync(e => e.ExternalId == "ENG-001");
        var savedOrder = await db.WorkOrders.FirstAsync(o => o.ExternalId == "TEST-1001");

        Console.WriteLine($"[DB TEST OK] Инженер загружен: {savedEngineer.Name}, навыков: {savedEngineer.Skills.Count}");
        Console.WriteLine($"[DB TEST OK] Заказ загружен: {savedOrder.District}, длительность: {savedOrder.ServiceDuration}");
    }
}