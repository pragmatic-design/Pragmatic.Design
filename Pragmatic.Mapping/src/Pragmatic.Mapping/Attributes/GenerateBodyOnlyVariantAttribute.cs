namespace Pragmatic.Mapping.Attributes;

/// <summary>
///     Generates an additional <c>FromEntityBodyOnly()</c> method that maps only
///     scalar (body) properties, skipping collections, nested DTOs, and dictionaries.
///     <para>
///         Useful for mutation scenarios where you want to map simple properties
///         independently from navigation and collection handling.
///     </para>
/// </summary>
/// <example>
///     <code>
///     [MapFrom&lt;Order&gt;]
///     [GenerateBodyOnlyVariant]
///     public partial record OrderDto
///     {
///         public string OrderNumber { get; init; }   // Mapped in both
///         public decimal Total { get; init; }         // Mapped in both
///         public AddressDto? Address { get; init; }   // Skipped in BodyOnly
///         public List&lt;LineDto&gt; Lines { get; init; }   // Skipped in BodyOnly
///     }
///
///     // Generated: FromEntity() maps everything
///     // Generated: FromEntityBodyOnly() maps only OrderNumber + Total
///     </code>
/// </example>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct, Inherited = false)]
public sealed class GenerateBodyOnlyVariantAttribute : Attribute
{
}
