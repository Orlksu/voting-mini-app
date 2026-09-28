using Backend.Contracts.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Backend.Api.Infrastructure.Configurations;

public class RouteStopConfiguration : IEntityTypeConfiguration<RouteStop>
{
    public void Configure(EntityTypeBuilder<RouteStop> builder)
    {
        builder.HasKey(x => x.Id);

        builder.Property(x => x.OrderId);
        builder.Property(x => x.ExternalOrderId).HasMaxLength(100);
        builder.Property(x => x.AssignmentReason).HasColumnType("text");
        builder.Property(x => x.TravelTimeFromPrevious).HasColumnType("interval");
        builder.Property(x => x.WaitingDuration).HasColumnType("interval");
        
        builder.HasIndex(x => x.OrderId);
        builder.HasIndex(x => x.ExternalOrderId);
        
        builder.HasIndex(x => new { x.EngineerRouteId, x.Sequence }).IsUnique();

        builder.ComplexProperty(x => x.Location, loc =>
        {
            loc.Property(l => l.RawAddress).HasMaxLength(500);
            loc.ComplexProperty(l => l.Coordinates, geo =>
            {
                geo.Property(g => g.Latitude);
                geo.Property(g => g.Longitude);
            });
        });
        
    }
}