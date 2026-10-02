using Pragmatic.Internationalization.Types;

namespace Pragmatic.Internationalization.Samples.Scenarios.EFCore;

/// <summary>
///     Sample entity exercising the I18N EF Core value converter for
///     <see cref="CurrencyCode"/> (stored as a varchar(3) ISO 4217 code).
/// </summary>
public sealed class ProductEntity
{
    public int Id { get; set; }

    public string Name { get; set; } = "";

    public CurrencyCode Currency { get; set; }
}
