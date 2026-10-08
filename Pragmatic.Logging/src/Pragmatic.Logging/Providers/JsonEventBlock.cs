namespace Pragmatic.Logging.Providers;

/// <summary>
///     The JSON event block of one call site, held by the call site's state type: a static field per
///     <typeparamref name="TState" />, read with no lookup, as <c>Utf8LogStateWriters&lt;TState&gt;</c> is.
/// </summary>
/// <remarks>
///     A generated state belongs to one call site, so its event and template never change. The entry still
///     carries what it was encoded from, and a call that does not match is written field by field and the entry
///     replaced. Shared by every JSON provider: they write with the same field names and encoder, and only lines
///     that do not indent use it.
/// </remarks>
internal static class JsonEventBlock<TState>
{
    public static JsonEventBlockEntry? Entry;
}
