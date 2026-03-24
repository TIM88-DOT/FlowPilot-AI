using FlowPilot.Domain.Common;
using FlowPilot.Domain.Enums;

namespace FlowPilot.Domain.Entities;

/// <summary>
/// A customer belonging to a tenant. Phone and Email are column-encrypted.
/// </summary>
public class Customer : BaseEntity
{
    public string Phone { get; set; } = string.Empty;
    public string? Email { get; set; }
    public string FirstName { get; set; } = string.Empty;
    public string? LastName { get; set; }
    public string PreferredLanguage { get; set; } = "fr";
    public string? Tags { get; set; }
    public decimal NoShowScore { get; set; }
    public ConsentStatus ConsentStatus { get; set; } = ConsentStatus.Pending;

    public ICollection<ConsentRecord> ConsentRecords { get; set; } = new List<ConsentRecord>();
    public ICollection<Appointment> Appointments { get; set; } = new List<Appointment>();
    public ICollection<Message> Messages { get; set; } = new List<Message>();
}
