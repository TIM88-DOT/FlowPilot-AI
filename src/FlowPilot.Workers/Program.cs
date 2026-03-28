using FlowPilot.Application.Messaging;
using FlowPilot.Infrastructure.Messaging;
using FlowPilot.Infrastructure.Persistence;
using FlowPilot.Shared;
using FlowPilot.Shared.Interfaces;
using FlowPilot.Workers;
using Microsoft.EntityFrameworkCore;
using Serilog;

Log.Logger = new LoggerConfiguration()
    .WriteTo.Console()
    .CreateBootstrapLogger();

try
{
    var builder = Host.CreateApplicationBuilder(args);

    builder.Services.AddSerilog((services, configuration) => configuration
        .ReadFrom.Configuration(builder.Configuration)
        .ReadFrom.Services(services)
        .Enrich.FromLogContext()
        .WriteTo.Console()
        .WriteTo.Seq(builder.Configuration["Seq:ServerUrl"] ?? "http://localhost:5341"));

    // Database
    builder.Services.AddDbContext<AppDbContext>(options =>
        options.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection")));

    // Workers operate without an HTTP context — use a no-op tenant for cross-tenant queries
    builder.Services.AddScoped<ICurrentTenant, WorkerTenant>();

    // SMS provider — same config-driven swap as API
    string smsProvider = builder.Configuration["SmsProvider"] ?? "Fake";
    if (smsProvider.Equals("Twilio", StringComparison.OrdinalIgnoreCase))
        builder.Services.AddScoped<ISmsProvider, TwilioSmsProvider>();
    else
        builder.Services.AddScoped<ISmsProvider, FakeSmsProvider>();

    // Hosted services
    builder.Services.AddHostedService<ScheduledMessageDispatcher>();

    var host = builder.Build();
    host.Run();
}
catch (Exception ex)
{
    Log.Fatal(ex, "Worker host terminated unexpectedly");
}
finally
{
    Log.CloseAndFlush();
}
