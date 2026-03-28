using System.Linq.Expressions;
using System.Reflection;
using FlowPilot.Domain.Common;
using FlowPilot.Domain.Entities;
using FlowPilot.Shared.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace FlowPilot.Infrastructure.Persistence;

/// <summary>
/// Application database context with automatic multi-tenancy and soft delete query filters.
/// Every entity inheriting <see cref="BaseEntity"/> gets a global filter:
/// WHERE tenant_id = @currentTenantId AND is_deleted = false
/// </summary>
public class AppDbContext : DbContext
{
    private readonly ICurrentTenant _currentTenant;

    public AppDbContext(DbContextOptions<AppDbContext> options, ICurrentTenant currentTenant)
        : base(options)
    {
        _currentTenant = currentTenant;
    }

    // Tenants
    public DbSet<Tenant> Tenants => Set<Tenant>();
    public DbSet<TenantSettings> TenantSettings => Set<TenantSettings>();

    // Identity
    public DbSet<User> Users => Set<User>();

    // Customers
    public DbSet<Customer> Customers => Set<Customer>();
    public DbSet<ConsentRecord> ConsentRecords => Set<ConsentRecord>();

    // Services
    public DbSet<Service> Services => Set<Service>();

    // Appointments
    public DbSet<Appointment> Appointments => Set<Appointment>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();

    // Messaging
    public DbSet<Message> Messages => Set<Message>();
    public DbSet<ScheduledMessage> ScheduledMessages => Set<ScheduledMessage>();
    public DbSet<Template> Templates => Set<Template>();
    public DbSet<TemplateLocaleVariant> TemplateLocaleVariants => Set<TemplateLocaleVariant>();

    // Billing
    public DbSet<Plan> Plans => Set<Plan>();
    public DbSet<UsageRecord> UsageRecords => Set<UsageRecord>();

    // AI / Agents
    public DbSet<AgentRun> AgentRuns => Set<AgentRun>();
    public DbSet<ToolCallLog> ToolCallLogs => Set<ToolCallLog>();

    // Infrastructure
    public DbSet<ProcessedEvent> ProcessedEvents => Set<ProcessedEvent>();

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        optionsBuilder.UseSnakeCaseNamingConvention();
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.ApplyConfigurationsFromAssembly(Assembly.GetExecutingAssembly());

        ApplyGlobalFilters(modelBuilder);
    }

    public override int SaveChanges()
    {
        SetAuditFields();
        return base.SaveChanges();
    }

    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        SetAuditFields();
        return base.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// Applies TenantId + IsDeleted global query filters to every entity that inherits BaseEntity.
    /// </summary>
    private void ApplyGlobalFilters(ModelBuilder modelBuilder)
    {
        foreach (Microsoft.EntityFrameworkCore.Metadata.IMutableEntityType entityType in modelBuilder.Model.GetEntityTypes())
        {
            if (!typeof(BaseEntity).IsAssignableFrom(entityType.ClrType))
                continue;

            // Build: entity => entity.TenantId == _currentTenant.TenantId && !entity.IsDeleted
            ParameterExpression parameter = Expression.Parameter(entityType.ClrType, "entity");

            Expression tenantFilter = Expression.Equal(
                Expression.Property(parameter, nameof(BaseEntity.TenantId)),
                Expression.Property(Expression.Constant(this), nameof(CurrentTenantId)));

            Expression softDeleteFilter = Expression.Equal(
                Expression.Property(parameter, nameof(BaseEntity.IsDeleted)),
                Expression.Constant(false));

            Expression combinedFilter = Expression.AndAlso(tenantFilter, softDeleteFilter);

            LambdaExpression lambda = Expression.Lambda(combinedFilter, parameter);
            modelBuilder.Entity(entityType.ClrType).HasQueryFilter(lambda);
        }
    }

    /// <summary>
    /// Exposed as a property so the EF filter expression tree can capture it as a reference
    /// that re-evaluates on each query (not a captured constant).
    /// </summary>
    public Guid CurrentTenantId => _currentTenant.TenantId;

    /// <summary>
    /// Automatically sets CreatedAt/UpdatedAt and TenantId on save.
    /// </summary>
    private void SetAuditFields()
    {
        DateTime utcNow = DateTime.UtcNow;

        foreach (Microsoft.EntityFrameworkCore.ChangeTracking.EntityEntry<BaseEntity> entry in ChangeTracker.Entries<BaseEntity>())
        {
            switch (entry.State)
            {
                case EntityState.Added:
                    entry.Entity.CreatedAt = utcNow;
                    entry.Entity.UpdatedAt = utcNow;
                    // Only set TenantId from JWT if not already assigned (e.g. during registration)
                    if (entry.Entity.TenantId == Guid.Empty)
                        entry.Entity.TenantId = _currentTenant.TenantId;
                    if (entry.Entity.Id == Guid.Empty)
                        entry.Entity.Id = Guid.NewGuid();
                    break;

                case EntityState.Modified:
                    entry.Entity.UpdatedAt = utcNow;
                    // Prevent TenantId from being changed after creation
                    entry.Property(e => e.TenantId).IsModified = false;
                    break;
            }
        }
    }
}
