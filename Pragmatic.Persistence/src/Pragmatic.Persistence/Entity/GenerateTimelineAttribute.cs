namespace Pragmatic.Persistence.Entity;

/// <summary>
///     Opt-in: generates a <c>GetTimeline()</c> CTE query method using LAG/LEAD
///     for temporal entities with ValidFrom/ValidTo.
/// </summary>
[AttributeUsage(AttributeTargets.Class)]
public sealed class GenerateTimelineAttribute : Attribute;
