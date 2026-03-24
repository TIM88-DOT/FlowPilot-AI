using FlowPilot.Shared.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;

namespace FlowPilot.Infrastructure.Persistence;

/// <summary>
/// Factory used by EF Core tooling (dotnet ef migrations) to create the DbContext at design time.
/// </summary>
public class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext(string[] args)
    {
        IConfigurationRoot configuration = new ConfigurationBuilder()
            .SetBasePath(Path.Combine(Directory.GetCurrentDirectory(), "../FlowPilot.Api"))
            .AddJsonFile("appsettings.Development.json", optional: false)
            .Build();

        var optionsBuilder = new DbContextOptionsBuilder<AppDbContext>();
        optionsBuilder.UseNpgsql(configuration.GetConnectionString("DefaultConnection"));

        return new AppDbContext(optionsBuilder.Options, new DesignTimeTenant());
    }

    /// <summary>
    /// Stub tenant used only during migrations — provides empty GUIDs.
    /// </summary>
    private class DesignTimeTenant : ICurrentTenant
    {
        public Guid TenantId => Guid.Empty;
        public Guid UserId => Guid.Empty;
        public string UserRole => string.Empty;
    }
}
