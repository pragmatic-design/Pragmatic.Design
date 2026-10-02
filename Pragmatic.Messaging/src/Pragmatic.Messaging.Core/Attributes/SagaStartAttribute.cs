namespace Pragmatic.Messaging.Attributes;

/// <summary>
///     Marks a saga method as the entry point (initial state transition).
///     Exactly one method per saga must have this attribute.
/// </summary>
[AttributeUsage(AttributeTargets.Method, Inherited = false)]
public sealed class SagaStartAttribute : Attribute;
