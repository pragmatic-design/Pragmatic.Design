using Pragmatic.Internationalization.Types;

namespace Pragmatic.Internationalization.EntityFrameworkCore.ValueConverters;

/// <summary>
///     Provides configuration helpers for <see cref="Money" /> properties in EF Core.
/// </summary>
/// <remarks>
///     <para>
///         Since Money is a value type (struct), it cannot be used with EF Core's owned entities.
///         Instead, you can store Money as separate Amount and Currency columns using Shadow Properties
///         or map the components directly in your entity.
///     </para>
///     <para>
///         <b>Recommended approach:</b> Store Amount (decimal) and CurrencyCode (string) as separate
///         properties in your entity and use them to construct Money in your domain logic.
///     </para>
/// </remarks>
/// <example>
///     <code>
/// // Entity with separate properties
/// public class Order
/// {
///     public int Id { get; set; }
///     public decimal TotalAmount { get; set; }
///     public CurrencyCode TotalCurrency { get; set; }
/// 
///     // Computed property (not mapped)
///     public Money Total => Money.From(TotalAmount, TotalCurrency);
/// }
/// 
/// // Configure in OnModelCreating
/// modelBuilder.Entity&lt;Order&gt;()
///     .Property(e => e.TotalAmount).HasPrecision(19, 4);
/// modelBuilder.Entity&lt;Order&gt;()
///     .Property(e => e.TotalCurrency).HasMaxLength(3);
/// </code>
/// </example>
public static class MoneyConfiguration
{
    /// <summary>
    ///     Default precision for decimal amount storage.
    /// </summary>
    public const int DefaultPrecision = 19;

    /// <summary>
    ///     Default scale for decimal amount storage.
    /// </summary>
    public const int DefaultScale = 4;
}