namespace Pragmatic.Notifications.Attributes;

/// <summary>
///     Marks the boundary whose database holds the notification records (<c>__Notifications</c>).
/// </summary>
/// <remarks>
///     <para>
///         The table is mapped into that boundary's generated <c>DbContext</c>, so it is created and
///         migrated with the application's own data and no host-side call is needed. The same shape as
///         <c>[EnableOutbox]</c>, <c>[EnableEventOutbox]</c> and <c>[EnableBatchProgress]</c>: a
///         boundary declares that it hosts a framework table, and the generator maps it.
///     </para>
///     <para>
///         ⚠️ <b>This exists because the other shape does not work.</b> <c>UseEfCoreStore()</c>
///         registers a context of the package's own, and a Pragmatic host creates one context per
///         declared database — the migration context — and nothing else, so <c>__Notifications</c> was
///         never created and the first send failed at run time. A second context against the same
///         database cannot fix it either: <c>EnsureCreated</c> is all-or-nothing per database, so it
///         finds the database already there and creates none of its tables.
///     </para>
///     <para>
///         Requires the boundary to reference <c>Pragmatic.Notifications.EFCore</c>; without it the
///         table is not mapped, and the generator reports <b>PRAG2100</b> on the boundary.
///     </para>
/// </remarks>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class StoresNotificationsAttribute : Attribute;
