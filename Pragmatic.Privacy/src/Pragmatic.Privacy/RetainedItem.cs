namespace Pragmatic.Privacy;

/// <summary>
///     Something kept despite an erasure request, and the obligation that justifies keeping it.
/// </summary>
/// <param name="What">What was kept, in terms the subject can be told — not a column name.</param>
/// <param name="Reason">The obligation. Never empty: retaining without saying why is the same as forgetting to erase.</param>
/// <param name="RequiresKey">
///     Whether keeping it needs the subject's encryption key. A value kept in the clear does not, and
///     must not stop the key from being destroyed: that would leave readable every field whose erasure
///     <em>is</em> the key's destruction. A legal hold, or a value encrypted under the subject's key, does.
///     <see langword="true" /> unless the producer knows otherwise — keeping a key too long is the
///     recoverable mistake.
/// </param>
public readonly record struct RetainedItem(string What, string Reason, bool RequiresKey = true);
