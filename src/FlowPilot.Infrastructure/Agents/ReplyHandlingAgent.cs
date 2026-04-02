using System.Text.Json;
using FlowPilot.Application.Agents;
using FlowPilot.Application.Appointments;
using FlowPilot.Domain.Entities;
using FlowPilot.Infrastructure.Persistence;
using FlowPilot.Shared;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace FlowPilot.Infrastructure.Agents;

/// <summary>
/// Classifies inbound SMS intent and takes action:
/// - Confidence >= 0.85 for Confirm → auto-confirm the appointment
/// - Confidence < 0.75 → escalate to staff (log only for now)
/// - Other intents → log classification for staff review
/// </summary>
public sealed class ReplyHandlingAgent
{
    private readonly IAgentOrchestrator _orchestrator;
    private readonly IAppointmentService _appointmentService;
    private readonly AppDbContext _db;
    private readonly ILogger<ReplyHandlingAgent> _logger;

    private static readonly string[] ToolNames =
    [
        "get_customer_history",
        "get_appointment_details",
        "classify_intent",
        "confirm_appointment"
    ];

    private const string SystemPrompt = """
        You are an SMS intent classifier for FlowPilot, a SaaS platform for appointment-based businesses
        in Algeria. Customers reply to reminder SMS messages and you need to determine their intent.

        RULES:
        1. Call get_customer_history to understand the customer context.
        2. If the customer has upcoming appointments, call get_appointment_details to get context.
        3. Classify the customer's message into one of these intents:
           - Confirm: customer is confirming their appointment (e.g. "Oui", "OK", "Je confirme", "نعم")
           - Cancel: customer wants to cancel (e.g. "Annuler", "Non", "لا")
           - Reschedule: customer wants to change the time (e.g. "Changer l'heure", "Reporter")
           - Question: customer is asking a question (e.g. "C'est à quelle heure ?")
           - Other: anything else
        4. ALWAYS call classify_intent with your classification, confidence score, and reasoning.
        5. Confidence scoring guidelines:
           - 0.90-1.0: Very clear intent (single word affirmative/negative in expected language)
           - 0.75-0.89: Likely intent but some ambiguity
           - 0.50-0.74: Uncertain — needs staff review
           - Below 0.50: Cannot determine intent
        6. If intent is Confirm AND confidence >= 0.85, also call confirm_appointment.
        7. Consider that customers may reply in French, Arabic, or informal Algerian dialect (Darija).

        MULTIPLE APPOINTMENTS:
        - A customer may have several upcoming appointments. All of them are listed in the context.
        - If the reply is a clear confirmation ("Oui", "OK", "Je confirme", "Confirm") with no date/service
          mentioned, apply it to the SOONEST upcoming Scheduled appointment only.
        - If the reply mentions a specific date, time, or service name, match it to the correct appointment.
        - If intent is Confirm, call confirm_appointment for the matched appointment only — not all of them.

        IMPORTANT — Distinguish acknowledgment from confirmation:
        - Words like "Nice", "Cool", "Merci", "Thanks", "D'accord", "👍" after a BOOKING notification
          are acknowledgments (classify as Other), NOT confirmations.
        - A confirmation is an explicit intent to confirm attendance: "Oui", "OK", "Confirm", "Je confirme",
          "I'll be there", "نعم", "واه".
        - When in doubt, classify as Other with low confidence rather than accidentally confirming.
        """;

    public ReplyHandlingAgent(
        IAgentOrchestrator orchestrator,
        IAppointmentService appointmentService,
        AppDbContext db,
        ILogger<ReplyHandlingAgent> logger)
    {
        _orchestrator = orchestrator;
        _appointmentService = appointmentService;
        _db = db;
        _logger = logger;
    }

    /// <summary>
    /// Processes an inbound SMS that is not a STOP keyword.
    /// Returns the classification result for the caller to act on.
    /// </summary>
    public async Task<IntentClassification?> ClassifyAndActAsync(
        Guid customerId, string messageBody, CancellationToken cancellationToken = default)
    {
        // Find all upcoming appointments for this customer so the agent can match the reply
        List<Appointment> upcomingAppointments = await _db.Appointments
            .AsNoTracking()
            .Where(a => a.CustomerId == customerId
                && (a.Status == Domain.Enums.AppointmentStatus.Scheduled
                    || a.Status == Domain.Enums.AppointmentStatus.Confirmed)
                && a.StartsAt > DateTime.UtcNow)
            .OrderBy(a => a.StartsAt)
            .Take(10)
            .ToListAsync(cancellationToken);

        string userMessage = $"Customer {customerId} sent this SMS: \"{messageBody}\"";
        if (upcomingAppointments.Count > 0)
        {
            userMessage += "\nUpcoming appointments:";
            foreach (Appointment apt in upcomingAppointments)
            {
                userMessage += $"\n- {apt.Id} | {apt.StartsAt:yyyy-MM-dd HH:mm} | {apt.ServiceName} | Status: {apt.Status}";
            }
        }

        AgentRunResult result = await _orchestrator.RunAsync(new AgentRequest(
            AgentType: "ReplyHandling",
            SystemPrompt: SystemPrompt,
            UserMessage: userMessage,
            ToolNames: ToolNames,
            CustomerId: customerId,
            TriggerEvent: "InboundSms"
        ), cancellationToken);

        if (!result.Success)
        {
            _logger.LogError("ReplyHandlingAgent failed for customer {CustomerId}: {Error}",
                customerId, result.ErrorMessage);
            return null;
        }

        // Extract the classification from the tool call logs
        ToolCallLog? classifyCall = await _db.ToolCallLogs
            .AsNoTracking()
            .Where(t => t.AgentRunId == result.AgentRunId && t.ToolName == "classify_intent")
            .OrderByDescending(t => t.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);

        if (classifyCall?.OutputJson is null)
            return null;

        using JsonDocument doc = JsonDocument.Parse(classifyCall.OutputJson);
        JsonElement root = doc.RootElement;

        return new IntentClassification(
            root.GetProperty("intent").GetString()!,
            root.GetProperty("confidence").GetDouble(),
            root.GetProperty("reasoning").GetString()!
        );
    }
}

/// <summary>
/// Result of an SMS intent classification.
/// </summary>
public sealed record IntentClassification(string Intent, double Confidence, string Reasoning);
