using FlowPilot.Infrastructure.Persistence;
using FlowPilot.Shared.Interfaces;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using NSubstitute;

namespace FlowPilot.UnitTests;

/// <summary>
/// Provides an in-memory SQLite-backed AppDbContext for unit tests.
/// Each test gets a fresh database via a unique connection.
/// </summary>
public sealed class TestDbFixture : IDisposable
{
    private readonly SqliteConnection _connection;

    public Guid TenantId { get; } = Guid.NewGuid();
    public Guid UserId { get; } = Guid.NewGuid();
    public ICurrentTenant CurrentTenant { get; }

    public TestDbFixture()
    {
        CurrentTenant = Substitute.For<ICurrentTenant>();
        CurrentTenant.TenantId.Returns(TenantId);
        CurrentTenant.UserId.Returns(UserId);
        CurrentTenant.UserRole.Returns("Owner");

        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        using AppDbContext db = CreateContext();
        db.Database.EnsureCreated();
    }

    public AppDbContext CreateContext()
    {
        DbContextOptions<AppDbContext> options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(_connection)
            .Options;

        return new AppDbContext(options, CurrentTenant);
    }

    public void Dispose()
    {
        _connection.Dispose();
    }
}
