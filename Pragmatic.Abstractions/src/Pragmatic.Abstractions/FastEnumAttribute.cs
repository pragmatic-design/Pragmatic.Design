namespace Pragmatic;

/// <summary>
///     Marks an enum for source-generated fast helpers.
///     Generates extension methods: <c>ToStringFast()</c>, <c>IsDefined()</c>,
///     <c>TryParse()</c>, <c>GetValues()</c>, <c>GetNames()</c>, plus an AOT-safe
///     <c>JsonConverter</c>.
/// </summary>
/// <remarks>
///     <para>
///         Every generated member is a <c>switch</c> over the declared values: no reflection, no
///         boxing, and no allocation beyond the interned member names the switch returns.
///     </para>
///     <para>
///         Beyond the five methods above, the generator also emits a <c>JsonConverter</c> that
///         serializes the enum as its string name with no reflection — usable under Native AOT and
///         trimming — an <c>IsDefined(string)</c> overload, and a case-insensitive
///         <c>TryParse(string?, bool ignoreCase, out T)</c>.
///     </para>
///     <example>
///         <code>
///         [FastEnum]
///         public enum OrderStatus
///         {
///             Pending,
///             Confirmed,
///             Cancelled
///         }
///
///         // Generated:
///         OrderStatus.Pending.ToStringFast()     // "Pending" (no reflection)
///         OrderStatusExtensions.IsDefined("Confirmed")  // true (no boxing)
///         OrderStatusExtensions.TryParse("Cancelled", out var status)  // true
///         OrderStatusExtensions.GetValues()       // ReadOnlySpan&lt;OrderStatus&gt;
///         OrderStatusExtensions.GetNames()        // ReadOnlySpan&lt;string&gt;
///         </code>
///     </example>
/// </remarks>
[AttributeUsage(AttributeTargets.Enum)]
public sealed class FastEnumAttribute : Attribute;
