namespace Pragmatic.Temporal.EFCore.Tests;

/// <summary>
///     An entity carrying plain BCL instants, which is what most applications store.
/// </summary>
/// <remarks>
///     Deliberately none of the temporal module's own types: the rule under test is about
///     <see cref="DateTimeOffset" /> and <see cref="DateTime" />, the two the serializer assumes are
///     UTC in the column.
/// </remarks>
public class InstantEntity
{
    public int Id { get; set; }

    public DateTimeOffset Moment { get; set; }

    public DateTimeOffset? NullableMoment { get; set; }

    public DateTime Stamp { get; set; }

    public DateTime? NullableStamp { get; set; }
}
