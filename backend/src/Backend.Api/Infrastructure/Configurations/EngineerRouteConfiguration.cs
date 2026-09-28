using Backend.Contracts.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Backend.Api.Infrastructure.Configurations;

public class EngineerRouteConfiguration : IEntityTypeConfiguration<EngineerRoute>
{
    public void Configure(EntityTypeBuilder<EngineerRoute> builder)
    {
        builder.HasKey(x => x.Id);

        builder.Ignore(x => x.TotalDistanceKm);
        builder.Ignore(x => x.TotalTravelTime);

        builder.HasOne<OperationResult>()
            .WithMany(x => x.Routes)
            .HasForeignKey(x => x.OperationResultId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Property(x => x.EngineerExternalId).HasMaxLength(100);
        builder.Property(x => x.EngineerName).HasMaxLength(200);
        builder.Property(x => x.BaselineDistanceKm);
        builder.Property(x => x.CreatedAt).HasDefaultValueSql("NOW()").IsRequired();
        builder.Property(x => x.EncodedPolyline).HasColumnType("text");

        builder.HasIndex(x => x.CreatedAt);
        builder.HasIndex(x => x.EngineerId);
        builder.HasIndex(x => x.OperationResultId);

        builder.HasMany(x => x.Stops)
            .WithOne()
            .HasForeignKey(x => x.EngineerRouteId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}