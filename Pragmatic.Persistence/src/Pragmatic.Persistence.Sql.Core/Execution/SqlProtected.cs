namespace Pragmatic.Persistence.Sql.Execution;

/// <summary>
///     A text column stored protected (<c>[Convert&lt;SqlProtected&gt;]</c>): written through <see cref="SqlProtection.Current" />'s Protect, read
///     through its Unprotect; a predicate's value is protected the same way, so an equality matches only with a deterministic protector (the same
///     value, the same stored text). Ordering, LIKE and ranges read the stored text, not the value.
/// </summary>
public sealed class SqlProtected : ISqlConverter<string, string>
{
    public static string ToStore(string value) => SqlProtection.Current.Protect(value);

    public static string FromStore(string value) => SqlProtection.Current.Unprotect(value);
}
