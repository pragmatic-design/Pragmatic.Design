using System.Collections;
using System.Runtime.CompilerServices;

namespace Pragmatic.Notifications;

/// <summary>
///     What an <see cref="Pipeline.IRecipientResolver" /> made of a recipient: the addresses it reached, and the recipients it could not
///     reach at all (<see cref="Unreached" />), which the notification's result names.
/// </summary>
/// <remarks>
///     A list of the resolved addresses, so a resolver that reaches everything writes <c>[recipient, …]</c> as before. Before #743 a user or
///     a role no resolver could turn into an address was logged and dropped: a send to nobody looked, to its caller, like a send.
/// </remarks>
[CollectionBuilder(typeof(RecipientResolution), nameof(Create))]
public sealed class RecipientResolution : IReadOnlyList<ResolvedRecipient>
{
    private readonly IReadOnlyList<ResolvedRecipient> _resolved;

    /// <param name="resolved">The addresses reached.</param>
    /// <param name="unreached">The recipients no address was found for, as the caller named them (<c>user 'u-1'</c>, <c>role 'ops'</c>).</param>
    public RecipientResolution(IReadOnlyList<ResolvedRecipient> resolved, IReadOnlyList<string>? unreached = null)
    {
        ArgumentNullException.ThrowIfNull(resolved);
        _resolved = resolved;
        Unreached = unreached ?? [];
    }

    /// <summary>The recipients no address was found for; empty when every one was reached.</summary>
    public IReadOnlyList<string> Unreached { get; }

    /// <inheritdoc />
    public int Count => _resolved.Count;

    /// <inheritdoc />
    public ResolvedRecipient this[int index] => _resolved[index];

    /// <summary>The collection expression's builder: the addresses reached, none unreached.</summary>
    public static RecipientResolution Create(ReadOnlySpan<ResolvedRecipient> resolved) => new(resolved.ToArray());

    /// <inheritdoc />
    public IEnumerator<ResolvedRecipient> GetEnumerator() => _resolved.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}
