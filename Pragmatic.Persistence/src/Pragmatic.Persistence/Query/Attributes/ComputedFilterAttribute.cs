namespace Pragmatic.Persistence.Query.Attributes;

/// <summary>
///     Marks a boolean computed property, or a boolean method, as a reusable query filter.
///     Implies <see cref="ProjectableAttribute"/> — also generates the Expression in the Expr class.
///     Additionally generates a <c>Specification&lt;T&gt;</c> and a <c>Where{Name}()</c> extension method.
/// </summary>
/// <remarks>
///     <para>
///         Only boolean expression-bodied members are supported:
///         <c>public bool IsOverdue =&gt; DueDate &lt; DateTimeOffset.UtcNow &amp;&amp; Status != InvoiceStatus.Paid;</c>
///     </para>
///     <para>
///         A method takes the values the rule needs from outside the row — a day, a threshold — and the
///         specification and the extension take them too:
///         <c>[ComputedFilter] public bool IsAwayOn(DateOnly day) =&gt; …</c> generates
///         <c>IsAwayOnSpec(DateOnly day)</c> and <c>query.WhereAwayOn(day)</c>. It is the form for a rule
///         over "today": the caller passes the date from the application's clock, where
///         <c>DateTime.UtcNow</c> in the body would be the database's.
///     </para>
///     <para>
///         <c>PRAG0733</c> reports a member the generator cannot write: not <c>bool</c>, static, generic,
///         without an expression body, or with a <c>ref</c>/<c>out</c>/<c>in</c>/<c>params</c> parameter.
///     </para>
/// </remarks>
/// <example>
///     <code>
/// public partial class Invoice
/// {
///     [ComputedFilter]
///     public bool IsOverdue =&gt; DueDate &lt; DateTimeOffset.UtcNow &amp;&amp; Status != InvoiceStatus.Paid;
///
///     [ComputedFilter]
///     public bool IsDueBy(DateOnly day) =&gt; DueOn &lt;= day &amp;&amp; Status != InvoiceStatus.Paid;
/// }
///
/// // Generated:
/// // 1. Invoice.Expr.IsOverdue (Expression, from [Projectable])
/// // 2. InvoiceComputedFilters.IsOverdueSpec (Specification&lt;Invoice&gt;)
/// // 3. query.WhereOverdue() extension method
/// // 4. InvoiceComputedFilters.IsDueBySpec(DateOnly day) and query.WhereDueBy(day)
/// </code>
/// </example>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Method)]
public sealed class ComputedFilterAttribute : Attribute
{
}
