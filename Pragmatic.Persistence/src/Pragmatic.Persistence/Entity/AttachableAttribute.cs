namespace Pragmatic.Persistence.Entity;

/// <summary>
///     Declared on a <b>[PolymorphicAttachment]</b> entity: names one type whose rows may own it.
///     The generator emits, on that owner, the extensions that query and batch-load these
///     attachments — <c>Query{Attachment}s(owner, repository)</c> and
///     <c>Load{Attachment}sBatchAsync(repository, ids, ct)</c> — and, on the attachment, the typed
///     <c>ForOwner&lt;TOwner&gt;()</c>. Repeat it once per owner type.
/// </summary>
/// <typeparam name="TOwner">A type whose rows can own this attachment.</typeparam>
/// <example>
///     <code>
/// [Entity]
/// [PolymorphicAttachment]      // Document belongs to owners of more than one type
/// [Attachable&lt;Invoice&gt;]       // … and Invoice is one of them
/// [Attachable&lt;Reservation&gt;]
/// public partial class Document : IEntity { }
///     </code>
/// </example>
/// <remarks>
///     ⚠️ The roles are easy to swap: the class carrying the attribute is the <em>attachment</em>,
///     and the type argument is an <em>owner</em>. Written the other way round —
///     <c>[Attachable&lt;Document&gt;]</c> on <c>Invoice</c> — nothing at all is generated, and nothing
///     says so.
/// </remarks>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = true)]
public sealed class AttachableAttribute<TOwner> : Attribute
    where TOwner : class;
