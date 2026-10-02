namespace Pragmatic.Actions.Attributes;

/// <summary>
///     Marks a class as a DomainAction, enabling source generation for:
///     - SetDependencies() method for dependency injection
///     - Boundary interface inclusion (unless Internal or System)
///     - Pipeline execution
/// </summary>
/// <remarks>
///     The class must:
///     - Be partial
///     - Inherit from DomainAction&lt;T&gt; or one of its variants
///     - Override the Execute method
/// </remarks>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class DomainActionAttribute : Attribute
{
    /// <summary>
    ///     Whether the operation is kept off the boundary's <b>public</b> interface. Leave it unset and
    ///     the presence of an <c>[Endpoint]</c> decides.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Three states, because there are three intents and only one of them is common:
    ///     </para>
    ///     <list type="table">
    ///         <item>
    ///             <term>unset</term>
    ///             <description>
    ///                 Inferred. With an <c>[Endpoint]</c> the operation is surface, so it is public;
    ///                 without one it is a step or a helper, so it stays on the <c>internal</c>
    ///                 interface. Not writing anything gives the answer that is right almost always.
    ///             </description>
    ///         </item>
    ///         <item>
    ///             <term><c>false</c></term>
    ///             <description>
    ///                 Public anyway. This is how an operation with no HTTP surface is offered to other
    ///                 modules — <c>IngestTextAction</c> is the case: never routed, called across a
    ///                 boundary every day.
    ///             </description>
    ///         </item>
    ///         <item>
    ///             <term><c>true</c></term>
    ///             <description>Internal anyway, endpoint or not.</description>
    ///         </item>
    ///     </list>
    ///     <para>
    ///         ⚠️ Unset is not <c>false</c> on purpose: with <c>false</c> as the default an operation
    ///         nobody meant to offer would sit on the interface other modules consume until someone remembered a flag
    ///         they did not know existed. Leaking by forgetting is the wrong way round.
    ///     </para>
    /// </remarks>
    /// <remarks>
    ///     Declared <c>bool</c> rather than <c>bool?</c> — a nullable is not a valid attribute argument
    ///     (CS0655). The three states are still there: the generator reads the attribute's named
    ///     arguments, so "written as false" and "not written" are different things to it, whatever the
    ///     language default is.
    /// </remarks>
    public bool Internal { get; set; }

    /// <summary>
    ///     When true, the action is a system/infrastructure action that:
    ///     - Does NOT appear in any Boundary interface
    ///     - Does NOT go through the pipeline by default (opt-in via attributes)
    ///     - CAN have [Endpoint] attribute for direct exposure
    /// </summary>
    /// <remarks>
    ///     Use for infrastructure operations like:
    ///     - File downloads/exports
    ///     - Health checks
    ///     - System diagnostics
    /// </remarks>
    public bool System { get; set; }
}
