using System.Text.Json;
using FlowPilot.Application.Agents;
using FlowPilot.Domain.Entities;
using FlowPilot.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FlowPilot.Infrastructure.Agents.Tools;

/// <summary>
/// Fetches full appointment details including customer info and staff assignment.
/// </summary>
public sealed class GetAppointmentDetailsTool : IAgentTool
{
    private readonly AppDbContext _db;

    public GetAppointmentDetailsTool(AppDbContext db) => _db = db;

    public string Name => "get_appointment_details";

    public string Description =>
        "Fetches full details of an appointment including status, date/time, service name, " +
        "customer name, and staff assignment.";

    public BinaryData InputSchema => BinaryData.FromString("""
        {
            "type": "object",
            "properties": {
                "appointmentId": { "type": "string", "format": "uuid", "description": "The appointment ID" }
            },
            "required": ["appointmentId"]
        }
        """);

    public async Task<string> ExecuteAsync(string inputJson, CancellationToken cancellationToken = default)
    {
        using JsonDocument doc = JsonDocument.Parse(inputJson);
        Guid appointmentId = Guid.Parse(doc.RootElement.GetProperty("appointmentId").GetString()!);

        var appointment = await _db.Appointments
            .AsNoTracking()
            .Include(a => a.Customer)
            .Include(a => a.StaffUser)
            .Where(a => a.Id == appointmentId)
            .Select(a => new
            {
                a.Id,
                a.Status,
                a.StartsAt,
                a.EndsAt,
                a.ServiceName,
                a.Notes,
                Customer = new
                {
                    a.Customer.Id,
                    a.Customer.FirstName,
                    a.Customer.LastName,
                    a.Customer.Phone,
                    a.Customer.PreferredLanguage
                },
                StaffName = a.StaffUser != null
                    ? a.StaffUser.FirstName + " " + a.StaffUser.LastName
                    : null
            })
            .FirstOrDefaultAsync(cancellationToken);

        if (appointment is null)
            return JsonSerializer.Serialize(new { error = "Appointment not found." });

        return JsonSerializer.Serialize(appointment);
    }
}
