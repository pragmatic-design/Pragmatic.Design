namespace Pragmatic.Messaging.Attributes;

/// <summary>
///     Specifies the compensation action to dispatch if this saga step fails.
///     The SG generates the compensation chain in the orchestrator.
/// </summary>
/// <typeparam name="TAction">The DomainAction type to dispatch for compensation.</typeparam>
[AttributeUsage(AttributeTargets.Method, Inherited = false)]
public sealed class CompensateWithAttribute<TAction> : Attribute;
