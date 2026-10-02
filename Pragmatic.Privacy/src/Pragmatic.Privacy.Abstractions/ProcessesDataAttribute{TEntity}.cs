namespace Pragmatic.Privacy;

/// <summary>
///     Declares the personal data an operation reaches through another operation, for the Article 30
///     processing register.
/// </summary>
/// <remarks>
///     <para>
///         The register derives an operation's reach from the <b>types of its dependencies</b>: a query
///         and a mutation name their entity, and an action holding an
///         <c>IRepository&lt;TEntity&gt;</c> or an <c>IMutationInvoker&lt;TMutation, TEntity&gt;</c>
///         names it in the type argument. ⚠️ <b>A boundary interface names none.</b> An action that
///         composes through <c>I{Boundary}Actions</c> reaches whatever those operations reach, and the
///         type says nothing about which — so without this attribute the operation disappears from the
///         register while continuing to process the data.
///     </para>
///     <para>
///         Declared rather than inferred, and both inference routes were weighed: reading the boundary
///         interface's operations over-reports — it says the action <em>can</em> reach every entity of
///         the boundary, not that it does — and walking the call sites is exact only for the calls it
///         can see, so an action reaching data another way goes on disappearing while the register
///         looks complete. What is determinable at compile time is stated.
///     </para>
///     <para>
///         An action that composes and declares neither form is reported (<c>PRAG2911</c>): the
///         omission becomes a build message instead of an absent row.
///     </para>
/// </remarks>
/// <example>
///     <code>
/// [DomainAction]
/// [ProcessesData&lt;Member&gt;]
/// public partial class ImportMembersAction : DomainAction&lt;ImportResult&gt;
/// {
///     private ITenancyInternalActions _tenancy = null!;   // names no entity
/// }
///     </code>
/// </example>
/// <typeparam name="TEntity">An entity this operation reaches. Repeat the attribute for each.</typeparam>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = false)]
public sealed class ProcessesDataAttribute<TEntity> : Attribute;
