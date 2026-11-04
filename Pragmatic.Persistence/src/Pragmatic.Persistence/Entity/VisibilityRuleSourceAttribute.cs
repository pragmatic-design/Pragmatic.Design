using System.ComponentModel;

namespace Pragmatic.Persistence.Entity;

/// <summary>
///     The predicate of a <c>VisibilityRule&lt;T&gt;</c> declared in this assembly, as source, for an engine that translates the rule in
///     another assembly. Written by the source generator; not meant to be written by hand.
/// </summary>
/// <typeparam name="TRule">The rule.</typeparam>
/// <param name="predicate">
///     The rule's <c>ToExpression</c> lambda, with every name it reads fully qualified, so it binds wherever it is compiled.
/// </param>
/// <remarks>
///     <para>
///         An engine compiles a rule into the statements that read its entity, and it translates the rule from its source. A module that
///         reads another module's entity (<c>[ReadAccess&lt;T&gt;]</c>) sees that entity, and its rules, as metadata, which carries no
///         method body: the rule's predicate would be untranslatable there, and the entity unreadable. The assembly that declares the rule
///         therefore states its predicate here, and the reading one translates it as if the rule were its own.
///     </para>
///     <para>
///         EF Core does not need it: it reads the rule's expression tree at run time.
///     </para>
/// </remarks>
[EditorBrowsable(EditorBrowsableState.Never)]
[AttributeUsage(AttributeTargets.Assembly, AllowMultiple = true)]
public sealed class VisibilityRuleSourceAttribute<TRule>(string predicate) : Attribute
    where TRule : class
{
    /// <summary>The rule's predicate, as source.</summary>
    public string Predicate { get; } = predicate;
}
