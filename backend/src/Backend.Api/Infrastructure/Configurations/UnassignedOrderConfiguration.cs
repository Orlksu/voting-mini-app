using Backend.Contracts.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Backend.Api.Infrastructure.Configurations;

public class UnassignedOrderConfiguration : IEntityTypeConfiguration<UnassignedOrder>
{
    public void Configure(EntityTypeBuilder<UnassignedOrder> builder)
    {
        builder.HasKey(x => x.Id);

        builder.HasOne<OperationResult>()
            .WithMany(x => x.UnassignedOrders)
            .HasForeignKey(x => x.OperationResultId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Property(x => x.ExternalOrderId).HasMaxLength(100);
        builder.Property(x => x.ReasonDescription).HasColumnType("text");
        
        builder.HasIndex(x => x.ExternalOrderId);
    }
}