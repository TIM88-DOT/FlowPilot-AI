using FlowPilot.Infrastructure.Messaging;

namespace FlowPilot.UnitTests;

/// <summary>
/// Tests the deterministic reminder time-phrase logic that replaced the LLM's (unreliable)
/// hand-written durations. This is the source of truth for "time remaining" in reminder SMS.
/// </summary>
public sealed class ReminderTimePhraseTests
{
    [Theory]
    [InlineData(120, "fr", "2h")]
    [InlineData(120, "en", "2h")]
    [InlineData(60, "fr", "1h")]
    [InlineData(90, "fr", "1h30")]
    [InlineData(45, "fr", "45 min")]
    [InlineData(30, "en", "30 min")]
    [InlineData(5, "fr", "5 min")]
    public void Format_RendersCompactDuration(int minutes, string locale, string expected)
    {
        Assert.Equal(expected, ReminderTimePhrase.Format(TimeSpan.FromMinutes(minutes), locale));
    }

    [Theory]
    [InlineData(0, "fr", "bientôt")]
    [InlineData(-15, "fr", "bientôt")]
    [InlineData(0, "en", "shortly")]
    public void Format_NonPositiveRemaining_FallsBackGracefully(int minutes, string locale, string expected)
    {
        Assert.Equal(expected, ReminderTimePhrase.Format(TimeSpan.FromMinutes(minutes), locale));
    }

    [Fact]
    public void Resolve_ReplacesTokenWithRemainingTimeAtSend()
    {
        DateTime now = new(2026, 6, 24, 12, 0, 0, DateTimeKind.Utc);
        DateTime startsAt = now.AddMinutes(60); // 1h away when sent

        string body = "Votre RDV est dans {time_until} à 14h. Répondez OUI.";
        string resolved = ReminderTimePhrase.Resolve(body, startsAt, now, "fr");

        Assert.Equal("Votre RDV est dans 1h à 14h. Répondez OUI.", resolved);
    }

    [Fact]
    public void Resolve_NoToken_ReturnsBodyUnchanged()
    {
        DateTime now = new(2026, 6, 24, 12, 0, 0, DateTimeKind.Utc);
        const string body = "Rappel: RDV demain à 14h.";
        Assert.Equal(body, ReminderTimePhrase.Resolve(body, now.AddHours(24), now, "fr"));
    }

    [Theory]
    [InlineData("Votre RDV est dans 3h à 14h.", true)]
    [InlineData("Your appointment is in 30 min.", true)]
    [InlineData("Votre RDV est dans 2 heures.", true)]
    [InlineData("Votre RDV est dans {time_until} à 14h.", false)] // token, not a hardcoded number
    [InlineData("Rappel: RDV demain à 14h.", false)]              // no relative duration at all
    public void HasHardcodedDuration_DetectsLiteralDurations(string body, bool expected)
    {
        Assert.Equal(expected, ReminderTimePhrase.HasHardcodedDuration(body));
    }
}
