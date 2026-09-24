using Backend.Contracts.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Backend.Api.Infrastructure.Configurations;

public class DepotConfiguration: IEntityTypeConfiguration<Depot>
{
    public void Configure(EntityTypeBuilder<Depot> builder)
    {
        builder.HasKey(x => x.Id);

        builder.Property(x => x.District).HasMaxLength(100);
        builder.HasIndex(x => x.District).IsUnique();

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