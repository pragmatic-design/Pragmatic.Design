namespace Pragmatic.Messaging.Attributes;

/// <summary>
///     Marks a class as a saga orchestrator.
///     The SG generates an Orchestrator with state machine routing,
///     transition validation, and DomainAction dispatch.
/// </summary>
/// <typeparam name="TState">The enum representing saga states.</typeparam>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class SagaAttribute<TState> : Attribute where TState : struct, Enum;
