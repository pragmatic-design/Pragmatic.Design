namespace Pragmatic.Jobs.Attributes;

/// <summary>
///     Marks the single <c>[Boundary]</c> whose generated DbContext hosts the durable job store's tables
///     — <c>__Jobs</c> and <c>__RecurringJobs</c>. The source generator maps both in that boundary
///     DbContext's <c>OnModelCreating</c> and emits their schema, so the migration creates them.
/// </summary>
/// <remarks>
///     <para>
///         Without it nothing creates the tables: the context's <c>OnModelCreatingPartial</c> hook maps
///         into the model EF holds in memory, not into the schema the migration builds, and
///         <c>jobs.UseEfCore()</c> — which fails deliberately rather than falling back to memory — would
///         have nothing to read. Every place in this
///         repository that persisted a job hand-wrote a second <c>DbContext</c> over the same database
///         for that reason, and no generated host persisted one at all.
///     </para>
///     <para>
///         The store is a <b>single</b> store for the application, like batch progress and unlike the
///         per-boundary saga and outbox: exactly one boundary carries this, and the tables live in that
///         boundary's database. The host still chooses the store —
///         <c>app.UseJobs(jobs =&gt; { jobs.UseEfCore(); jobs.UseEfCorePersistence(); })</c> — because
///         which store to run is a deployment's decision; what this attribute settles is that the tables
///         exist.
///     </para>
///     <para>
///         Requires the host to reference <c>Pragmatic.Jobs.EFCore</c>, whose two
///         <c>IEntityTypeConfiguration</c> the generated context names. Without it the attribute maps
///         nothing and the generator reports <c>PRAG2508</c>.
///     </para>
/// </remarks>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class EnableJobPersistenceAttribute : Attribute;
