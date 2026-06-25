using System.Text.RegularExpressions;

namespace FlowPilot.Infrastructure.Messaging;

/// <summary>
/// Deterministic rendering of the "time remaining until the appointment" phrase used in reminder SMS.
/// The LLM agent must NOT compute this itself (it is unreliable at the arithmetic and at picking a
/// sendAt); instead it writes the <see cref="TimeUntilToken"/> placeholder and the dispatcher resolves
/// it from the live appointment time at the moment the SMS is actually sent — so the text is correct
/// regardless of dispatch timing. This keeps the business rule in C#, per CLAUDE.md.
/// </summary>
public static class ReminderTimePhrase
{
    /// <summary>Placeholder the agent embeds where the remaining time should appear, e.g. "dans {time_until}".</summary>
    public const string TimeUntilToken = "{time_until}";

    // Matches an LLM-authored hardcoded duration after "dans"/"in" (fr/en) — e.g. "dans 3h", "in 30 min".
    private static readonly Regex HardcodedDuration = new(
        @"\b(dans|in)\s+\d+\s*(h|hr|hrs|hour|hours|heure|heures|min|mins|minute|minutes)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>
    /// Formats a remaining duration as a compact phrase: "45 min", "2h", "1h30".
    /// Suitable for both French ("dans 2h") and English ("in 2h") sentences.
    /// </summary>
    public static string Format(TimeSpan remaining, string? locale)
    {
        int totalMinutes = (int)Math.Round(remaining.TotalMinutes);
        if (totalMinutes <= 0)
            return string.Equals(locale, "en", StringComparison.OrdinalIgnoreCase) ? "shortly" : "bientôt";

        if (totalMinutes < 60)
            return $"{totalMinutes} min";

        int hours = totalMinutes / 60;
        int minutes = totalMinutes % 60;
        return minutes == 0 ? $"{hours}h" : $"{hours}h{minutes:D2}";
    }

    /// <summary>
    /// Replaces <see cref="TimeUntilToken"/> in <paramref name="body"/> with the remaining time
    /// computed from <paramref name="startsAtUtc"/> at <paramref name="nowUtc"/>. No-op if absent.
    /// </summary>
    public static string Resolve(string body, DateTime startsAtUtc, DateTime nowUtc, string? locale)
    {
        if (string.IsNullOrEmpty(body) || !body.Contains(TimeUntilToken, StringComparison.Ordinal))
            return body;

        return body.Replace(TimeUntilToken, Format(startsAtUtc - nowUtc, locale), StringComparison.Ordinal);
    }

    /// <summary>
    /// True when the body states a relative duration as a literal number (which the LLM must not do)
    /// instead of using <see cref="TimeUntilToken"/>.
    /// </summary>
    public static bool HasHardcodedDuration(string body) =>
        !string.IsNullOrEmpty(body)
        && !body.Contains(TimeUntilToken, StringComparison.Ordinal)
        && HardcodedDuration.IsMatch(body);
}
