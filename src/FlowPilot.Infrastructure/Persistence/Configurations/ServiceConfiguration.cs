using FlowPilot.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FlowPilot.Infrastructure.Persistence.Configurations;

public class ServiceConfiguration : IEntityTypeConfiguration<Service>
{
    public void Configure(EntityTypeBuilder<Service> builder)
    {
        builder.ToTable("services");

        builder.Property(s => s.Name).HasMaxLength(200).IsRequired();
        builder.Property(s => s.DurationMinutes).HasDefaultValue(30);
        builder.Property(s => s.Price).HasPrecision(10, 2);
        builder.Property(s => s.Currency).HasMaxLength(10);
        builder.Property(s => s.IsActive).HasDefaultValue(true);
        builder.Property(s => s.SortOrder).HasDefaultValue(0);

        builder.HasIndex(s => new { s.Name, s.TenantId }).IsUnique();
    }
}
