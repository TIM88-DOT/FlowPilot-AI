using FlowPilot.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FlowPilot.Infrastructure.Persistence.Configurations;

public class CustomerConfiguration : IEntityTypeConfiguration<Customer>
{
    public void Configure(EntityTypeBuilder<Customer> builder)
    {
        builder.ToTable("customers");

        builder.Property(c => c.Phone).HasMaxLength(20).IsRequired();
        builder.Property(c => c.Email).HasMaxLength(200);
        builder.Property(c => c.FirstName).HasMaxLength(100).IsRequired();
        builder.Property(c => c.LastName).HasMaxLength(100);
        builder.Property(c => c.PreferredLanguage).HasMaxLength(10).HasDefaultValue("fr");
        builder.Property(c => c.Tags).HasMaxLength(1000);
        builder.Property(c => c.NoShowScore).HasPrecision(5, 2).HasDefaultValue(0m);
        builder.Property(c => c.ConsentStatus).HasConversion<string>().HasMaxLength(20);

        builder.HasIndex(c => new { c.Phone, c.TenantId }).IsUnique();
    }
}
