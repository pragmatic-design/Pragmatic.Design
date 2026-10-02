namespace Pragmatic.Privacy;

/// <summary>
///     States that an operation composing another reaches no personal data — reviewed, and none.
/// </summary>
/// <remarks>
///     <para>
///         The counterpart of <see cref="ProcessesDataAttribute{TEntity}" />, and it exists because
///         <c>PRAG2911</c> needs an answer that is not a lie. An operation that composes through a
///         boundary interface cannot have its reach inferred, so the check asks the author to say; an
///         author whose answer is "none" would otherwise have to name an entity the operation does not
///         touch, putting a row in the Article 30 register to quieten a build warning.
///     </para>
///     <para>
///         ⚠️ It is a statement, not a suppression. It says the composition was looked at and reaches
///         no classified data — and it stops being true the day the operation it composes starts
///         touching some, which is exactly when somebody should be reading this line again.
///     </para>
/// </remarks>
/// <example>
///     <code>
/// [DomainAction]
/// [ProcessesData]                                     // reviewed: reaches none
/// public partial class RebuildSearchIndexAction : VoidDomainAction
/// {
///     private ICatalogInternalActions _catalog = null!;
/// }
///     </code>
/// </example>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class ProcessesDataAttribute : Attribute;
