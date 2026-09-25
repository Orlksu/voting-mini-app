using Backend.Contracts.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Backend.Api.Infrastructure.Configurations;

public class WorkOrderConfiguration : IEntityTypeConfiguration<WorkOrder>
{
    public void Configure(EntityTypeBuilder<WorkOrder> builder)
    {
        builder.HasKey(x => x.Id);

        builder.Property(x => x.District)
            .HasMaxLength(100)
            .IsRequired();
        builder.Property(x => x.Priority).HasConversion<string>();
        builder.Property(x => x.Status).HasConversion<string>();
        builder.HasIndex(x => new { x.ExternalId, x.District }).IsUnique();
        builder.HasIndex(x => x.Status);
        builder.HasIndex(x => new { x.Status, x.District });
        builder.HasIndex(x => new { x.Status, x.DepotId });
        builder.ComplexProperty(x => x.Location, loc =>
        {
            loc.Property(l => l.RawAddress).HasMaxLength(500);
            loc.ComplexProperty(l => l.Coordinates, geo =>
            {
                geo.Property(g => g.Latitude);
                geo.Property(g => g.Longitude);
            });
        });

        builder.ComplexProperty(x => x.PreferredWindow, w =>
        {
            w.Property(p => p.Start);
            w.Property(p => p.End);
        });

        builder.Property(x => x.ServiceDuration).HasColumnType("interval");
        builder.Property(x => x.TaskType).HasMaxLength(100);
        
        builder.Property(x => x.RequiredSkill)
            .HasConversion<string>()
            .HasMaxLength(50);
        builder
            .HasOne(e => e.Depot)
            .WithMany()
            .HasForeignKey(e => e.DepotId)
            .OnDelete(DeleteBehavior.Restrict);
            ;
    }
}