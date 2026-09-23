using Backend.Contracts.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Backend.Api.Infrastructure;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options)
    {
    }

    public DbSet<Depot> Depots => Set<Depot>();
    public DbSet<GeocodeCacheItem> GeocodeCache => Set<GeocodeCacheItem>();
    public DbSet<WorkOrder> WorkOrders => Set<WorkOrder>();
    public DbSet<Engineer> Engineers => Set<Engineer>();
    public DbSet<EngineerRoute> EngineerRoutes => Set<EngineerRoute>();
    public DbSet<RouteStop> RouteStops => Set<RouteStop>();
    public DbSet<UnassignedOrder> UnassignedOrders => Set<UnassignedOrder>();
    public DbSet<OperationResult> OperationResults => Set<OperationResult>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);
    }
}