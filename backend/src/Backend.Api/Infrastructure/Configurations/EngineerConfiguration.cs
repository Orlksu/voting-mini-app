using Backend.Contracts.Domain.Entities;
using Backend.Contracts.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using System.Text.Json;

namespace Backend.Api.Infrastructure.Configurations;

public class EngineerConfiguration : IEntityTypeConfiguration<Engineer>
{
    public void Configure(EntityTypeBuilder<Engineer> builder)
    {
        builder.HasKey(x => x.Id);

        builder.Property(x => x.ExternalId).HasMaxLength(100);
        builder.HasIndex(x => x.ExternalId).IsUnique();

        builder.Property(x => x.Name).HasMaxLength(200);

        builder.Property(x => x.District).HasMaxLength(100);
        builder.HasIndex(x => x.District);

        builder.ComplexProperty(x => x.StartLocation, loc =>
        {
            loc.Property(l => l.RawAddress).HasMaxLength(500);
            loc.ComplexProperty(l => l.Coordinates, geo =>
            {
                geo.Property(g => g.Latitude);
                geo.Property(g => g.Longitude);
            });
        });

        builder.ComplexProperty(x => x.Shift, shift =>
        {
            shift.Property(s => s.Start);
            shift.Property(s => s.End);
        });

        builder.Property(x => x.Skills)
            .HasConversion(
                skills => JsonSerializer.Serialize(skills, JsonOptions),
                json => JsonSerializer.Deserialize<HashSet<SkillType>>(json, JsonOptions) ?? new HashSet<SkillType>())
            .HasColumnType("jsonb")
            .Metadata.SetValueComparer(SkillsValueComparer);

        builder
            .HasOne(e => e.Depot)
            .WithMany()
            .HasForeignKey(e => e.DepotId)
            .OnDelete(DeleteBehavior.Restrict);
        ;
    }
    
    private static readonly ValueComparer<HashSet<SkillType>> SkillsValueComparer = new(
        (left, right) => left != null && right != null && left.SetEquals(right),
        skills => skills.Aggregate(0, (hash, skill) => HashCode.Combine(hash, skill.GetHashCode())),
        skills => new HashSet<SkillType>(skills));

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.General);
}