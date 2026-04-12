using FlowPilot.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace FlowPilot.IntegrationTests.Fixtures;

/// <summary>
/// Custom WebApplicationFactory that replaces the main PostgreSQL database
/// with a dedicated test database that is created fresh per test class.
/// </summary>
public class FlowPilotApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private readonly string _dbName = $"flowpilot_test_{Guid.NewGuid():N}";

    private string ConnectionString =>
        $"Host=localhost;Port=5432;Database={_dbName};Username=flowpilot;Password=flowpilot_dev_pass;Include Error Detail=true";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");

        // Force FakeSmsProvider in tests — appsettings may default to "Twilio"
        builder.UseSetting("SmsProvider", "Fake");

        builder.ConfigureServices(services =>
        {
            // Remove the existing AppDbContext registration
            ServiceDescriptor? descriptor = services.SingleOrDefault(
                d => d.ServiceType == typeof(DbContextOptions<AppDbContext>));
            if (descriptor is not null)
                services.Remove(descriptor);

            // Add AppDbContext with the test database
            services.AddDbContext<AppDbContext>((sp, options) =>
            {
                options.UseNpgsql(ConnectionString);
            });
        });
    }

    public async Task InitializeAsync()
    {
        using IServiceScope scope = Services.CreateScope();
        AppDbContext db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.Database.EnsureCreatedAsync();
    }

    async Task IAsyncLifetime.DisposeAsync()
    {
        using IServiceScope scope = Services.CreateScope();
        AppDbContext db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.Database.EnsureDeletedAsync();
    }
}
