using Backend.Contracts.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Backend.Api.Infrastructure.Configurations;

public class GeocodeCacheItemConfiguration : IEntityTypeConfiguration<GeocodeCacheItem>
{
    public void Configure(EntityTypeBuilder<GeocodeCacheItem> builder)
    {
        builder.HasKey(x => x.Id);

        builder.Property(x => x.RawQuery).HasMaxLength(500);
        builder.HasIndex(x => x.RawQuery).IsUnique();
    }
}