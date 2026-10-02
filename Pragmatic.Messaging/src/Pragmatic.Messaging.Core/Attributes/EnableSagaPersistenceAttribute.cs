namespace Pragmatic.Messaging.Attributes;

/// <summary>
///     Marks a <c>[Boundary]</c> whose generated DbContext must host the saga persistence tables
///     (<c>__SagaInstances</c>/<c>__SagaSteps</c>). The source generator maps them in the boundary
///     DbContext's <c>OnModelCreating</c> and emits their schema, so <c>EfCoreSagaRepository</c> has
///     tables to read/write.
/// </summary>
/// <remarks>
///     Requires the boundary project to reference <c>Pragmatic.Messaging.EFCore</c>; without it the
///     attribute is a no-op and the generator reports PRAG0832. No host-side call is needed — the
///     generator wires <c>EfCoreSagaRepository</c> against this boundary's DbContext at compile time.
/// </remarks>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class EnableSagaPersistenceAttribute : Attribute;
