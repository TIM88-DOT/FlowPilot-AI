using FlowPilot.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FlowPilot.Infrastructure.Persistence.Configurations;

public class ConsentRecordConfiguration : IEntityTypeConfiguration<ConsentRecord>
{
    public void Configure(EntityTypeBuilder<ConsentRecord> builder)
    {
        builder.ToTable("consent_records");

        builder.Property(c => c.Status).HasConversion<string>().HasMaxLength(20);
        builder.Property(c => c.Source).HasConversion<string>().HasMaxLength(20);
        builder.Property(c => c.Notes).HasMaxLength(500);

        builder.HasOne(c => c.Customer)
            .WithMany(cust => cust.ConsentRecords)
            .HasForeignKey(c => c.CustomerId);
    }
}
