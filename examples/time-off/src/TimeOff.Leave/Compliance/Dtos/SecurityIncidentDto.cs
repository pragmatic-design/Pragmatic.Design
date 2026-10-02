using Pragmatic.Incidents;

namespace TimeOff.Leave.Dtos;

/// <summary>
///     An incident the trail suggests, and the reporting clock running against it.
/// </summary>
/// <remarks>
///     <para>
///         ⚠️ <b>Every one of these is at <see cref="IncidentStage.Detected" /> and nothing else.</b>
///         The framework notices a pattern and starts the clocks; whether the incident is notifiable is
///         a judgement about impact, and neither the framework nor this application makes it. What
///         <see cref="NextObligation" /> and <see cref="HoursRemaining" /> answer is "how long is left
///         if it turns out to be", which is the part that is arithmetic against a deadline nobody wrote
///         down.
///     </para>
///     <para>
///         The clock runs from <see cref="DetectedAt" /> — not from when the attempts happened, which
///         is usually unknowable, and not from when somebody got round to looking.
///     </para>
/// </remarks>
public sealed record SecurityIncidentDto(
    string IncidentId,
    string Summary,
    DateTimeOffset DetectedAt,
    string Stage,
    string? NextObligation,
    DateTimeOffset? DueAt,
    double? HoursRemaining)
{
    internal static SecurityIncidentDto From(SecurityIncident incident, DateTimeOffset now) =>
        new(
            incident.IncidentId,
            incident.Summary,
            incident.DetectedAt,
            incident.Stage.ToString(),
            incident.NextObligation?.Obligation,
            incident.NextObligation?.DueAt,
            incident.TimeRemaining(now)?.TotalHours);
}
