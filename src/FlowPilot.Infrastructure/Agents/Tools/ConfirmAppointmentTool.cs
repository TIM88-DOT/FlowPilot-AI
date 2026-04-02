using System.Text.Json;
using FlowPilot.Application.Agents;
using FlowPilot.Application.Appointments;
using FlowPilot.Shared;

namespace FlowPilot.Infrastructure.Agents.Tools;

/// <summary>
/// Confirms an appointment by transitioning its status to Confirmed.
/// Domain-enforced status transition rules still apply — the AI cannot bypass them.
/// </summary>
public sealed class ConfirmAppointmentTool : IAgentTool
{
    private readonly IAppointmentService _appointmentService;

    public ConfirmAppointmentTool(IAppointmentService appointmentService) => _appointmentService = appointmentService;

    public string Name => "confirm_appointment";

    public string Description =>
        "Confirms an appointment (transitions status from Scheduled to Confirmed). " +
        "Only use this when you are confident the customer intends to confirm.";

    public BinaryData InputSchema => BinaryData.FromString("""
        {
            "type": "object",
            "properties": {
                "appointmentId": { "type": "string", "format": "uuid", "description": "The appointment to confirm" }
            },
            "required": ["appointmentId"]
        }
        """);

    public async Task<string> ExecuteAsync(string inputJson, CancellationToken cancellationToken = default)
    {
        using JsonDocument doc = JsonDocument.Parse(inputJson);
        Guid appointmentId = Guid.Parse(doc.RootElement.GetProperty("appointmentId").GetString()!);

        Result<AppointmentDto> result = await _appointmentService.ConfirmAsync(appointmentId, cancellationToken);

        if (result.IsFailure)
            return JsonSerializer.Serialize(new
            {
                success = false,
                error = result.Error.Description,
                hint = "This appointment may already be Confirmed. Check the appointment list in context and pick the correct Scheduled appointment."
            });

        return JsonSerializer.Serialize(new
        {
            success = true,
            appointmentId = result.Value.Id,
            newStatus = result.Value.Status.ToString()
        });
    }
}
