namespace Pragmatic.Messaging.Attributes;

/// <summary>
///     Marks the property that correlates this message to a saga instance — the alternative to
///     implementing <c>ICorrelatedMessage</c>. The source generator reads the property at compile
///     time (works on positional record properties too) and routes the event to the saga whose
///     correlation id equals the property value (non-string values are <c>ToString()</c>-ed).
/// </summary>
/// <remarks>
///     One property per message type (extras warn, PRAG0821). A saga event with neither
///     <c>ICorrelatedMessage</c> nor <c>[CorrelationKey]</c> is a compile error (PRAG0820).
/// </remarks>
[AttributeUsage(AttributeTargets.Property)]
public sealed class CorrelationKeyAttribute : Attribute;
