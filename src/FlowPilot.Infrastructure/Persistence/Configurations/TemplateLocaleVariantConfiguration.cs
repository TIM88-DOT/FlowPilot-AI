using FlowPilot.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FlowPilot.Infrastructure.Persistence.Configurations;

public class TemplateLocaleVariantConfiguration : IEntityTypeConfiguration<TemplateLocaleVariant>
{
    public void Configure(EntityTypeBuilder<TemplateLocaleVariant> builder)
    {
        builder.ToTable("template_locale_variants");

        builder.Property(v => v.Locale).HasMaxLength(10).IsRequired();
        builder.Property(v => v.Body).IsRequired();

        // One variant per locale per template
        builder.HasIndex(v => new { v.TemplateId, v.Locale }).IsUnique();

        builder.HasOne(v => v.Template)
            .WithMany(t => t.LocaleVariants)
            .HasForeignKey(v => v.TemplateId);
    }
}
