namespace Pragmatic.Messaging.Attributes;

/// <summary>
///     Marks the single <c>[Boundary]</c> whose generated DbContext hosts the batch-progress table
///     (<c>__BatchProgress</c>). The source generator maps it in that boundary DbContext's
///     <c>OnModelCreating</c>, emits its schema, and registers <c>EfCoreBatchProgressStore</c> against
///     that DbContext — so EF-backed batch progress has a table to read/write.
/// </summary>
/// <remarks>
///     <para>
///         Batch progress is a <b>single</b> store (unlike the per-boundary saga/outbox), so exactly one
///         boundary may carry this attribute; marking more than one is a compile error (PRAG0834).
///     </para>
///     <para>
///         Requires the boundary/host to reference <c>Pragmatic.Messaging.Batch</c>; without it the
///         attribute is a no-op and the generator reports PRAG0835. No host-side call is needed — the
///         generator wires <c>EfCoreBatchProgressStore</c> against this boundary's DbContext at compile time.
///     </para>
/// </remarks>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class EnableBatchProgressAttribute : Attribute;
