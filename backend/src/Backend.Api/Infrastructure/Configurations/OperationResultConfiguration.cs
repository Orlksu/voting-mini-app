using Backend.Contracts.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Backend.Api.Infrastructure.Configurations;

public class OperationResultConfiguration : IEntityTypeConfiguration<OperationResult>
{
    public void Configure(EntityTypeBuilder<OperationResult> builder)
    {
        builder.HasKey(x => x.Id);

        builder.Property(x => x.JobId);
        builder.Property(x => x.PlanDate);

        builder.ComplexProperty(x => x.Metrics, m =>
        {
            m.Property(p => p.TotalOrders);
            m.Property(p => p.AssignedOrders);
            m.Property(p => p.UnassignedOrders);
            m.Property(p => p.OptimizedActiveEngineers);
            m.Property(p => p.BaselineActiveEngineers);
            m.Property(p => p.SavedEngineersCount);
            m.Property(p => p.OptimizedTotalDistanceKm);
            m.Property(p => p.BaselineTotalDistanceKm);
            m.Property(p => p.DistanceEconomyPercent);
        });
    }
}