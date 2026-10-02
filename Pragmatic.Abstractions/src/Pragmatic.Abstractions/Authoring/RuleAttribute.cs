namespace Pragmatic.Authoring;

/// <summary>
///     States a business rule, in natural language, that the annotated member must enforce.
///     Repeatable — one attribute per rule. Inert at runtime; read at compile time by the generated
///     use-case catalog, which carries the rules in the author's words as living specification next
///     to the code that realizes it.
/// </summary>
/// <remarks>
///     <para>
///         A rule written without a <see cref="UseCaseAttribute" /> beside it is still recorded, in
///         <c>PragmaticUseCases.RulesWithoutAUseCase</c> rather than in <c>All</c>. Dropping it would
///         be how this attribute spent its first year: declared, documented and read by nobody.
///     </para>
///     Pairs with <see cref="UseCaseAttribute"/> for traceability:
///     <code>
///     [UseCase("DRG-DISPENSE")]
///     [Rule("Quantity to dispense must be positive")]
///     [Rule("Stock on hand cannot go below zero")]
///     public VoidResult Dispense(int quantity) => throw Behavior.Pending();
///     </code>
/// </remarks>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = true, Inherited = false)]
public sealed class RuleAttribute(string text) : Attribute
{
    /// <summary>The business rule, phrased for a human reader.</summary>
    public string Text { get; } = text;
}
