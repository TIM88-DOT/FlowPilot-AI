using System.Text.Json;
using FlowPilot.Application.Agents;
using FlowPilot.Application.Appointments;
using FlowPilot.Shared;

namespace FlowPilot.Infrastructure.Agents.Tools;

/// <summary>
/// Cancels an appointment by transitioning its status to Cancelled.
/// Domain-enforced status transition rules still apply — the AI cannot bypass them.
/// </summary>
public sealed class CancelAppointmentTool : IAgentTool
{
    private readonly IAppointmentService _appointmentService;

    public CancelAppointmentTool(IAppointmentService appointmentService) => _appointmentService = appointmentService;

    public string Name => "cancel_appointment";

    public string Description =>
        "Cancels an appointment (transitions status to Cancelled). " +
        "Only use this when you are confident the customer intends to cancel.";

    public BinaryData InputSchema => BinaryData.FromString("""
        {
            "type": "object",
            "properties": {
                "appointmentId": { "type": "string", "format": "uuid", "description": "The appointment to cancel" }
            },
            "required": ["appointmentId"]
        }
        """);

    public async Task<string> ExecuteAsync(string inputJson, CancellationToken cancellationToken = default)
    {
        using JsonDocument doc = JsonDocument.Parse(inputJson);
        Guid appointmentId = Guid.Parse(doc.RootElement.GetProperty("appointmentId").GetString()!);

        Result<AppointmentDto> result = await _appointmentService.CancelAsync(appointmentId, cancellationToken);

        if (result.IsFailure)
            return JsonSerializer.Serialize(new
            {
                success = false,
                error = result.Error.Description
            });

        return JsonSerializer.Serialize(new
        {
            success = true,
            appointmentId = result.Value.Id,
            newStatus = result.Value.Status.ToString()
        });
    }
}
