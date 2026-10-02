namespace Pragmatic.Privacy;

/// <summary>
///     Declares how to get from this entity to the data subject it belongs to.
/// </summary>
/// <remarks>
///     <para>
///         The generator already knows the foreign keys; what it cannot know is which of several paths
///         is the one that means "belongs to this person". An order has a customer and a
///         sales representative, and both are people — only one of them makes the order *their* data.
///     </para>
///     <para>
///         <b>This is the most fragile declaration in the whole design.</b> A wrong path produces an
///         erasure that quietly misses rows, and no diagnostic can catch it: the compiler can tell that
///         a path is missing, not that it points at the wrong person. It is why the erasure setpoint is
///         an end-to-end test that inspects the database afterwards, rather than a check on the call.
///     </para>
/// </remarks>
/// <param name="pathProperty">
///     The navigation or foreign-key property leading towards the subject — directly, or to another
///     entity that itself declares a path.
/// </param>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class LinksToSubjectAttribute(string pathProperty) : Attribute
{
    /// <summary>The property leading towards the subject.</summary>
    public string PathProperty { get; } = pathProperty;
}
